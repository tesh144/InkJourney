"""
Creates three custom Mapbox Classic styles (Spooky, Dark, Light) by downloading
dark-v11 and light-v11, applying colour transforms, and saving to the account.

Run:  python3 create_mapbox_styles.py
"""

import urllib.request
import json
import ssl
import re

_ctx = ssl.create_default_context()
_ctx.check_hostname = False
_ctx.verify_mode = ssl.CERT_NONE

MAPBOX_TOKEN = "pk.eyJ1IjoidGVzaDE0NCIsImEiOiJjbW55ZDZudzMwMHZyMnBzYWh2M3Yxb2V1In0.b9_qzGiE2GdpL7TfVPgaGA"
USERNAME     = "tesh144"


# ── API helpers ────────────────────────────────────────────────────────────────

def fetch_style(style_path):
    url = f"https://api.mapbox.com/styles/v1/{style_path}?access_token={MAPBOX_TOKEN}"
    with urllib.request.urlopen(url, timeout=30, context=_ctx) as r:
        return json.loads(r.read())


def save_style(style_data):
    url = f"https://api.mapbox.com/styles/v1/{USERNAME}?access_token={MAPBOX_TOKEN}"
    body = json.dumps(style_data).encode()
    req  = urllib.request.Request(url, data=body,
                                   headers={"Content-Type": "application/json"},
                                   method="POST")
    with urllib.request.urlopen(req, timeout=30, context=_ctx) as r:
        result = json.loads(r.read())
        return result.get("id", "unknown")


def strip_metadata(style):
    """Remove server-assigned fields so Mapbox accepts the style as new."""
    for key in ("id", "created", "modified", "owner", "draft", "protected", "visibility"):
        style.pop(key, None)
    return style


# ── Colour transform helpers ───────────────────────────────────────────────────

HSL_RE  = re.compile(r'hsl\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)%,\s*(\d+(?:\.\d+)?)%\)')
HSLA_RE = re.compile(r'hsla\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)%,\s*(\d+(?:\.\d+)?)%,\s*(\d+(?:\.\d+)?)\)')


def recolor(json_str, fn):
    """Apply fn(h, s, l, a=None) → color string to every HSL/HSLA in json_str."""
    out = HSL_RE.sub(
        lambda m: fn(float(m.group(1)), float(m.group(2)), float(m.group(3))),
        json_str
    )
    out = HSLA_RE.sub(
        lambda m: fn(float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4))),
        out
    )
    return out


# ── Spooky theme: dark navy → deep purple, labels → app accent (#c17bff) ──────

def spooky(h, s, l, a=None):
    wrap   = "hsla" if a is not None else "hsl"
    suffix = f", {a}" if a is not None else ""

    if l < 8:       # deepest darks (water, voids) → near-black navy
        return f"{wrap}(220, 55%, {l:.1f}%{suffix})"
    elif l < 22:    # backgrounds, buildings → dark navy
        nl = round(l * 0.72, 1)
        return f"{wrap}(222, 47%, {nl}%{suffix})"
    elif l < 42:    # minor roads, mid elements → dark purple
        nl = round(l * 0.85, 1)
        return f"{wrap}(268, 38%, {nl}%{suffix})"
    elif l < 65:    # medium-bright elements → mid purple
        nl = round(l * 0.78, 1)
        return f"{wrap}(270, 32%, {nl}%{suffix})"
    else:           # labels / bright highlights → app accent purple
        nl = round(l * 0.88, 1)
        return f"{wrap}(275, 80%, {nl}%{suffix})"


# ── Main ───────────────────────────────────────────────────────────────────────

def main():
    print("Fetching base styles from Mapbox…")
    dark_base  = fetch_style("mapbox/dark-v11")
    light_base = fetch_style("mapbox/light-v11")
    print("  ✓ dark-v11 fetched")
    print("  ✓ light-v11 fetched\n")

    results = {}

    # ── Spooky ────────────────────────────────────────────────────────────────
    print("Creating Spooky style…")
    spooky_data = strip_metadata(json.loads(json.dumps(dark_base)))
    spooky_data["name"] = "InkJourney_Spooky"
    spooky_str  = recolor(json.dumps(spooky_data), spooky)
    spooky_id   = save_style(json.loads(spooky_str))
    results["Spooky"] = f"tesh144/{spooky_id}"
    print(f"  ✓ Spooky → tesh144/{spooky_id}")

    # ── Dark (dark-v11 as-is, just renamed) ───────────────────────────────────
    print("Creating Dark style…")
    dark_data = strip_metadata(json.loads(json.dumps(dark_base)))
    dark_data["name"] = "InkJourney_Dark"
    dark_id   = save_style(dark_data)
    results["Dark"] = f"tesh144/{dark_id}"
    print(f"  ✓ Dark   → tesh144/{dark_id}")

    # ── Light (light-v11 as-is, just renamed) ─────────────────────────────────
    print("Creating Light style…")
    light_data = strip_metadata(json.loads(json.dumps(light_base)))
    light_data["name"] = "InkJourney_Light"
    light_id   = save_style(light_data)
    results["Light"] = f"tesh144/{light_id}"
    print(f"  ✓ Light  → tesh144/{light_id}")

    print("\n" + "="*60)
    print("Paste these into Unity Inspector → MapStyleEntry.styleString:")
    print("="*60)
    for name, style_id in results.items():
        print(f"  {name:<8} →  {style_id}")
    print()
    print("Also update MapBackground.cs with the new Spooky style ID.")
    print(f"  if (style.Contains(\"{spooky_id}\")) return new Color(0.050f, 0.063f, 0.110f);")


if __name__ == "__main__":
    main()
