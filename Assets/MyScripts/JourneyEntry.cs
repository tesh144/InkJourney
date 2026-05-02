using System.Collections.Generic;

public class JourneyEntry
{
    public string ID;
    public string Title;
    public string Description;
    public int    StickerID;
    public int    MapStyleIndex;
    public List<string> Tags = new List<string>();
    public long   Created;
    public List<ChapterDef> Chapters = new List<ChapterDef>();

    // Derived at runtime from the first chapter's linked story — not stored in Firestore
    public float Latitude;
    public float Longitude;

    public class ChapterDef
    {
        public string Id;
        public string StoryId;
        public int    Order;
        public string InteractionType;        // "read" | "arrive" | "write"
        public string PrerequisiteChapterId;  // null = previous in sequence
        public UnlockCondition Condition;     // null = always
    }

    public class UnlockCondition
    {
        public string Type; // "always" | "proximity" | "time_of_day" | "time_delay" | "write_story" | "seasonal"
        // proximity / write_story:
        public float Latitude;
        public float Longitude;
        public float RadiusMetres;
        // time_of_day:
        public int HourFrom;
        public int HourTo;
        // time_delay:
        public int DelayMinutes;
        // seasonal:
        public int MonthFrom;
        public int MonthTo;
    }
}
