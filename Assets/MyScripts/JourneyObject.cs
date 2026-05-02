using TMPro;
using UnityEngine;
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
            locationText.text = firstStory?.pointer?.location ?? firstStory?.cachedLocation ?? "";

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
