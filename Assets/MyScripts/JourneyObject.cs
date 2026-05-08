using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class JourneyObject : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI locationText;
    public TextMeshProUGUI distanceText;
    public TextMeshProUGUI descriptionText;
    public Image stickerImage;
    public Slider progressBar;
    public TextMeshProUGUI progressText;
    public GameObject selectedIndicator;

    public Color defaultTitleColor  = Color.white;
    public Color selectedTitleColor = Color.cyan;

    [Tooltip("Tick on the persistent main-map journey card so it refreshes itself and redraws the route when enabled")]
    public bool isMapCard;

    [HideInInspector] public JourneyEntry entry;

    private JourneyLibraryPanel journeyLibraryPanel;

    private void OnEnable()
    {
        GPSManager.OnPositionSampled += RefreshDistance;

        if (isMapCard && JourneyManager.instance?.activeJourney != null)
        {
            Initialise(JourneyManager.instance.activeJourney, null);
            JourneyManager.instance.DrawJourneyRoute();
        }
    }

    private void OnDisable()
    {
        GPSManager.OnPositionSampled -= RefreshDistance;
    }

    private void RefreshDistance(float lat, float lon)
    {
        if (distanceText == null || entry == null) return;
        float dy = (entry.Latitude  - lat) * 111320f;
        float dx = (entry.Longitude - lon) * (111320f * Mathf.Cos(entry.Latitude * Mathf.Deg2Rad));
        int metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
        distanceText.text = metres > 999 ? $"{Mathf.RoundToInt(metres / 1000f)}k" : $"{metres}m";
    }

    public void Initialise(JourneyEntry j, JourneyLibraryPanel panel)
    {
        entry = j;
        journeyLibraryPanel = panel;

        if (titleText != null) titleText.text = j.Title ?? "";
        if (descriptionText != null) descriptionText.text = j.Description ?? "";

        if (stickerImage != null)
        {
            var sprite = (j.StickerID >= 1 && StickerManager.instance != null)
                ? StickerManager.instance.GetSticker(j.StickerID)
                : null;
            stickerImage.sprite = sprite;
            stickerImage.gameObject.SetActive(sprite != null);
        }

        // Resolve first-chapter story for location text and lat/lon (may have been 0 at load time)
        GoogleSheetsFetcher.Entry firstStory = null;
        if (j.Chapters != null && j.Chapters.Count > 0)
        {
            var firstChapter = j.Chapters.Find(c => c.Order == 0) ?? j.Chapters[0];
            firstStory = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == firstChapter.StoryId)
                      ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == firstChapter.StoryId);
            if (firstStory != null && j.Latitude == 0f && j.Longitude == 0f)
            {
                j.Latitude  = firstStory.Latitude;
                j.Longitude = firstStory.Longitude;
            }
        }

        if (locationText != null)
        {
            string loc = firstStory?.cachedLocation
                      ?? firstStory?.pointer?.location
                      ?? "";

            if (!string.IsNullOrEmpty(loc))
                locationText.text = loc;
            else if (firstStory != null)
                StartCoroutine(FetchLocation(firstStory));
        }

        if (distanceText != null)
        {
            if (GPSManager.Instance != null)
                RefreshDistance(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
            else
                distanceText.text = "--";
        }

        if (JourneyManager.instance != null)
        {
            int total     = j.Chapters?.Count ?? 0;
            int completed = JourneyManager.instance.GetCompletedChapterIds(j.ID).Count;
            if (progressBar  != null) progressBar.value = total > 0 ? (float)completed / total : 0f;
            if (progressText != null) progressText.text = $"{completed}/{total}";
        }

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (titleText != null)
            titleText.color = selected ? selectedTitleColor : defaultTitleColor;
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
    }

    public void RefreshProgress()
    {
        if (JourneyManager.instance == null || entry == null) return;
        int total     = entry.Chapters?.Count ?? 0;
        int completed = JourneyManager.instance.GetCompletedChapterIds(entry.ID).Count;
        if (progressBar  != null) progressBar.value = total > 0 ? (float)completed / total : 0f;
        if (progressText != null) progressText.text = $"{completed}/{total}";
    }

    private IEnumerator FetchLocation(GoogleSheetsFetcher.Entry story)
    {
        string token = MapLoader.instance?.mapboxToken ?? "";
        string url   = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{story.Longitude},{story.Latitude}.json?access_token={token}";

        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;

        var response = JsonUtility.FromJson<MapPointer.MapboxGeocodeResponse>(req.downloadHandler.text);
        if (response?.features == null || response.features.Length == 0) yield break;

        string neighbourhood = null, city = null, adminArea = null;
        var first = response.features[0];
        if (first.place_type != null &&
            (System.Array.IndexOf(first.place_type, "neighborhood") >= 0 ||
             System.Array.IndexOf(first.place_type, "locality")     >= 0))
            neighbourhood = first.text;

        if (first.context != null)
            foreach (var ctx in first.context)
            {
                if (city      == null && ctx.id != null && (ctx.id.StartsWith("place.")    || ctx.id.StartsWith("locality."))) city      = ctx.text;
                if (adminArea == null && ctx.id != null &&  ctx.id.StartsWith("district."))                                    adminArea = ctx.text;
            }

        if (city == null)
            foreach (var feat in response.features)
                if (feat.place_type != null && System.Array.IndexOf(feat.place_type, "place") >= 0)
                { city = feat.text; break; }

        string loc = MapPointer.BuildLocation(neighbourhood, city, adminArea) ?? first.place_name;
        story.cachedLocation = loc;
        if (locationText != null) locationText.text = loc;
    }

    // Assign to button OnClick
    public void Select()
    {
        journeyLibraryPanel?.SelectJourney(this);
    }

    // Assign to close/X button OnClick on the main map journey card
    public void Dismiss()
    {
        JourneyManager.instance?.DeactivateJourney();
    }
}
