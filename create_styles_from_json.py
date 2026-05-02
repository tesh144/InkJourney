"""
Creates three InkJourney Mapbox styles (Light, Dark, Spooky) from a downloaded
base style JSON. Deletes all existing InkJourney_* styles first.

Usage:
    cd /Users/tesh/InkJourney
    python3 create_styles_from_json.py style.json
"""

import json, re, sys, colorsys, urllib.request, ssl

_ctx = ssl.create_default_context()
_ctx.check_hostname = False
_ctx.verify_mode = ssl.CERT_NONE

MAPBOX_TOKEN = "sk.eyJ1IjoidGVzaDE0NCIsImEiOiJjbW55bDQyZ3kwMjQ2MnNzZDRuaWJxMXNvIn0.NESlfjrKnQbdKKjRtY3BGQ"
USERNAME     = "tesh144"

HEX_RE = re.compile(r'"(#[0-9a-fA-F]{6})"')


# ── Colour helpers ─────────────────────────────────────────────────────────────

def hex_to_hsl(h):
    r = int(h[1:3], 16) / 255
    g = int(h[3:5], 16) / 255
    b = int(h[5:7], 16) / 255
    hue, lum, sat = colorsys.rgb_to_hls(r, g, b)
    return hue, sat, lum


def hsl_to_hex(h, s, l):
    h = h % 1.0
    s = max(0.0, min(1.0, s))
    l = max(0.0, min(1.0, l))
    r, g, b = colorsys.hls_to_rgb(h, l, s)
    return '#{:02x}{:02x}{:02x}'.format(
        int(round(r * 255)), int(round(g * 255)), int(round(b * 255)))


def recolor(json_str, fn):
    return HEX_RE.sub(lambda m: f'"{fn(m.group(1))}"', json_str)


# ── Theme transforms ───────────────────────────────────────────────────────────

def dark_fn(hex_color):
    h, s, l = hex_to_hsl(hex_color)
    return hsl_to_hex(h, s, 1.0 - l)


def spooky_fn(hex_color):
    """Same inverted-lightness base as dark — roads/labels overridden separately."""
    h, s, l = hex_to_hsl(hex_color)
    return hsl_to_hex(h, s, 1.0 - l)


# ── Per-theme post-processing ──────────────────────────────────────────────────

def post_process_spooky(style):
    """Override roads to dark amber/brown, water to dark navy."""
    for layer in style.get("layers", []):
        lid = layer.get("id", "")
        if lid in ("road-simple", "tunnel-simple", "bridge-simple", "bridge-case-simple"):
            layer["paint"]["line-color"] = "#6b5228"   # dark amber roads
        elif lid in ("road-rail", "bridge-rail"):
            layer["paint"]["line-color"] = "#3d3020"
        elif lid == "water":
            layer["paint"]["fill-color"] = "#060d14"   # very dark navy water
        elif lid == "waterway":
            layer["paint"]["line-color"] = "#04090e"
        elif lid == "land":
            # Force dark blue-grey land at all zoom levels instead of interpolated greys
            layer["paint"]["background-color"] = "#1a2130"
    return style


def post_process_light(style):
    """Parks green, tunnels visible, bridges match roads, rail darker than roads."""
    road_color = next(
        (l["paint"].get("line-color") for l in style.get("layers", [])
         if l.get("id") == "road-simple" and isinstance(l.get("paint", {}).get("line-color"), str)),
        "#9cafba")

    for layer in style.get("layers", []):
        lid = layer.get("id", "")
        if lid == "national-park":
            layer["paint"]["fill-color"] = "#a4b79e"
        elif lid == "landuse":
            layer["paint"]["fill-color"] = [
                "match", ["get", "class"],
                ["wood", "grass", "scrub", "park", "pitch"], "#a4b79e",
                "agriculture", "#b0c0aa",
                "#d9dfe3"
            ]
        elif lid == "tunnel-simple":
            layer["paint"]["line-color"] = "#a0a8b0"
        elif lid in ("bridge-simple", "bridge-case-simple"):
            layer["paint"]["line-color"] = road_color
        elif lid in ("road-rail", "bridge-rail"):
            layer["paint"]["line-color"] = "#5a7080"   # darker than roads
    return style


def post_process_dark(style):
    """Tunnels visible, bridges match roads, rail darker than roads."""
    road_color = next(
        (l["paint"].get("line-color") for l in style.get("layers", [])
         if l.get("id") == "road-simple" and isinstance(l.get("paint", {}).get("line-color"), str)),
        "#3c5060")

    for layer in style.get("layers", []):
        lid = layer.get("id", "")
        if lid == "tunnel-simple":
            layer["paint"]["line-color"] = "#3a4048"
        elif lid in ("bridge-simple", "bridge-case-simple"):
            layer["paint"]["line-color"] = road_color
        elif lid in ("road-rail", "bridge-rail"):
            layer["paint"]["line-color"] = "#2e4256"   # visible against dark bg, secondary to roads
    return style


# ── Label sizes ────────────────────────────────────────────────────────────────

def fix_label_sizes(style):
    """Set road label sizes appropriate for our render zoom (~14-15).
    Base style stops at zoom 3 which extrapolates to huge sizes beyond that."""
    for layer in style.get("layers", []):
        if layer.get("id") == "road-label-simple":
            layer["layout"]["text-size"] = [
                "interpolate", ["linear"], ["zoom"],
                12, ["match", ["get", "class"],
                     ["motorway", "trunk", "primary", "secondary", "tertiary"], 6, 5],
                15, ["match", ["get", "class"],
                     ["motorway", "trunk", "primary", "secondary", "tertiary"], 7, 6],
                18, ["match", ["get", "class"],
                     ["motorway", "trunk", "primary", "secondary", "tertiary"], 8, 7],
            ]
    return style


# ── API helpers ────────────────────────────────────────────────────────────────

def list_styles():
    url = f"https://api.mapbox.com/styles/v1/{USERNAME}?access_token={MAPBOX_TOKEN}&limit=100"
    with urllib.request.urlopen(url, timeout=30, context=_ctx) as r:
        return json.loads(r.read())


def delete_style(style_id):
    url = f"https://api.mapbox.com/styles/v1/{USERNAME}/{style_id}?access_token={MAPBOX_TOKEN}"
    req = urllib.request.Request(url, method="DELETE")
    with urllib.request.urlopen(req, timeout=30, context=_ctx) as r:
        return r.status


def strip_metadata(style):
    for key in ("id", "created", "modified", "owner", "draft", "protected", "visibility"):
        style.pop(key, None)
    return style


def post_style(style_data):
    url  = f"https://api.mapbox.com/styles/v1/{USERNAME}?access_token={MAPBOX_TOKEN}"
    body = json.dumps(style_data).encode()
    req  = urllib.request.Request(url, data=body,
                                   headers={"Content-Type": "application/json"},
                                   method="POST")
    with urllib.request.urlopen(req, timeout=30, context=_ctx) as r:
        return json.loads(r.read()).get("id", "unknown")


# ── Main ───────────────────────────────────────────────────────────────────────

def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "style.json"
    with open(path) as f:
        base_str = f.read()

    # Delete all previously uploaded InkJourney styles
    OLD_IDS = [
        # Session 1
        "cmnyeo2uq000001r3bos37tkt", "cmnyeit6n000d01sd5s13hg1n", "cmnyel9ms000a01qzcokob5lm",
        # Session 2
        "cmnyl4nd4000x01scbshahike", "cmnyl4nw2001801s9bjl8bain", "cmnyl4q4i000l01sibu2hdtu1",
        # Session 3
        "cmnyl8v5z000q01sd32sbarye", "cmnyl8vuq001f01sb91t9652u", "cmnyl8wpd001o01sd90d6bp1w",
        # Session 4
        "cmnylcfrj000z01sc7k1m9n81", "cmnylcgad000k01sgh15vgayg", "cmnylcgtf001x01s96zshd32f",
        # Session 5 (intermediate spooky)
        "cmnylc3ea000001saaxa99ice",
        # Session 6
        "cmnylpkj8000g01sdfpq2gkx3", "cmnylpl2i000h01sd3utg0s7h", "cmnylplke001001scgb5oekq8",
        # Session 7
        "cmnym2de1001y01s96j6ic06s", "cmnym2e70001l01qoccj14hmx", "cmnym2en7001e01secu625p6c",
        # Session 8
        "cmnymd7s3001a01s639irfsiz", "cmnymd8bx001o01qogarufg2t", "cmnymd8to000o01si2o5l3px8",
        # Session 9
        "cmnymezm7001j01p7cjypbzaz", "cmnymf09m000201sa452p0kw1", "cmnymf12p001k01p74lq6a2cm",
        # Session 10
        "cmnyn70ei001n01p7a31vferr", "cmnyn714k001b01s97gtx2itz", "cmnyn71rz001h01sehmjd45cl",
        # Session 11
        "cmnynki54000k01sdb2ze8efe", "cmnynkjpy001s01sd9bd99be2", "cmnynkkt5001r01qo0bzt4m23",
        # Session 12
        "cmnynqcnm000l01sd1b42hs2n", "cmnynqczv000h01r3dwc72n1e", "cmnynqdfj000m01sd7aqq2bfk",
    ]
    print("Deleting old InkJourney styles…")
    for sid in OLD_IDS:
        try:
            delete_style(sid)
            print(f"  ✗ Deleted {sid}")
        except Exception:
            pass  # already deleted or never existed

    results = {}

    # Light
    print("\nUploading Light…")
    light = post_process_light(fix_label_sizes(strip_metadata(json.loads(base_str))))
    light["name"] = "InkJourney_Light"
    light_id = post_style(light)
    results["Light"] = f"tesh144/{light_id}"
    print(f"  ✓ Light  → tesh144/{light_id}")

    # Dark
    print("Uploading Dark…")
    dark = post_process_dark(fix_label_sizes(strip_metadata(json.loads(recolor(base_str, dark_fn)))))
    dark["name"] = "InkJourney_Dark"
    dark_id = post_style(dark)
    results["Dark"] = f"tesh144/{dark_id}"
    print(f"  ✓ Dark   → tesh144/{dark_id}")

    # Spooky
    print("Uploading Spooky…")
    spooky = post_process_spooky(
        fix_label_sizes(strip_metadata(json.loads(recolor(base_str, spooky_fn)))))
    spooky["name"] = "InkJourney_Spooky"
    spooky_id = post_style(spooky)
    results["Spooky"] = f"tesh144/{spooky_id}"
    print(f"  ✓ Spooky → tesh144/{spooky_id}")

    print("\n" + "=" * 60)
    print("Paste into Unity Inspector → MapStyleEntry.styleString:")
    print("=" * 60)
    for name, sid in results.items():
        print(f"  {name:<8} →  {sid}")
    print()
    print("Background colours to set in Inspector:")
    print("  Light    →  #E3E3E3")
    print("  Dark     →  #191B1C")
    print("  Spooky   →  #0e1f2a")


if __name__ == "__main__":
    main()
