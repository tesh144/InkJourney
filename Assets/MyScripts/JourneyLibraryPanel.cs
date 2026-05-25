using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class JourneyLibraryPanel : MonoBehaviour
{
    public static JourneyLibraryPanel instance;

    [Header("Journey Lists")]
    [FormerlySerializedAs("journeyListParent")]
    public Transform ownedJourneyListParent;
    public Transform savedJourneyListParent;
    public Transform publicJourneyListParent;
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

    [Header("Photo Stack")]
    public PhotoShuffle photoShuffle;

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

        JourneyManager.onJourneyActivated   += OnJourneyActivated;
        JourneyManager.onJourneyDeactivated += OnJourneyDeactivated;
    }

    private void OnDisable()
    {
        if (searchInput != null)
            searchInput.onValueChanged.RemoveListener(OnSearchChanged);

        JourneyManager.onJourneyActivated   -= OnJourneyActivated;
        JourneyManager.onJourneyDeactivated -= OnJourneyDeactivated;
    }

    private void OnJourneyActivated()
    {
        RefreshStartStopButtons();
        ApplyFilters();
    }

    private void OnJourneyDeactivated()
    {
        if (selectedJourney != null)
        {
            selectedJourney.SetSelected(false);
            selectedJourney = null;
        }
        RefreshStartStopButtons();
        ApplyFilters();
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

        var sorted = new System.Collections.Generic.List<JourneyEntry>(journeys);
        sorted.Sort((a, b) => b.Created.CompareTo(a.Created));

        foreach (var journey in sorted)
        {
            if (journey == null) continue;

            var parent = GetListParentForJourney(journey);
            if (parent == null) continue;

            var go = Instantiate(journeyObjectPrefab, parent);
            var jo = go.GetComponent<JourneyObject>();
            if (jo == null) continue;

            jo.Initialise(journey, this);
            spawnedJourneys.Add(jo);
        }

        ApplyFilters();
        PrewarmAllPhotos();
        RestoreActiveJourneySelection();
    }

    private Transform GetListParentForJourney(JourneyEntry journey)
    {
        string userId = UserProfileManager.instance?.UserId;

        // Priority 1: owned
        if (!string.IsNullOrEmpty(userId) && journey.User == userId)
            return ownedJourneyListParent;

        // Priority 2: saved (user has read at least one chapter)
        if (JourneyManager.instance != null &&
            JourneyManager.instance.GetCompletedChapterIds(journey.ID).Count > 0)
            return savedJourneyListParent;

        // Priority 3: public
        return publicJourneyListParent;
    }

    private void RestoreActiveJourneySelection()
    {
        var active = JourneyManager.instance?.activeJourney;
        if (active == null) return;

        var jo = spawnedJourneys.Find(j => j?.entry?.ID == active.ID);
        if (jo == null) return;

        selectedJourney = jo;
        jo.SetSelected(true);
        RefreshPreviewPanel();
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
                string url = ResolvePhotoUrl(story);
                if (!string.IsNullOrEmpty(url))
                    StartCoroutine(PhotoAsset.Prewarm(url));
            }
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────

    public void SelectJourney(JourneyObject jo)
    {
        if (selectedJourney == jo)
        {
            selectedJourney.SetSelected(false);
            selectedJourney = null;
            JourneyManager.instance?.DeactivateJourney();
            RefreshPreviewPanel();
            return;
        }

        if (selectedJourney != null) selectedJourney.SetSelected(false);
        selectedJourney = jo;
        if (selectedJourney != null) selectedJourney.SetSelected(true);

        if (selectedJourney?.entry != null && JourneyManager.instance != null)
        {
            var entry = selectedJourney.entry;
            if (IsOwnedByCurrentUser(entry))
            {
                JourneyManager.instance.BeginEditingJourney(entry);
                UIStateManager.instance?.ActivateEditJourneySubState();
            }
            else
            {
                JourneyManager.instance.ActivateJourney(entry);
                FocusMapOnJourney(entry);
            }
        }

        RefreshPreviewPanel();
    }

    private static bool IsOwnedByCurrentUser(JourneyEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.User)) return false;
        return entry.User == UserProfileManager.instance?.UserId;
    }

    private void FocusMapOnJourney(JourneyEntry entry)
    {
        var loader = MapLoader.instance;
        var cursor = MainMapUserCursorController.Instance;
        if (loader == null || cursor == null) return;

        float focusLat = entry.Latitude;
        float focusLon = entry.Longitude;

        if (entry.Chapters != null && entry.Chapters.Count > 0)
        {
            var first = entry.Chapters.Find(c => c.Order == 0) ?? entry.Chapters[0];
            var story = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == first.StoryId)
                     ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == first.StoryId);
            if (story != null && (story.Latitude != 0f || story.Longitude != 0f))
            {
                focusLat = story.Latitude;
                focusLon = story.Longitude;
            }
        }

        if (focusLat == 0f && focusLon == 0f) return;

        float targetZoom = MapInputController.instance != null
            ? MapInputController.instance.minScale
            : -1f;

        Vector2 offset = MainMapUserCursorController.ProjectToMapLogicalPosition(
            focusLat, focusLon,
            loader.CurrentMapCenterLat, loader.CurrentMapCenterLon, loader.CurrentMapZoom);

        if (Mathf.Abs(offset.x) < 560f && Mathf.Abs(offset.y) < 560f)
        {
            cursor.PanToLatLon(focusLat, focusLon, targetZoom);
        }
        else
        {
            cursor.SetCursorVisible(false);
            if (targetZoom > 0f && MapInputController.instance != null)
                MapInputController.instance.SetTargetZoom(targetZoom);
            loader.LoadAtCoordinate(focusLat, focusLon);
        }
    }

    private void RefreshPreviewPanel()
    {
        var entry = selectedJourney?.entry;
        if (entry == null)
        {
            if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(false);
            if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(false);
            photoShuffle?.Clear();
            return;
        }

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
                string url = ResolvePhotoUrl(story);
                if (!string.IsNullOrEmpty(url))
                    urls.Add(url);
            }
        }

        if (urls.Count > 0)
            photoShuffle.SpawnPhotos(urls);
        else
            photoShuffle.Clear();
    }

    private static string ResolvePhotoUrl(GoogleSheetsFetcher.Entry story)
    {
        if (story == null) return null;
        if (!string.IsNullOrEmpty(story.PhotoUrl)) return story.PhotoUrl;
        if (!string.IsNullOrEmpty(story.ID) && PhotoUploadQueue.HasPending(story.ID))
            return "file://" + PhotoUploadQueue.FilePath(story.ID);
        return null;
    }

    private void RefreshStartStopButtons()
    {
        var entry = selectedJourney?.entry;
        if (entry == null || IsOwnedByCurrentUser(entry))
        {
            if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(false);
            if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(false);
            return;
        }

        bool isActive = JourneyManager.instance?.activeJourney?.ID == entry.ID;
        if (startJourneyButton != null) startJourneyButton.gameObject.SetActive(!isActive);
        if (stopJourneyButton  != null) stopJourneyButton.gameObject.SetActive(isActive);
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
        foreach (var jo in spawnedJourneys)
        {
            if (jo == null) continue;
            jo.gameObject.SetActive(MatchesFilter(jo.entry));
        }
    }

    private bool MatchesFilter(JourneyEntry entry)
    {
        if (entry == null) return false;
        if (entry.Draft)
        {
            string userId = UserProfileManager.instance?.UserId;
            if (string.IsNullOrEmpty(userId) || entry.User != userId) return false;
        }

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
