"""
Seed script for Altrincham test stories.
Run: python3 seed_altrincham.py
"""

import time
import uuid
import json
import math
import urllib.request
import urllib.error

PROJECT_ID = "inkjourney"
API_KEY = "AIzaSyB8do4t79xVew8sE52mQM7Yveo5SUPYQdQ"
BASE_URL = f"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}/databases/(default)/documents/Stories"

NOW = int(time.time())
FAR_FUTURE = 32506915900

USERS = {
    "mia": {"id": "u_mia_ward", "name": "Mia Ward"},
    "tariq": {"id": "u_tariq_hussain", "name": "Tariq Hussain"},
    "elsie": {"id": "u_elsie_holt", "name": "Elsie Holt"},
    "ben": {"id": "u_ben_clarke", "name": "Ben Clarke"},
    "rowan": {"id": "u_rowan_price", "name": "Rowan Price"},
    "priya": {"id": "u_priya_patel", "name": "Priya Patel"},
    "jules": {"id": "u_jules_morris", "name": "Jules Morris"},
}

# lat/lon across Altrincham, Hale, Bowdon, Broadheath, Timperley edge
STORIES = [
    {
        "user": "elsie",
        "title": "Market Hall, 6:07am",
        "content": "I opened stall shutters before sunrise and the whole hall smelled like warm bread and wet cardboard. First customer was a man in a cycling jacket buying one pear and talking about cloud shapes. By eight o'clock it was all clatter and coffee steam. I still love that first quiet hour best, when the lights hum and everyone is getting ready to be themselves.",
        "lat": 53.3872,
        "lon": -2.3530,
        "tags": ["food", "wholesome", "culture"],
        "days_ago": 142,
    },
    {
        "user": "tariq",
        "title": "Old Station Bricks",
        "content": "Near Altrincham Interchange there are brick courses that don't match the rest of the wall. Different clay, different age, different story. Most people walk straight past it; I stop every time. Buildings are edited like books. If you learn to read the joins, you can see what was removed and what was saved.",
        "lat": 53.3870,
        "lon": -2.3492,
        "tags": ["historical", "buildings", "exploration"],
        "days_ago": 136,
    },
    {
        "user": "ben",
        "title": "Tram Platform Etiquette",
        "content": "Unwritten rule at Navigation Road: if it's raining and someone has a giant umbrella, they become temporary mayor of the platform. We all huddle in and pretend not to make eye contact. This morning the umbrella-mayor was a woman in neon trainers who announced, 'Two inches to the left, people.' We obeyed instantly.",
        "lat": 53.3918,
        "lon": -2.3499,
        "tags": ["funny", "personal", "culture"],
        "days_ago": 131,
    },
    {
        "user": "mia",
        "title": "Blue Hour on Stamford New Road",
        "content": "The best light lasts seven minutes if the clouds cooperate. Shop windows turn into mirrors, buses become streaks of amber, and puddles suddenly look intentional. I shot twelve frames and kept one: a kid in a yellow coat stepping over a crack like it was a river. Tiny, perfect bravery.",
        "lat": 53.3876,
        "lon": -2.3521,
        "tags": ["creative", "magical", "personal"],
        "days_ago": 128,
    },
    {
        "user": "rowan",
        "title": "Poem on Hale Road",
        "content": "I wrote this while waiting for the crossing near Hale Road:\n\nA town is a pocket you keep finding things in,\na receipt, a pebble, a name you forgot you loved.\nStreetlights stitch evening to pavement.\nSomewhere a kettle begins.\nSomewhere someone says, come in, you're soaked.\n\nI think that's enough poetry for one red light.",
        "lat": 53.3803,
        "lon": -2.3406,
        "tags": ["poetry", "lgbt", "creative"],
        "days_ago": 124,
    },
    {
        "user": "priya",
        "title": "Bowdon Front Doors",
        "content": "I'm mildly obsessed with front doors. Bowdon has every style: fanlights with delicate glazing bars, deep-set arches, modern matte black slabs that try to look understated but absolutely are not. Today I counted twenty-one doors on one loop and accidentally turned a ten-minute walk into a one-hour architectural safari.",
        "lat": 53.3762,
        "lon": -2.3602,
        "tags": ["buildings", "exploration", "funny"],
        "days_ago": 119,
    },
    {
        "user": "jules",
        "title": "Broadheath Back Lane",
        "content": "Found a service lane behind the retail units with murals half-faded by weather. One was a fox in a hi-vis vest holding a coffee. Whoever painted it deserves civic honors. The alley smelled like paint, diesel, and chips. Proper hidden-gem energy.",
        "lat": 53.3954,
        "lon": -2.3639,
        "tags": ["hidden_gems", "exploration", "creative"],
        "days_ago": 114,
    },
    {
        "user": "tariq",
        "title": "Dunham Deer Note",
        "content": "Early walk in Dunham and counted nine deer before breakfast. One stood under an oak and looked directly at me like I owed rent. Parkland here changes slowly enough that you can notice it properly: one fallen branch, one new sapling, one season folding into the next.",
        "lat": 53.3748,
        "lon": -2.3933,
        "tags": ["wildlife", "wholesome", "historical"],
        "days_ago": 109,
    },
    {
        "user": "elsie",
        "title": "Soup Trial, Market Edition",
        "content": "I gave out free spoonfuls of roasted tomato soup today and discovered that children are the strictest critics. One six-year-old said, 'Good, but needs adventure.' I added smoked paprika and called it Pirate Soup. Sold out in forty minutes.",
        "lat": 53.3871,
        "lon": -2.3532,
        "tags": ["food", "funny", "wholesome"],
        "days_ago": 103,
    },
    {
        "user": "ben",
        "title": "George Street, Friday Physics",
        "content": "There is a law of motion specific to George Street: if one person says 'just one drink,' the night gains two extra hours. Verified repeatedly. Tonight included chips, a pub dog called Bruno, and a stranger teaching us a card trick that was either genius or very obvious. Still unsure.",
        "lat": 53.3878,
        "lon": -2.3535,
        "tags": ["culture", "funny", "personal"],
        "days_ago": 98,
    },
    {
        "user": "rowan",
        "title": "Part 1: The Green Door",
        "content": "On a side street off The Downs there's a green door with a brass fox knocker. I walked past it for months, then noticed a tiny carved star above the frame. Nobody else seemed to clock it. I left a chalk star on the pavement opposite like a note to future-me: come back, this means something.",
        "lat": 53.3857,
        "lon": -2.3500,
        "tags": ["mysteries", "spooky", "exploration"],
        "days_ago": 92,
    },
    {
        "user": "rowan",
        "title": "Part 2: The Green Door",
        "content": "I came back at dusk. The star I'd chalked was gone, washed by rain, but a tiny paper crane sat on the doorstep. Coincidence? Probably. Did I still gasp like I'd found a coded message in a detective novel? Also yes. I left the crane where it was. Some mysteries are better when they remain politely unresolved.",
        "lat": 53.3858,
        "lon": -2.3498,
        "tags": ["mysteries", "spooky", "magical"],
        "days_ago": 90,
    },
    {
        "user": "mia",
        "title": "Selfie in Rain, No Filter",
        "content": "Caught in a proper Manchester-style downpour near Old Market Place and took the most chaotic selfie of my life: wet fringe, laughing eyes, mascara attempting escape velocity. Weirdly it's my favorite photo this month because nothing is composed and everything feels true.",
        "lat": 53.3877,
        "lon": -2.3543,
        "tags": ["personal", "creative", "wholesome"],
        "days_ago": 84,
    },
    {
        "user": "priya",
        "title": "Library Steps and Limestone",
        "content": "Spent lunch break studying the stone on the old civic buildings around the library. Weathering patterns are like fingerprints: runoff darkening under ledges, pale streaks where replacement blocks were inserted decades later. I know this sounds nerdy. It is nerdy. I regret nothing.",
        "lat": 53.3883,
        "lon": -2.3496,
        "tags": ["buildings", "historical", "personal"],
        "days_ago": 78,
    },
    {
        "user": "jules",
        "title": "Canal Bench Census",
        "content": "Walked the canal path with a notebook and rated benches out of ten. Best bench: slightly wonky one near the willow, score 9.2 because it faces sunset and has excellent duck visibility. Worst bench: no backrest, oddly sticky, score 2.1. Public service complete.",
        "lat": 53.3951,
        "lon": -2.3449,
        "tags": ["exploration", "funny", "wildlife"],
        "days_ago": 73,
    },
    {
        "user": "elsie",
        "title": "Quiet Kindness at the Till",
        "content": "Older gent came up short by 70p and started apologizing before I could speak. Woman behind him quietly put a pound on the counter and said, 'We've all had Tuesdays.' He smiled, she waved it off, and everybody pretended not to cry over root vegetables.",
        "lat": 53.3873,
        "lon": -2.3531,
        "tags": ["wholesome", "personal", "culture"],
        "days_ago": 67,
    },
    {
        "user": "tariq",
        "title": "Moss Lane Matchday",
        "content": "At Alty's ground the sound arrives in layers: chatter, then drums, then that one person who can project from three postcodes away. I love that non-league still feels stitched into the town. You don't spectate from a distance; you stand close enough to hear names, jokes, and panic in real time.",
        "lat": 53.3812,
        "lon": -2.3510,
        "tags": ["culture", "historical", "personal"],
        "days_ago": 61,
    },
    {
        "user": "ben",
        "title": "Navigation Road Coffee Queue",
        "content": "Five people deep, train in three minutes, barista moving like a chess grandmaster. She remembered my order and called me 'double-oat-flat-white guy' which, frankly, is now my title in local government.",
        "lat": 53.3916,
        "lon": -2.3503,
        "tags": ["food", "funny", "culture"],
        "days_ago": 55,
    },
    {
        "user": "mia",
        "title": "Hidden Courtyard Light",
        "content": "Found a little courtyard off Greenwood Street where afternoon sun bounces off three brick walls and turns everything honey-colored. Took portraits for a friend there and every frame looked like late summer even though we were freezing.",
        "lat": 53.3869,
        "lon": -2.3560,
        "tags": ["hidden_gems", "creative", "magical"],
        "days_ago": 49,
    },
    {
        "user": "rowan",
        "title": "LGBT Book Swap Shelf",
        "content": "Spotted a tiny free-library shelf in a cafe with queer memoirs, a dog-eared anthology, and one mystery novel with three different bookmarks inside. Left a note in the front of my old favorite: 'Whoever finds this, you're not the only one figuring things out in this town.'",
        "lat": 53.3866,
        "lon": -2.3499,
        "tags": ["lgbt", "culture", "wholesome"],
        "days_ago": 41,
    },
    {
        "user": "jules",
        "title": "Spooky Underpass Echo",
        "content": "Went through the rail underpass at night and heard footsteps matching mine exactly. Stopped. Silence. Started again. Same thing. Turned out to be water drips hitting a metal sign in rhythm with my stride. Still absolutely sprinted the last ten meters.",
        "lat": 53.3895,
        "lon": -2.3478,
        "tags": ["spooky", "funny", "mysteries"],
        "days_ago": 33,
    },
    {
        "user": "priya",
        "title": "Old and New at Broadheath",
        "content": "Broadheath fascinates me because it refuses to be one thing. Industrial remnants, glass-fronted offices, loading bays, cycle lanes, all stitched together by practical need more than aesthetic purity. It shouldn't work as well as it does. And yet there it is: messy, useful, alive.",
        "lat": 53.3950,
        "lon": -2.3652,
        "tags": ["buildings", "exploration", "culture"],
        "days_ago": 24,
    },
    {
        "user": "elsie",
        "title": "Late Orange Sky Over Hale",
        "content": "Locked up, walked home through Hale village, and the sky turned that impossible peach color you only get for about four minutes in spring. Even the traffic behaved. Someone across the road just said, 'Look at that,' and everyone did.",
        "lat": 53.3774,
        "lon": -2.3337,
        "tags": ["wholesome", "magical", "personal"],
        "days_ago": 17,
    },
    {
        "user": "tariq",
        "title": "A Short History of My Bus Stop",
        "content": "I started waiting at this same stop as a student with cheap shoes and a backpack full of unread books. Years later I still stand in almost the same square of pavement, now in a teacher's coat with red marking pen in my pocket. Different life, same stop, same drizzle. Altrincham does that: it lets your timeline layer over itself. Places hold your former versions with surprising generosity.",
        "lat": 53.3844,
        "lon": -2.3554,
        "tags": ["historical", "personal", "poetry"],
        "days_ago": 9,
    },
    {
        "user": "ben",
        "title": "Breakfast Roll Peace Treaty",
        "content": "I brought bacon rolls to the office near Atlantic Street and accidentally ended a week-long argument about spreadsheet colors. Never underestimate carbohydrates as a conflict-resolution strategy.",
        "lat": 53.3929,
        "lon": -2.3667,
        "tags": ["food", "funny", "wholesome"],
        "days_ago": 3,
    },
]


def firestore_value(v):
    if isinstance(v, str):
        return {"stringValue": v}
    if isinstance(v, float):
        return {"doubleValue": v}
    if isinstance(v, int):
        return {"integerValue": str(v)}
    raise ValueError(f"Unsupported type: {type(v)}")


def firestore_array_str(values):
    return {
        "arrayValue": {
            "values": [{"stringValue": s} for s in values]
        }
    }


def distance_meters(story_a, story_b):
    lat1, lon1 = story_a["lat"], story_a["lon"]
    lat2, lon2 = story_b["lat"], story_b["lon"]
    lat_diff = (lat1 - lat2) * 111320.0
    lon_diff = (lon1 - lon2) * (111320.0 * math.cos(math.radians(lat1)))
    return (lat_diff * lat_diff + lon_diff * lon_diff) ** 0.5


def validate_story_spacing():
    conflicts = []
    for index, story in enumerate(STORIES):
        for other in STORIES[index + 1:]:
            if story["user"] == other["user"]:
                continue

            separation = distance_meters(story, other)
            if separation <= 50.0:
                conflicts.append((round(separation, 1), story["title"], other["title"]))

    if conflicts:
        lines = [f"{meters}m: '{title_a}' vs '{title_b}'" for meters, title_a, title_b in conflicts]
        raise ValueError("Different-user stories are too close together:\n" + "\n".join(lines))


def upload_story(story):
    user = USERS[story["user"]]
    created = NOW - (story["days_ago"] * 86400) - ((story["days_ago"] % 7) * 1733)
    doc_id = uuid.uuid4().hex

    fields = {
        "User": firestore_value(user["id"]),
        "UserName": firestore_value(user["name"]),
        "Latitude": firestore_value(float(story["lat"])),
        "Longitude": firestore_value(float(story["lon"])),
        "Title": firestore_value(story["title"]),
        "Content": firestore_value(story["content"]),
        "Tags": firestore_array_str(story["tags"]),
        "Theme": firestore_value(""),
        "Voice": firestore_value(""),
        "Track": firestore_value(""),
        "Font": firestore_value(""),
        "Likes": firestore_value(0),
        "Views": firestore_value(0),
        "Created": firestore_value(created),
        "LastUpdated": firestore_value(created),
        "Expire": firestore_value(FAR_FUTURE),
        "PhotoUrl": firestore_value(""),
    }

    url = f"{BASE_URL}/{doc_id}?key={API_KEY}"
    payload = json.dumps({"fields": fields}).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json"},
        method="PATCH",
    )

    try:
        with urllib.request.urlopen(req, timeout=25) as resp:
            status = resp.getcode()
            body = resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        status = e.code
        body = e.read().decode("utf-8", errors="replace")
    except Exception as e:
        status = 0
        body = str(e)

    if status in (200, 201):
        print(f"  OK  {story['title']}  [{user['name']}]  tags={story['tags']}")
    else:
        print(f"  ERR {story['title']}  [{status}] {body[:140]}")


if __name__ == "__main__":
    print(f"Uploading {len(STORIES)} stories to Firestore project '{PROJECT_ID}'...\n")
    validate_story_spacing()
    for story in STORIES:
        if len(story["tags"]) > 3:
            raise ValueError(f"Too many tags on '{story['title']}': {story['tags']}")
        upload_story(story)
    print(f"\nDone. {len(STORIES)} stories attempted.")
