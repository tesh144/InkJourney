"""
Patches existing custom Mapbox styles to hide POI/transit labels
while keeping road names, place names, and water labels visible.

Run:  python3 patch_mapbox_labels.py
"""

import urllib.request
import json
import ssl

_ctx = ssl.create_default_context()
_ctx.check_hostname = False
_ctx.verify_mode = ssl.CERT_NONE

MAPBOX_TOKEN = "pk.eyJ1IjoidGVzaDE0NCIsImEiOiJjbW55ZDZudzMwMHZyMnBzYWh2M3Yxb2V1In0.b9_qzGiE2GdpL7TfVPgaGA"
USERNAME     = "tesh144"

# Your three custom style IDs — update if you regenerate styles
STYLES = {
    "Spooky": "cmnyeo2uq000001r3bos37tkt",
    "Dark":   "cmnyeit6n000d01sd5s13hg1n",
    "Light":  "cmnyel9ms000a01qzcokob5lm",
}

# Layer IDs containing any of these substrings will be hidden.
# "road-label" does NOT contain any of these, so street names are kept.
HIDE_IF_CONTAINS = [
    "poi",           # poi-label, poi-label-sm, etc.
    "transit",       # transit-label
    "airport",       # airport-label
]

# Also hide these exact layer IDs (add more as needed)
HIDE_EXACT = set()


def fetch_style(style_id):
    url = f"https://api.mapbox.com/styles/v1/{USERNAME}/{style_id}?access_token={MAPBOX_TOKEN}"
    with urllib.request.urlopen(url, timeout=30, context=_ctx) as r:
        return json.loads(r.read())


def put_style(style_id, style_data):
    """Replace the style in-place (PUT keeps the same style ID)."""
    url = f"https://api.mapbox.com/styles/v1/{USERNAME}/{style_id}?access_token={MAPBOX_TOKEN}"
    # Strip server-assigned fields before sending
    for key in ("id", "created", "modified", "owner", "draft", "protected", "visibility"):
        style_data.pop(key, None)
    body = json.dumps(style_data).encode()
    req  = urllib.request.Request(url, data=body,
                                   headers={"Content-Type": "application/json"},
                                   method="PUT")
    with urllib.request.urlopen(req, timeout=30, context=_ctx) as r:
        return json.loads(r.read())


def should_hide(layer_id):
    lid = layer_id.lower()
    if lid in HIDE_EXACT:
        return True
    return any(kw in lid for kw in HIDE_IF_CONTAINS)


def patch_labels(style):
    hidden, kept = [], []
    for layer in style.get("layers", []):
        lid = layer.get("id", "")
        if should_hide(lid):
            layer.setdefault("layout", {})["visibility"] = "none"
            hidden.append(lid)
        else:
            kept.append(lid)
    return style, hidden


def main():
    for name, style_id in STYLES.items():
        print(f"\nPatching {name} ({style_id})…")
        style = fetch_style(style_id)
        style, hidden_layers = patch_labels(style)

        print(f"  Hidden {len(hidden_layers)} layers:")
        for lid in hidden_layers:
            print(f"    - {lid}")

        put_style(style_id, style)
        print(f"  ✓ {name} updated in place")

    print("\nDone. Street names, place names, and water labels are untouched.")


if __name__ == "__main__":
    main()
