using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class JourneyLibraryPanel : MonoBehaviour
{
    public static JourneyLibraryPanel instance;

    [Header("Journey List")]
    public Transform journeyListParent;
    public GameObject journeyObjectPrefab;
    public TMP_InputField searchInput;

    [Header("Filter Buttons")]
    public Button filterCompleteButton;
    public Button filterIncompleteButton;
    public GameObject filterCompleteOnIndicator;
    public GameObject filterIncompleteOnIndicator;

    [Header("Preview Panel")]
    public TextMeshProUGUI previewTitle;
    public TextMeshProUGUI previewDescription;
    public TextMeshProUGUI previewLocation;
    public TextMeshProUGUI previewDistance;
    public Slider previewProgressBar;
    public Button startJourneyButton;
    public Button stopJourneyButton;
    public RawImage journeyMapImage;

    [Header("Photo Stack")]
    public PhotoShuffle photoShuffle;

    [Header("Preview Map Pins")]
    public Color chapterPinColor = new Color(0.96f, 0.65f, 0.14f); // #f5a623
    public enum PinSize { Small, Medium, Large }
    public PinSize chapterPinSize = PinSize.Small;

    private JourneyObject selectedJourney;
    private readonly List<JourneyObject> spawnedJourneys = new List<JourneyObject>();

    private string _query = "";
    private int _typeFilter = 0; // 0 = all, 1 = complete, 2 = incomplete

    private void Awake()
    {
        instance = this;
        if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(false);
        if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _query = "";
        _typeFilter = 0;
        if (searchInput != null)
        {
            searchInput.SetTextWithoutNotify("");
            searchInput.onValueChanged.AddListener(OnSearchChanged);
        }
        RefreshFilterVisuals();
        RefreshStartStopButtons();
        PopulateJourneysList();

        JourneyManager.onJourneyActivated   += OnJourneyActivationChanged;
        JourneyManager.onJourneyDeactivated += OnJourneyActivationChanged;
    }

    private void OnDisable()
    {
        if (searchInput != null)
            searchInput.onValueChanged.RemoveListener(OnSearchChanged);

        JourneyManager.onJourneyActivated   -= OnJourneyActivationChanged;
        JourneyManager.onJourneyDeactivated -= OnJourneyActivationChanged;

        // Reset the map image so it doesn't persist into other panels
        if (journeyMapImage != null)
        {
            journeyMapImage.texture = null;
            journeyMapImage.uvRect  = new Rect(0f, 0f, 1f, 1f);
            journeyMapImage.color   = Color.white;
        }
    }

    private void OnJourneyActivationChanged()
    {
        RefreshStartStopButtons();
        PopulateJourneysList();
    }

    // ── List Population ───────────────────────────────────────────────────

    public void PopulateJourneysList()
    {
        selectedJourney = null;
        RefreshStartStopButtons();

        foreach (var jo in spawnedJourneys)
            if (jo != null) Destroy(jo.gameObject);
        spawnedJourneys.Clear();

        var journeys = GoogleSheetsFetcher.instance?.journeysList;
        if (journeys == null) return;

        foreach (var journey in journeys)
        {
            if (journey == null) continue;

            var go = Instantiate(journeyObjectPrefab, journeyListParent);
            var jo = go.GetComponent<JourneyObject>();
            if (jo == null) continue;

            jo.Initialise(journey, this);
            spawnedJourneys.Add(jo);
        }

        ApplyFilters();
        PrewarmAllPhotos();
    }

    private void PrewarmAllPhotos()
    {
        var journeys = GoogleSheetsFetcher.instance?.journeysList;
        if (journeys == null) return;
        var fetcher = GoogleSheetsFetcher.instance;
        foreach (var journey in journeys)
        {
            if (journey?.Chapters == null) continue;
            foreach (var chapter in journey.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId)) continue;
                var story = fetcher.storiesList?.Find(e => e?.ID == chapter.StoryId)
                         ?? fetcher.landmarksList?.Find(e => e?.ID == chapter.StoryId);
                if (!string.IsNullOrEmpty(story?.PhotoUrl))
                    StartCoroutine(PhotoAsset.Prewarm(story.PhotoUrl));
            }
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────

    public void SelectJourney(JourneyObject jo)
    {
        if (selectedJourney != null) selectedJourney.SetSelected(false);
        selectedJourney = jo;
        if (selectedJourney != null) selectedJourney.SetSelected(true);

        RefreshPreviewPanel();
    }

    private void RefreshPreviewPanel()
    {
        var entry = selectedJourney?.entry;
        if (entry == null) return;

        PopulatePhotoStack(entry);

        if (previewTitle != null)       previewTitle.text       = entry.Title ?? "";
        if (previewDescription != null) previewDescription.text = entry.Description ?? "";

        // Location from first chapter's story
        string loc = "";
        if (entry.Chapters != null && entry.Chapters.Count > 0)
        {
            var firstChapter = entry.Chapters.Find(c => c.Order == 0) ?? entry.Chapters[0];
            var story = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == firstChapter.StoryId)
                     ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == firstChapter.StoryId);
            if (story != null)
                loc = story.pointer?.location ?? story.cachedLocation ?? "";
        }
        if (previewLocation != null) previewLocation.text = loc;

        if (previewDistance != null && GPSManager.Instance != null)
        {
            float dy = (entry.Latitude  - GPSManager.Instance.latitude)  * 111320f;
            float dx = (entry.Longitude - GPSManager.Instance.longitude) * (111320f * Mathf.Cos(entry.Latitude * Mathf.Deg2Rad));
            int m = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
            previewDistance.text = m > 999 ? $"{Mathf.RoundToInt(m / 1000f)}k" : $"{m}m";
        }

        if (previewProgressBar != null && JourneyManager.instance != null)
            previewProgressBar.value = JourneyManager.instance.GetProgressPercent(entry.ID);

        RefreshStartStopButtons();
        LoadPreviewMap(entry);
    }

    private void PopulatePhotoStack(JourneyEntry entry)
    {
        if (photoShuffle == null) return;

        var urls = new System.Collections.Generic.List<string>();
        if (entry.Chapters != null)
        {
            var fetcher = GoogleSheetsFetcher.instance;
            foreach (var chapter in entry.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId)) continue;
                var story = fetcher?.storiesList?.Find(e => e?.ID == chapter.StoryId)
                         ?? fetcher?.landmarksList?.Find(e => e?.ID == chapter.StoryId);
                if (!string.IsNullOrEmpty(story?.PhotoUrl))
                    urls.Add(story.PhotoUrl);
            }
        }

        if (urls.Count > 0)
            photoShuffle.SpawnPhotos(urls);
        else
            photoShuffle.Clear();
    }

    private void RefreshStartStopButtons()
    {
        var entry = selectedJourney?.entry;
        if (entry == null)
        {
            if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(false);
            if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(false);
            return;
        }

        bool isActive = JourneyManager.instance?.activeJourney?.ID == entry.ID;
        if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(!isActive);
        if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(isActive);
    }

    private void LoadPreviewMap(JourneyEntry entry)
    {
        if (journeyMapImage == null || MapLoader.instance == null) return;
        if (entry.Latitude == 0f && entry.Longitude == 0f) return;

        string styleId = MapLoader.instance.mapStyles != null && MapLoader.instance.mapStyles.Count > 0
            ? MapLoader.instance.mapStyles[entry.MapStyleIndex % MapLoader.instance.mapStyles.Count].styleString
            : "mapbox/dark-v11";

        // Build chapter pin overlays: pin-s-1+colour(lon,lat),pin-s-2+colour(lon,lat),...
        var overlayParts = new System.Collections.Generic.List<string>();
        if (entry.Chapters != null)
        {
            foreach (var chapter in entry.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId)) continue;
                var story = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == chapter.StoryId)
                         ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == chapter.StoryId);
                if (story == null || (story.Latitude == 0f && story.Longitude == 0f)) continue;

                string sizeCode = chapterPinSize == PinSize.Large ? "l" : chapterPinSize == PinSize.Medium ? "m" : "s";
                string hex      = ColorUtility.ToHtmlStringRGB(chapterPinColor);
                string label    = (chapter.Order + 1).ToString();
                string lon      = story.Longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                string lat      = story.Latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                overlayParts.Add($"pin-{sizeCode}-{label}+{hex}({lon},{lat})");
            }
        }

        string overlay = overlayParts.Count > 0 ? string.Join(",", overlayParts) + "/" : "";
        string center  = $"{entry.Longitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{entry.Latitude.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";
        string url     = $"https://api.mapbox.com/styles/v1/{styleId}/static/{overlay}{center},13,0/640x320@2x?access_token={MapLoader.instance.mapboxToken}";

        // Clear old image immediately so it doesn't show stale content while loading
        if (journeyMapImage != null)
        {
            journeyMapImage.texture = null;
            journeyMapImage.uvRect  = new Rect(0f, 0f, 1f, 1f);
            journeyMapImage.color   = Color.white;
        }

        StartCoroutine(LoadMapImage(url));
    }

    private System.Collections.IEnumerator LoadMapImage(string url)
    {
        Texture2D tex = null;
        yield return MapboxImageCache.Fetch(url, t => tex = t);
        if (tex == null || journeyMapImage == null) yield break;
        {
            journeyMapImage.texture = tex;

            // Center-crop so the texture fills the container without stretching
            float texAspect  = tex.width  / (float)tex.height;
            float dispAspect = journeyMapImage.rectTransform.rect.width
                             / journeyMapImage.rectTransform.rect.height;

            if (texAspect > dispAspect)
            {
                float u = dispAspect / texAspect;
                journeyMapImage.uvRect = new Rect((1f - u) * 0.5f, 0f, u, 1f);
            }
            else
            {
                float v = texAspect / dispAspect;
                journeyMapImage.uvRect = new Rect(0f, (1f - v) * 0.5f, 1f, v);
            }
        }
    }

    // ── Start / Stop ──────────────────────────────────────────────────────

    public void StartJourney()
    {
        if (selectedJourney?.entry == null || JourneyManager.instance == null) return;
        JourneyManager.instance.ActivateJourney(selectedJourney.entry);
        RefreshStartStopButtons();
    }

    public void StopJourney()
    {
        if (JourneyManager.instance == null) return;
        JourneyManager.instance.DeactivateJourney();
        RefreshStartStopButtons();
    }

    // ── Filters ───────────────────────────────────────────────────────────

    public void ToggleFilterComplete()
    {
        _typeFilter = _typeFilter == 1 ? 0 : 1;
        RefreshFilterVisuals();
        ApplyFilters();
    }

    public void ToggleFilterIncomplete()
    {
        _typeFilter = _typeFilter == 2 ? 0 : 2;
        RefreshFilterVisuals();
        ApplyFilters();
    }

    private void RefreshFilterVisuals()
    {
        if (filterCompleteOnIndicator   != null) filterCompleteOnIndicator.SetActive(_typeFilter == 1);
        if (filterIncompleteOnIndicator != null) filterIncompleteOnIndicator.SetActive(_typeFilter == 2);
    }

    private void OnSearchChanged(string value)
    {
        _query = value ?? "";
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        string activeId = JourneyManager.instance?.activeJourney?.ID;
        foreach (var jo in spawnedJourneys)
        {
            if (jo == null) continue;
            bool show = MatchesFilter(jo.entry) && jo.entry?.ID != activeId;
            jo.gameObject.SetActive(show);
        }
    }

    private bool MatchesFilter(JourneyEntry entry)
    {
        if (entry == null) return false;

        if (_typeFilter != 0 && JourneyManager.instance != null)
        {
            float pct = JourneyManager.instance.GetProgressPercent(entry.ID);
            bool complete = pct >= 1f && entry.Chapters != null && entry.Chapters.Count > 0;
            if (_typeFilter == 1 && !complete) return false;
            if (_typeFilter == 2 && complete)  return false;
        }

        if (!string.IsNullOrWhiteSpace(_query))
            return MatchesSearch(entry, _query);

        return true;
    }

    private static bool MatchesSearch(JourneyEntry entry, string query)
    {
        if (entry == null || string.IsNullOrWhiteSpace(query)) return true;
        string q = query.Trim();

        if (!string.IsNullOrEmpty(entry.Title) && entry.Title.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (entry.Tags != null)
            foreach (var tag in entry.Tags)
            {
                string display = TagManager.instance != null ? TagManager.instance.GetDisplayName(tag) : tag;
                if (!string.IsNullOrEmpty(display) && display.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

        return false;
    }

    // ── Progress Refresh ──────────────────────────────────────────────────

    public void RefreshJourneyProgress()
    {
        foreach (var jo in spawnedJourneys)
            jo?.RefreshProgress();

        if (selectedJourney != null && previewProgressBar != null && JourneyManager.instance != null)
            previewProgressBar.value = JourneyManager.instance.GetProgressPercent(selectedJourney.entry.ID);

        FindObjectOfType<JourneyCompletedCountDisplay>()?.Refresh();
    }
}
