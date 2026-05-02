using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JourneyStartedPopup : MonoBehaviour
{
    public static JourneyStartedPopup instance;

    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public Image            stickerImage;

    private void Awake()
    {
        instance = this;
    }

    public void Show(JourneyEntry journey)
    {
        if (journey == null) return;

        if (titleText != null)
            titleText.text = journey.Title ?? "";

        if (descriptionText != null)
        {
            string location = ResolveLocation(journey);
            string distance = ResolveDistanceMiles(journey);
            int    chapters = journey.Chapters?.Count ?? 0;

            descriptionText.text =
                $"{journey.Description ?? ""}\n\n" +
                $"{location}\n" +
                $"Distance: {distance} miles\n" +
                $"Chapters: {chapters}";
        }

        if (stickerImage != null)
        {
            var sprite = (journey.StickerID >= 1 && StickerManager.instance != null)
                ? StickerManager.instance.GetSticker(journey.StickerID)
                : null;
            stickerImage.sprite = sprite;
            stickerImage.gameObject.SetActive(sprite != null);
        }

        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ResolveLocation(JourneyEntry journey)
    {
        if (journey.Chapters == null || journey.Chapters.Count == 0) return "";
        var first = journey.Chapters.Find(c => c.Order == 0) ?? journey.Chapters[0];
        var story = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == first.StoryId)
                 ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == first.StoryId);
        return story?.pointer?.location ?? story?.cachedLocation ?? "";
    }

    private static string ResolveDistanceMiles(JourneyEntry journey)
    {
        if (GPSManager.Instance == null) return "?";
        if (journey.Latitude == 0f && journey.Longitude == 0f) return "?";

        float dy = (journey.Latitude  - GPSManager.Instance.latitude)  * 111320f;
        float dx = (journey.Longitude - GPSManager.Instance.longitude) *
                   (111320f * Mathf.Cos(journey.Latitude * Mathf.Deg2Rad));
        float metres = Mathf.Sqrt(dx * dx + dy * dy);
        float miles  = metres / 1609.34f;
        return miles < 0.1f ? "< 0.1" : miles.ToString("F1");
    }
}
