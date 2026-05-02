"""
Fetches all Landmarks from Firestore, appends an official website URL
to the content of each one, and writes it back.

Run:  python3 update_landmark_links.py
"""

import urllib.request
import urllib.error
import json
import ssl

_ctx = ssl.create_default_context()
_ctx.check_hostname = False
_ctx.verify_mode = ssl.CERT_NONE

PROJECT_ID = "inkjourney"
API_KEY    = "AIzaSyB8do4t79xVew8sE52mQM7Yveo5SUPYQdQ"
BASE      = f"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}/databases/(default)/documents"

# ── Official URLs keyed by landmark title (case-insensitive substring match) ──
LINKS = {
    "tower of london":          "https://www.hrp.org.uk/tower-of-london/",
    "tower bridge":             "https://www.towerbridge.org.uk/",
    "st paul":                  "https://www.stpauls.co.uk/",
    "westminster abbey":        "https://www.westminster-abbey.org/",
    "buckingham palace":        "https://www.rct.uk/visit/buckingham-palace",
    "british museum":           "https://www.britishmuseum.org/",
    "national gallery":         "https://www.nationalgallery.org.uk/",
    "tate modern":              "https://www.tate.org.uk/visit/tate-modern",
    "tate britain":             "https://www.tate.org.uk/visit/tate-britain",
    "natural history museum":   "https://www.nhm.ac.uk/",
    "science museum":           "https://www.sciencemuseum.org.uk/",
    "victoria and albert":      "https://www.vam.ac.uk/",
    "v&a":                      "https://www.vam.ac.uk/",
    "london eye":               "https://www.londoneye.com/",
    "the shard":                "https://www.theviewfromtheshard.com/",
    "hyde park":                "https://www.royalparks.org.uk/parks/hyde-park",
    "kensington gardens":       "https://www.royalparks.org.uk/parks/kensington-gardens",
    "regent's park":            "https://www.royalparks.org.uk/parks/the-regents-park",
    "greenwich":                "https://www.rmg.co.uk/royal-observatory",
    "royal observatory":        "https://www.rmg.co.uk/royal-observatory",
    "cutty sark":               "https://www.rmg.co.uk/cutty-sark",
    "national maritime":        "https://www.rmg.co.uk/national-maritime-museum",
    "hampton court":            "https://www.hrp.org.uk/hampton-court-palace/",
    "kew gardens":              "https://www.kew.org/",
    "shakespeare's globe":      "https://www.shakespearesglobe.com/",
    "globe theatre":            "https://www.shakespearesglobe.com/",
    "southbank":                "https://www.southbankcentre.co.uk/",
    "south bank":               "https://www.southbankcentre.co.uk/",
    "royal albert hall":        "https://www.royalalberthall.com/",
    "trafalgar square":         "https://www.london.gov.uk/visit/trafalgar-square",
    "covent garden":            "https://www.coventgarden.london/",
    "borough market":           "https://boroughmarket.org.uk/",
    "leadenhall market":        "https://www.leadenhallmarket.co.uk/",
    "columbia road":            "https://columbiaroad.info/",
    "portobello":               "https://www.portobelloroad.co.uk/",
    "brick lane":               "https://bricklane.org/",
    "camden market":            "https://www.camdenmarket.com/",
    "canary wharf":             "https://canarywharf.com/",
    "st katharine":             "https://www.skdocks.co.uk/",
    "museum of london":         "https://www.museumoflondon.org.uk/",
    "london bridge":            "https://www.southwark.gov.uk/",
    "millennium bridge":        "https://www.thamespathway.com/",
    "albert bridge":            "https://historicengland.org.uk/listing/the-list/list-entry/1357039",
    "chelsea physic":           "https://chelseaphysicgarden.co.uk/",
    "golden goose":             "https://goldengooselondon.com/",
    "oval":                     "https://www.kiaoval.com/",
    "brixton market":           "https://brixtonmarket.net/",
    "crystal palace":           "https://www.crystalpalacepark.org.uk/",
    "horniman":                 "https://www.horniman.ac.uk/",
    "dulwich":                  "https://www.dulwichpicturegallery.org.uk/",
}


def find_url(title):
    t = title.lower()
    for keyword, url in LINKS.items():
        if keyword in t:
            return url
    return None


def firestore_patch(path, fields):
    field_mask = "&".join(f"updateMask.fieldPaths={k}" for k in fields)
    url = f"{BASE}/{path}?key={API_KEY}&{field_mask}"
    body = json.dumps({"fields": fields}).encode()
    req = urllib.request.Request(url, data=body,
                                  headers={"Content-Type": "application/json"},
                                  method="PATCH")
    with urllib.request.urlopen(req, timeout=20, context=_ctx) as r:
        return r.getcode()


def list_landmarks():
    url = f"{BASE}/Landmarks?key={API_KEY}"
    docs = []
    page_token = None
    while True:
        paged = url + (f"&pageToken={page_token}" if page_token else "")
        req = urllib.request.Request(paged)
        with urllib.request.urlopen(req, timeout=20, context=_ctx) as r:
            data = json.loads(r.read())
        docs.extend(data.get("documents", []))
        page_token = data.get("nextPageToken")
        if not page_token:
            break
    return docs


def maps_url(lat, lon, title):
    import urllib.parse
    query = urllib.parse.quote(f"{title}, London")
    return f"https://www.google.com/maps/search/?api=1&query={query}"


def get_str(fields, key):
    return fields.get(key, {}).get("stringValue", "")


def main():
    print("Fetching landmarks…\n")
    docs = list_landmarks()
    print(f"Found {len(docs)} landmarks.\n")

    updated = 0
    skipped = 0

    for doc in docs:
        fields  = doc.get("fields", {})
        title   = get_str(fields, "Title")
        content = get_str(fields, "Content")
        lat     = fields.get("Latitude",  {}).get("doubleValue", 0)
        lon     = fields.get("Longitude", {}).get("doubleValue", 0)
        path    = doc["name"].split("/documents/")[1]

        # Strip any previously added link lines so we can rewrite them cleanly
        lines = content.split("\n")
        lines = [l for l in lines if not l.startswith("Website:") and not l.startswith("Google Maps:")]
        content = "\n".join(lines).rstrip()

        website = find_url(title)
        gmaps   = maps_url(lat, lon, title)

        link_lines = ""
        if website:
            link_lines += f"\nWebsite: {website}"
        link_lines += f"\nGoogle Maps: {gmaps}"

        new_content = content.rstrip() + link_lines

        try:
            firestore_patch(path, {
                "Content": {"stringValue": new_content}
            })
            print(f"  OK  {title}")
            if website:
                print(f"       Website:     {website}")
            print(f"       Google Maps: {gmaps}")
            updated += 1
        except Exception as e:
            print(f"  ERR {title}: {e}")

    print(f"\nDone. {updated} updated, {skipped} skipped.")


if __name__ == "__main__":
    main()
