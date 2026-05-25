using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using Sirenix.OdinInspector;

public class JourneyManager : MonoBehaviour
{
    public static JourneyManager instance;

    public JourneyEntry activeJourney { get; private set; }

    [Header("Main Map Journey Card")]
    [Tooltip("JourneyObject instance on the main map that shows the active journey")]
    public JourneyObject mapJourneyCard;

    [Header("Journey Complete Popup")]
    public GameObject journeyCompletePopup;
    public TextMeshProUGUI journeyCompleteTitleText;

    public static event Action onJourneyActivated;
    public static event Action onJourneyDeactivated;
    public static event Action<GoogleSheetsFetcher.Entry> onStoryAddedToJourney;
    public static event Action onJourneyCreationDistanceLimitReached;

    // ── Journey Creation ──────────────────────────────────────────────────

    [Header("Journey Creation")]
    public float creationDistanceLimitMetres = 5000f;

    [ShowInInspector, ReadOnly] public bool isCreatingJourney { get; private set; }
    [ShowInInspector, ReadOnly] public string CreatingJourneyId    => creatingJourney?.ID          ?? "";
    [ShowInInspector, ReadOnly] public string CreatingJourneyTitle => creatingJourney?.Title        ?? "";
    [ShowInInspector, ReadOnly] public string CreatingJourneyDesc  => creatingJourney?.Description  ?? "";

    public JourneyEntry creatingJourney { get; private set; }

    private float _creationStartLat, _creationStartLon;
    private Coroutine _saveDebounce;
    private bool _isEditMode;

    [ShowInInspector, ReadOnly] public bool isEditMode => _isEditMode;

    public void BeginCreatingJourney()
    {
        if (isCreatingJourney) return;
        isCreatingJourney = true;
        creatingJourney = new JourneyEntry
        {
            ID          = Guid.NewGuid().ToString("N"),
            Title       = "Untitled",
            Description = "Add description...",
            Draft       = true,
            User        = UserProfileManager.instance?.UserId ?? "",
            Created     = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Chapters    = new List<JourneyEntry.ChapterDef>()
        };
        if (MapLoader.instance != null)
            creatingJourney.MapStyleIndex = MapLoader.instance.currentStyleIndex;
        if (GPSManager.Instance != null)
        {
            _creationStartLat = GPSManager.Instance.latitude;
            _creationStartLon = GPSManager.Instance.longitude;
        }
        MapLoader.onStyleChanged     += OnCreationStyleChanged;
        GPSManager.OnPositionSampled += OnCreationGpsSampled;

        // Persist to Firestore immediately so it survives app restarts
        GoogleSheetsFetcher.instance?.journeysList?.Add(creatingJourney);
        PlayerPrefs.SetString("Journey.CreatingId", creatingJourney.ID);
        PlayerPrefs.SetFloat("Journey.CreatingStartLat", _creationStartLat);
        PlayerPrefs.SetFloat("Journey.CreatingStartLon", _creationStartLon);
        PlayerPrefs.Save();
        SaveJourneyToFirestore(creatingJourney);
    }

    public void SaveCreatingJourney()
    {
        if (!isCreatingJourney || creatingJourney == null) return;
        if (_saveDebounce != null) StopCoroutine(_saveDebounce);
        _saveDebounce = StartCoroutine(SaveCreatingJourneyDebounced());
    }

    private IEnumerator SaveCreatingJourneyDebounced()
    {
        yield return new WaitForSeconds(1f);
        if (isCreatingJourney && creatingJourney != null)
            SaveJourneyToFirestore(creatingJourney);
        _saveDebounce = null;
    }

    public void BeginEditingJourney(JourneyEntry journey)
    {
        if (isCreatingJourney) return;
        _isEditMode       = true;
        isCreatingJourney = true;
        creatingJourney   = journey;
        if (GPSManager.Instance != null)
        {
            _creationStartLat = GPSManager.Instance.latitude;
            _creationStartLon = GPSManager.Instance.longitude;
        }
        MapLoader.onStyleChanged     += OnCreationStyleChanged;
        GPSManager.OnPositionSampled += OnCreationGpsSampled;
        // Journey already exists in Firestore — no immediate save needed
    }

    public void AddStoryToCreatingJourney(GoogleSheetsFetcher.Entry story)
    {
        if (!isCreatingJourney || creatingJourney == null || story == null) return;
        creatingJourney.Chapters.Add(new JourneyEntry.ChapterDef
        {
            Id              = Guid.NewGuid().ToString("N"),
            StoryId         = story.ID,
            Order           = creatingJourney.Chapters.Count,
            InteractionType = "read"
        });
        SaveJourneyToFirestore(creatingJourney);
        onStoryAddedToJourney?.Invoke(story);
        GoogleSheetsFetcher.instance?.RefreshMap();
    }

    public void FinishCreatingJourney(string title, string description)
    {
        if (!isCreatingJourney || creatingJourney == null) return;
        if (!_isEditMode && (creatingJourney.Chapters == null || creatingJourney.Chapters.Count == 0))
        {
            Debug.Log("[Journey] FinishCreatingJourney: 0 chapters — ignoring Finish (user must add at least one story)");
            return;
        }
        creatingJourney.Title       = string.IsNullOrWhiteSpace(title) ? "My Journey" : title.Trim();
        creatingJourney.Description = description ?? "";
        creatingJourney.Draft       = false;
        StopCreationTracking();
        bool wasEdit = _isEditMode;
        _isEditMode = false;
        if (!wasEdit)
        {
            PlayerPrefs.DeleteKey("Journey.CreatingId");
            PlayerPrefs.DeleteKey("Journey.CreatingStartLat");
            PlayerPrefs.DeleteKey("Journey.CreatingStartLon");
            PlayerPrefs.Save();
        }
        var j = creatingJourney;
        isCreatingJourney = false;
        creatingJourney   = null;
        SaveJourneyToFirestore(j);
        JourneyLibraryPanel.instance?.PopulateJourneysList();
        if (!wasEdit)
            ActivateJourney(j, showPopup: false);
        else
            GoogleSheetsFetcher.instance?.RefreshMap();
    }

    public void CancelCreatingJourney()
    {
        Debug.Log($"[Journey] CancelCreatingJourney called\n{System.Environment.StackTrace}");
        StopCreationTracking();
        if (_isEditMode)
        {
            _isEditMode       = false;
            isCreatingJourney = false;
            creatingJourney   = null;
            GoogleSheetsFetcher.instance?.RefreshMap();
            return;
        }
        PlayerPrefs.DeleteKey("Journey.CreatingId");
        PlayerPrefs.DeleteKey("Journey.CreatingStartLat");
        PlayerPrefs.DeleteKey("Journey.CreatingStartLon");
        PlayerPrefs.Save();
        if (creatingJourney != null)
        {
            GoogleSheetsFetcher.instance?.journeysList?.Remove(creatingJourney);
            FirebaseFirestore.DefaultInstance
                .Collection("Journeys").Document(creatingJourney.ID)
                .DeleteAsync();
        }
        isCreatingJourney = false;
        creatingJourney   = null;
    }

    public void DeleteJourney(JourneyEntry journey)
    {
        if (journey == null) return;

        string userId = UserProfileManager.instance?.UserId;

        // Delete owned stories and immediately hide all chapter pins
        var fetcher = GoogleSheetsFetcher.instance;
        if (fetcher != null && journey.Chapters != null)
        {
            foreach (var chapter in journey.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId)) continue;
                var story = fetcher.storiesList?.Find(e => e?.ID == chapter.StoryId)
                         ?? fetcher.landmarksList?.Find(e => e?.ID == chapter.StoryId);
                if (story == null) continue;
                if (story.User == userId)
                    fetcher.DeleteEntryFromFirestore(story);
                else if (story.pointer != null)
                    story.pointer.gameObject.SetActive(false);
            }
        }

        // Delete the journey document and clean up local state
        FirebaseFirestore.DefaultInstance
            .Collection("Journeys").Document(journey.ID)
            .DeleteAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning($"[Journey] Delete failed: {task.Exception}");
            });

        GoogleSheetsFetcher.instance?.journeysList?.Remove(journey);

        if (activeJourney?.ID == journey.ID)
            DeactivateJourney();

        if (isCreatingJourney && creatingJourney?.ID == journey.ID)
        {
            StopCreationTracking();
            _isEditMode = false;
            isCreatingJourney = false;
            creatingJourney = null;
            PlayerPrefs.DeleteKey("Journey.CreatingId");
            PlayerPrefs.Save();
        }

        // Delete progress record
        if (!string.IsNullOrEmpty(userId))
        {
            FirebaseFirestore.DefaultInstance
                .Collection(UserProfilesCollection).Document(userId)
                .Collection(ProgressSubcollection).Document(journey.ID)
                .DeleteAsync();
        }

        progressCache.Remove(journey.ID);
        PlayerPrefs.DeleteKey(PrefKeyProgressPfx + journey.ID);
        PlayerPrefs.Save();

        JourneyLibraryPanel.instance?.PopulateJourneysList();
        GoogleSheetsFetcher.instance?.RefreshMap();
    }

    /// Called after any story is deleted. Removes the story from every journey's
    /// chapter list locally and in Firestore, then refreshes the map.
    public void RemoveStoryFromAllJourneys(string storyId)
    {
        if (string.IsNullOrEmpty(storyId)) return;

        string userId  = UserProfileManager.instance?.UserId;
        var journeys   = GoogleSheetsFetcher.instance?.journeysList;
        if (journeys == null) return;

        bool mapDirty = false;

        foreach (var journey in journeys)
        {
            if (journey?.Chapters == null) continue;
            int removed = journey.Chapters.RemoveAll(c => c.StoryId == storyId);
            if (removed == 0) continue;

            // Re-sequence Order so Order==0 always identifies the first chapter
            for (int i = 0; i < journey.Chapters.Count; i++)
                journey.Chapters[i].Order = i;

            mapDirty = true;

            if (!string.IsNullOrEmpty(userId) && journey.User == userId)
                SaveJourneyToFirestore(journey);
        }

        if (mapDirty)
            GoogleSheetsFetcher.instance?.RefreshMap();
    }

    private void StopCreationTracking()
    {
        MapLoader.onStyleChanged     -= OnCreationStyleChanged;
        GPSManager.OnPositionSampled -= OnCreationGpsSampled;
    }

    private void OnCreationStyleChanged()
    {
        if (creatingJourney != null && MapLoader.instance != null)
            creatingJourney.MapStyleIndex = MapLoader.instance.currentStyleIndex;
    }

    private void OnCreationGpsSampled(float lat, float lon)
    {
        if (!isCreatingJourney || _isEditMode) return;
        if (DistMetres(lat, lon, _creationStartLat, _creationStartLon) > creationDistanceLimitMetres)
        {
            onJourneyCreationDistanceLimitReached?.Invoke();
            GPSManager.OnPositionSampled -= OnCreationGpsSampled;
        }
    }

    private void SaveJourneyToFirestore(JourneyEntry journey)
    {
        string userId = UserProfileManager.instance?.UserId;
        var chapters = new List<object>();
        foreach (var ch in journey.Chapters)
            chapters.Add(new Dictionary<string, object>
            {
                { "Id", ch.Id ?? "" }, { "StoryId", ch.StoryId ?? "" },
                { "Order", ch.Order }, { "InteractionType", ch.InteractionType ?? "read" }
            });
        var data = new Dictionary<string, object>
        {
            { "Title",         journey.Title ?? "" },
            { "Description",   journey.Description ?? "" },
            { "FontID",        journey.FontID },
            { "StickerID",     journey.StickerID },
            { "MapStyleIndex", journey.MapStyleIndex },
            { "Tags",          journey.Tags ?? new List<string>() },
            { "Created",       journey.Created },
            { "Chapters",      chapters },
            { "User",          userId ?? "" },
            { "Draft",         journey.Draft }
        };
        FirebaseFirestore.DefaultInstance
            .Collection("Journeys").Document(journey.ID)
            .SetAsync(data)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning($"[Journey] Save failed: {task.Exception}");
                else
                    Debug.Log($"[Journey] Saved journey {journey.ID}");
            });
    }

    // ── ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<string>> progressCache = new Dictionary<string, List<string>>();

    private Coroutine _routeCoroutine;
    private Coroutine _progressLoadCoroutine;

    private const string PrefKeyActiveId = "Journey.ActiveId";
    private const string PrefKeyPrevStyle = "Journey.PrevStyleIndex";
    private const string PrefKeyProgressPfx = "Journey.Progress.";
    private const string UserProfilesCollection = "UserProfiles";
    private const string ProgressSubcollection = "JourneyProgress";

    public JourneyEntry pendingJourneyPopup;

    private void Awake()
    {
        instance = this;

        if (mapJourneyCard != null)
            mapJourneyCard.gameObject.SetActive(false);
    }

    public void ActivateJourney(JourneyEntry journey, bool showPopup = true)
    {
        if (journey == null)
            return;

        if (activeJourney != null && activeJourney.ID != journey.ID)
            DeactivateJourney();

        activeJourney = journey;

        int prevStyle = PlayerPrefs.GetInt("MapStyleIndex", 0);
        PlayerPrefs.SetInt(PrefKeyPrevStyle, prevStyle);
        PlayerPrefs.SetString(PrefKeyActiveId, journey.ID);
        PlayerPrefs.Save();

        if (MapLoader.instance != null && MapLoader.instance.currentStyleIndex != journey.MapStyleIndex)
            MapLoader.instance.ChooseStyle(journey.MapStyleIndex);

        if (!progressCache.ContainsKey(journey.ID))
        {
            var prefsProgress = LoadProgressFromPrefs(journey.ID);

            if (prefsProgress.Count > 0)
                progressCache[journey.ID] = prefsProgress;
        }

        if (mapJourneyCard != null)
        {
            mapJourneyCard.Initialise(journey, null);
            mapJourneyCard.gameObject.SetActive(true);
        }

        GoogleSheetsFetcher.instance?.RefreshMap();
        onJourneyActivated?.Invoke();

        if (showPopup)
            pendingJourneyPopup = journey;

        DrawJourneyRoute();

        if (_progressLoadCoroutine != null)
            StopCoroutine(_progressLoadCoroutine);

        _progressLoadCoroutine = StartCoroutine(LoadProgressWhenUserReady(journey.ID, () =>
        {
            if (activeJourney == null || activeJourney.ID != journey.ID)
                return;

            mapJourneyCard?.RefreshProgress();
            JourneyLibraryPanel.instance?.RefreshJourneyProgress();
            DrawJourneyRoute();
            GoogleSheetsFetcher.instance?.RefreshMap();
        }));
    }

    public void DeactivateJourney()
    {
        if (activeJourney == null)
            return;

        activeJourney = null;
        pendingJourneyPopup = null;

        if (_progressLoadCoroutine != null)
        {
            StopCoroutine(_progressLoadCoroutine);
            _progressLoadCoroutine = null;
        }

        PlayerPrefs.DeleteKey(PrefKeyActiveId);
        PlayerPrefs.Save();

        int prevStyle = PlayerPrefs.GetInt(PrefKeyPrevStyle, 0);

        if (MapLoader.instance != null && MapLoader.instance.currentStyleIndex != prevStyle)
            MapLoader.instance.ChooseStyle(prevStyle);

        ClearRoute();

        if (mapJourneyCard != null)
            mapJourneyCard.gameObject.SetActive(false);

        GoogleSheetsFetcher.instance?.RefreshMap();
        onJourneyDeactivated?.Invoke();
    }

    public void OnJourneysLoaded()
    {
        StartCoroutine(PrewarmJourneyPhotos());

        // Restore in-progress creation if the app restarted mid-creation
        string creatingId = PlayerPrefs.GetString("Journey.CreatingId", "");
        if (!string.IsNullOrEmpty(creatingId))
        {
            var draft = GoogleSheetsFetcher.instance?.journeysList?.Find(j => j != null && j.ID == creatingId);
            if (draft != null)
            {
                isCreatingJourney = true;
                creatingJourney   = draft;
                _creationStartLat = PlayerPrefs.GetFloat("Journey.CreatingStartLat", 0f);
                _creationStartLon = PlayerPrefs.GetFloat("Journey.CreatingStartLon", 0f);
                MapLoader.onStyleChanged     += OnCreationStyleChanged;
                GPSManager.OnPositionSampled += OnCreationGpsSampled;
                StartCoroutine(RestoreCreationStateNextFrame());
                return;
            }
            PlayerPrefs.DeleteKey("Journey.CreatingId");
            PlayerPrefs.DeleteKey("Journey.CreatingStartLat");
            PlayerPrefs.DeleteKey("Journey.CreatingStartLon");
            PlayerPrefs.Save();
        }

        string savedId = PlayerPrefs.GetString(PrefKeyActiveId, "");
        if (!string.IsNullOrEmpty(savedId))
        {
            var journey = GoogleSheetsFetcher.instance?.journeysList?.Find(j => j != null && j.ID == savedId);
            if (journey != null)
                ActivateJourney(journey, showPopup: false);
        }
    }

    private IEnumerator RestoreCreationStateNextFrame()
    {
        yield return null;
        UIStateManager.instance?.ActivateCreateJourneySubState();
    }


    private IEnumerator PrewarmJourneyPhotos()
    {
        // Wait for the map to finish its initial load before competing for bandwidth
        yield return new WaitUntil(() => MapLoader.instance != null && !MapLoader.instance.IsMainMapReloading);

        // Re-sync pin positions now the map is ready
        if (activeJourney != null)
        {
            GoogleSheetsFetcher.instance?.RefreshMap();
            DrawJourneyRoute();
        }

        var journeys = GoogleSheetsFetcher.instance?.journeysList;
        if (journeys == null) yield break;
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
                    yield return PhotoAsset.Prewarm(story.PhotoUrl);
            }
        }
    }

    public void OnStoryRead(string storyId)
    {
        if (activeJourney == null || string.IsNullOrEmpty(storyId))
        {
            Debug.Log($"[Journey] OnStoryRead bail: activeJourney={activeJourney == null} storyId={storyId}");
            return;
        }

        var chapter = activeJourney.Chapters?.Find(c =>
            c.StoryId == storyId &&
            (string.IsNullOrEmpty(c.InteractionType) ||
             string.Equals(c.InteractionType, "read", StringComparison.OrdinalIgnoreCase)));

        if (chapter == null)
        {
            Debug.Log($"[Journey] OnStoryRead bail: no chapter found for storyId={storyId} in journey {activeJourney.Title}. Chapter StoryIds: {string.Join(", ", activeJourney.Chapters?.ConvertAll(c => c.StoryId + "(" + c.InteractionType + ")") ?? new List<string>())}");
            return;
        }

        Debug.Log($"[Journey] OnStoryRead matched chapter {chapter.Id} (Order={chapter.Order})");
        MarkChapterComplete(activeJourney.ID, chapter.Id);
    }

    public void MarkChapterComplete(string journeyId, string chapterId)
    {
        if (string.IsNullOrEmpty(journeyId) || string.IsNullOrEmpty(chapterId))
        {
            Debug.Log("[Journey] MarkChapterComplete bail: empty id");
            return;
        }

        if (!progressCache.TryGetValue(journeyId, out var completed))
        {
            completed = new List<string>();
            progressCache[journeyId] = completed;
        }

        if (completed.Contains(chapterId))
        {
            Debug.Log($"[Journey] MarkChapterComplete bail: {chapterId} already completed");
            return;
        }

        completed.Add(chapterId);

        SaveProgressToPrefs(journeyId, completed);
        PersistProgressToFirestore(journeyId, completed);

        JourneyLibraryPanel.instance?.RefreshJourneyProgress();
        mapJourneyCard?.RefreshProgress();

        if (activeJourney != null && activeJourney.ID == journeyId)
        {
            DrawJourneyRoute();
            GoogleSheetsFetcher.instance?.RefreshMap();

            var storyChapters = activeJourney.Chapters?.FindAll(c => !string.IsNullOrEmpty(c.StoryId));
            bool allDone = storyChapters != null && storyChapters.TrueForAll(c => completed.Contains(c.Id));

            Debug.Log($"[Journey] MarkChapterComplete: completed={completed.Count}/{storyChapters?.Count} allDone={allDone} completedIds=[{string.Join(",", completed)}] chapterIds=[{string.Join(",", storyChapters?.ConvertAll(c => c.Id) ?? new List<string>())}]");

            if (allDone)
            {
                Debug.Log("[Journey] Journey complete — showing popup immediately");

                if (journeyCompleteTitleText != null)
                    journeyCompleteTitleText.text = activeJourney.Title ?? "";

                if (journeyCompletePopup != null)
                    journeyCompletePopup.SetActive(true);
                else
                    Debug.LogWarning("[Journey] journeyCompletePopup is not assigned.");
            }
        }
        else
        {
            Debug.Log($"[Journey] MarkChapterComplete: activeJourney mismatch — activeJourney={activeJourney?.ID} journeyId={journeyId}");
        }
    }

    public void ResetActiveJourneyProgress()
    {
        if (activeJourney == null)
            return;

        string journeyId = activeJourney.ID;

        if (string.IsNullOrEmpty(journeyId))
            return;

        progressCache.Remove(journeyId);
        PlayerPrefs.DeleteKey(PrefKeyProgressPfx + journeyId);
        PlayerPrefs.Save();

        string userId = UserProfileManager.instance?.UserId;

        if (!string.IsNullOrEmpty(userId))
        {
            FirebaseFirestore.DefaultInstance
                .Collection(UserProfilesCollection).Document(userId)
                .Collection(ProgressSubcollection).Document(journeyId)
                .DeleteAsync()
                .ContinueWithOnMainThread(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                        Debug.LogWarning($"[Journey] Failed to delete Firestore progress: {task.Exception}");
                    else
                        Debug.Log($"[Journey] Deleted Firestore progress for {journeyId}");
                });
        }
        else
        {
            Debug.LogWarning("[Journey] ResetActiveJourneyProgress: UserId empty, only local progress cleared.");
        }

        JourneyLibraryPanel.instance?.RefreshJourneyProgress();
        mapJourneyCard?.RefreshProgress();
        DrawJourneyRoute();
        GoogleSheetsFetcher.instance?.RefreshMap();
    }

    public float GetProgressPercent(string journeyId)
    {
        var journey = GoogleSheetsFetcher.instance?.journeysList?.Find(j => j != null && j.ID == journeyId);
        if (journey?.Chapters == null) return 0f;

        int total = journey.Chapters.FindAll(c => !string.IsNullOrEmpty(c.StoryId)).Count;
        if (total == 0) return 0f;

        if (!progressCache.TryGetValue(journeyId, out var completed))
            return 0f;

        return Mathf.Min(1f, (float)completed.Count / total);
    }

    public List<string> GetCompletedChapterIds(string journeyId)
    {
        return progressCache.TryGetValue(journeyId, out var list) ? list : new List<string>();
    }

    public bool IsJourneyStoryVisible(string storyId)
    {
        if (string.IsNullOrEmpty(storyId))
            return true;

        if (GoogleSheetsFetcher.instance?.journeysList == null)
            return true;

        bool isAnyJourneyChapter = false;
        string currentUserId = UserProfileManager.instance?.UserId;

        foreach (var journey in GoogleSheetsFetcher.instance.journeysList)
        {
            if (journey?.Chapters == null)
                continue;

            foreach (var chapter in journey.Chapters)
            {
                if (chapter.StoryId != storyId)
                    continue;

                isAnyJourneyChapter = true;

                // Owned journeys: all stories always visible
                if (!string.IsNullOrEmpty(currentUserId) && journey.User == currentUserId)
                    return true;

                // Other journeys: first story always visible for discovery (not for drafts)
                if (chapter.Order == 0 && !journey.Draft)
                    return true;

                // Other journeys: completed chapters + next-in-chain visible when journey is active
                if (activeJourney != null && activeJourney.ID == journey.ID)
                {
                    var completedIds = GetCompletedChapterIds(journey.ID);

                    if (completedIds.Contains(chapter.Id))
                        return true;

                    bool isNext = true;

                    foreach (var c in journey.Chapters)
                    {
                        if (string.IsNullOrEmpty(c.StoryId))
                            continue;

                        if (c.Order >= chapter.Order)
                            continue;

                        if (!completedIds.Contains(c.Id))
                        {
                            isNext = false;
                            break;
                        }
                    }

                    if (isNext)
                        return true;
                }
            }
        }

        if (!isAnyJourneyChapter)
            return true;

        return false;
    }

    public JourneyEntry GetJourneyForFirstChapter(string storyId)
    {
        if (string.IsNullOrEmpty(storyId) || GoogleSheetsFetcher.instance?.journeysList == null)
            return null;

        foreach (var journey in GoogleSheetsFetcher.instance.journeysList)
        {
            if (journey?.Chapters == null)
                continue;

            var ch = journey.Chapters.Find(c => c.StoryId == storyId && c.Order == 0);

            if (ch != null)
                return journey;
        }

        return null;
    }

    public bool IsChapterUnlocked(JourneyEntry journey, JourneyEntry.ChapterDef chapter)
    {
        if (journey == null || chapter == null)
            return false;

        if (chapter.Order == 0)
            return true;

        var prevChapter = journey.Chapters?.Find(c => c.Order == chapter.Order - 1);

        if (prevChapter != null)
        {
            var completed = GetCompletedChapterIds(journey.ID);

            if (!completed.Contains(prevChapter.Id))
                return false;
        }

        var cond = chapter.Condition;

        if (cond == null || cond.Type == "always" || string.IsNullOrEmpty(cond.Type))
            return true;

        switch (cond.Type)
        {
            case "proximity":
                if (GPSManager.Instance == null)
                    return false;

                float dist = DistMetres(
                    GPSManager.Instance.latitude,
                    GPSManager.Instance.longitude,
                    cond.Latitude,
                    cond.Longitude
                );

                return dist <= cond.RadiusMetres;

            case "time_of_day":
                int hour = DateTime.Now.Hour;
                return hour >= cond.HourFrom && hour <= cond.HourTo;

            case "time_delay":
                return true;

            case "write_story":
                if (GPSManager.Instance == null)
                    return false;

                foreach (var story in GoogleSheetsFetcher.instance?.storiesList ?? new List<GoogleSheetsFetcher.Entry>())
                {
                    if (story == null)
                        continue;

                    if (UserProfileManager.instance == null || !UserProfileManager.instance.IsCurrentUser(story.User))
                        continue;

                    if (DistMetres(story.Latitude, story.Longitude, cond.Latitude, cond.Longitude) <= cond.RadiusMetres)
                        return true;
                }

                return false;

            case "seasonal":
                int month = DateTime.Now.Month;
                return month >= cond.MonthFrom && month <= cond.MonthTo;

            default:
                return true;
        }
    }

    public void DrawJourneyRoute()
    {
        if (activeJourney?.Chapters == null)
            return;

        if (_routeCoroutine != null)
            StopCoroutine(_routeCoroutine);

        _routeCoroutine = StartCoroutine(DrawJourneyRouteCoroutine());
    }

    private IEnumerator DrawJourneyRouteCoroutine()
    {
        while (GPSManager.Instance == null ||
               (GPSManager.Instance.latitude == 0f && GPSManager.Instance.longitude == 0f))
        {
            if (activeJourney == null)
                yield break;

            yield return new WaitForSeconds(0.5f);
        }

        if (activeJourney == null)
            yield break;

        var completed = GetCompletedChapterIds(activeJourney.ID);

        JourneyEntry.ChapterDef nextTarget = null;

        foreach (var chapter in activeJourney.Chapters)
        {
            if (string.IsNullOrEmpty(chapter.StoryId))
                continue;

            if (!completed.Contains(chapter.Id))
            {
                nextTarget = chapter;
                break;
            }
        }

        if (nextTarget == null)
        {
            var allPoints = new List<(float lat, float lon)>();

            foreach (var chapter in activeJourney.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId))
                    continue;

                var s = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == chapter.StoryId)
                     ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == chapter.StoryId);

                if (s != null && (s.Latitude != 0f || s.Longitude != 0f))
                    allPoints.Add((s.Latitude, s.Longitude));
            }

            if (allPoints.Count >= 2)
                yield return FetchAndDrawAllCompletedRoute(allPoints);

            yield break;
        }

        var nextStory = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == nextTarget.StoryId)
                     ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == nextTarget.StoryId);

        if (nextStory == null || (nextStory.Latitude == 0f && nextStory.Longitude == 0f))
            yield break;

        var routePoints = new List<(float lat, float lon)>();

        foreach (var chapter in activeJourney.Chapters)
        {
            if (string.IsNullOrEmpty(chapter.StoryId))
                continue;

            if (!completed.Contains(chapter.Id))
                break;

            var story = GoogleSheetsFetcher.instance?.storiesList?.Find(e => e?.ID == chapter.StoryId)
                     ?? GoogleSheetsFetcher.instance?.landmarksList?.Find(e => e?.ID == chapter.StoryId);

            if (story != null && (story.Latitude != 0f || story.Longitude != 0f))
                routePoints.Add((story.Latitude, story.Longitude));
        }

        // Mapbox Directions API hard limit is 25 waypoints.
        // Trim the oldest completed stops from the trail if over limit (keep player + target).
        const int MapboxMaxWaypoints = 25;
        while (routePoints.Count > MapboxMaxWaypoints - 2)
            routePoints.RemoveAt(0);

        int playerIdx = routePoints.Count;

        routePoints.Add((GPSManager.Instance.latitude, GPSManager.Instance.longitude));
        routePoints.Add((nextStory.Latitude, nextStory.Longitude));

        yield return FetchAndDrawRoute(routePoints, playerIdx);
    }

    private IEnumerator FetchAndDrawRoute(List<(float lat, float lon)> points, int playerWaypointIndex = 0)
    {
        var coords = new System.Text.StringBuilder();

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                coords.Append(';');

            coords.Append($"{points[i].lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{points[i].lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        string url = $"https://api.mapbox.com/directions/v5/mapbox/walking/{coords}?geometries=geojson&access_token={MapLoader.instance.mapboxToken}";

        using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Journey] Route fetch failed: {req.error}");
                yield break;
            }

            string json = req.downloadHandler.text;
            var rawCoords = ParseGeoJsonCoords(json);

            if (rawCoords == null || rawCoords.Count < 2)
                yield break;

            rawCoords.Insert(0, new[] { points[0].lon, points[0].lat });
            rawCoords.Add(new[] { points[points.Count - 1].lon, points[points.Count - 1].lat });

            if (playerWaypointIndex == 0)
            {
                MapRouteManager.instance?.ClearJourneyTrail();
                MapRouteManager.instance?.SetJourneyRoute(rawCoords, 0);
            }
            else
            {
                float pLat = points[playerWaypointIndex].lat;
                float pLon = points[playerWaypointIndex].lon;
                int split = FindClosestCoordIndex(rawCoords, pLat, pLon);

                var trail = rawCoords.GetRange(0, split + 1);
                var live = rawCoords.GetRange(split, rawCoords.Count - split);

                MapRouteManager.instance?.SetJourneyTrailRoute(trail);
                MapRouteManager.instance?.SetJourneyRoute(live, 0);
            }
        }
    }

    private static List<float[]> ParseGeoJsonCoords(string json)
    {
        int start = json.IndexOf("\"coordinates\":[[", StringComparison.Ordinal);

        if (start < 0)
            return null;

        start += 16;

        int end = json.IndexOf("]]", start);

        if (end < 0)
            return null;

        string section = json.Substring(start, end - start + 1);
        var matches = System.Text.RegularExpressions.Regex.Matches(section, @"\[(-?[\d.]+),(-?[\d.]+)\]");

        if (matches.Count == 0)
            return null;

        var list = new List<float[]>(matches.Count);

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lon) &&
                float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lat))
            {
                list.Add(new[] { lon, lat });
            }
        }

        return list;
    }

    private void ClearRoute()
    {
        if (_routeCoroutine != null)
        {
            StopCoroutine(_routeCoroutine);
            _routeCoroutine = null;
        }

        MapRouteManager.instance?.ClearJourneyRoute();
        MapRouteManager.instance?.ClearJourneyTrail();
    }

    private void PersistProgressToFirestore(string journeyId, List<string> completedChapterIds)
    {
        string userId = UserProfileManager.instance?.UserId;

        Debug.Log($"[Journey] PersistProgressToFirestore journeyId={journeyId}, userId={userId}, completed={completedChapterIds.Count}");

        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("[Journey] Cannot persist progress — UserId is empty.");
            return;
        }

        var data = new Dictionary<string, object>
        {
            { "CompletedChapterIds", new List<string>(completedChapterIds) },
            { "LastUpdatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
        };

        FirebaseFirestore.DefaultInstance
            .Collection(UserProfilesCollection).Document(userId)
            .Collection(ProgressSubcollection).Document(journeyId)
            .SetAsync(data, SetOptions.MergeAll)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                    Debug.LogWarning($"[Journey] Failed to persist progress: {task.Exception}");
                else
                    Debug.Log($"[Journey] Persisted progress to Firestore: UserProfiles/{userId}/{ProgressSubcollection}/{journeyId}");
            });
    }

    private IEnumerator LoadProgressWhenUserReady(string journeyId, Action onComplete = null)
    {
        float timeout = 10f;
        float elapsed = 0f;

        while ((UserProfileManager.instance == null || string.IsNullOrEmpty(UserProfileManager.instance.UserId)) && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (UserProfileManager.instance == null || string.IsNullOrEmpty(UserProfileManager.instance.UserId))
        {
            Debug.LogWarning("[Journey] UserId still not ready after timeout. Firestore progress not loaded.");
            onComplete?.Invoke();
            yield break;
        }

        LoadProgressFromFirestore(journeyId, onComplete);
    }

    public void LoadProgressFromFirestore(string journeyId, Action onComplete = null)
    {
        string userId = UserProfileManager.instance?.UserId;

        Debug.Log($"[Journey] LoadProgressFromFirestore journeyId={journeyId}, userId={userId}, hasUserProfileManager={UserProfileManager.instance != null}");

        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("[Journey] Cannot load progress yet — UserId is empty.");
            onComplete?.Invoke();
            return;
        }

        FirebaseFirestore.DefaultInstance
            .Collection(UserProfilesCollection).Document(userId)
            .Collection(ProgressSubcollection).Document(journeyId)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning($"[Journey] Failed to load progress from Firestore: {task.Exception}");
                    onComplete?.Invoke();
                    return;
                }

                Debug.Log($"[Journey] Firestore progress exists={task.Result.Exists} path=UserProfiles/{userId}/{ProgressSubcollection}/{journeyId}");

                if (task.Result.Exists)
                {
                    var data = task.Result.ToDictionary();

                    if (data.TryGetValue("CompletedChapterIds", out object raw) && raw is IEnumerable<object> items)
                    {
                        var firestoreList = new List<string>();

                        foreach (var item in items)
                        {
                            if (item != null)
                                firestoreList.Add(item.ToString());
                        }

                        var merged = new List<string>();

                        if (progressCache.TryGetValue(journeyId, out var existing))
                        {
                            foreach (var id in existing)
                            {
                                if (!string.IsNullOrEmpty(id) && !merged.Contains(id))
                                    merged.Add(id);
                            }
                        }

                        foreach (var id in firestoreList)
                        {
                            if (!string.IsNullOrEmpty(id) && !merged.Contains(id))
                                merged.Add(id);
                        }

                        progressCache[journeyId] = merged;
                        SaveProgressToPrefs(journeyId, merged);

                        Debug.Log($"[Journey] Loaded progress from Firestore: {merged.Count} completed chapters [{string.Join(",", merged)}]");
                    }
                    else
                    {
                        Debug.Log("[Journey] Firestore progress doc exists, but CompletedChapterIds missing or invalid.");
                    }
                }

                onComplete?.Invoke();
            });
    }

    private IEnumerator FetchAndDrawAllCompletedRoute(List<(float lat, float lon)> points)
    {
        var coords = new System.Text.StringBuilder();

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                coords.Append(';');

            coords.Append($"{points[i].lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{points[i].lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        string url = $"https://api.mapbox.com/directions/v5/mapbox/walking/{coords}?geometries=geojson&access_token={MapLoader.instance.mapboxToken}";

        using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Journey] Completed route fetch failed: {req.error}");
                yield break;
            }

            var rawCoords = ParseGeoJsonCoords(req.downloadHandler.text);

            if (rawCoords == null || rawCoords.Count < 2)
                yield break;

            rawCoords.Insert(0, new[] { points[0].lon, points[0].lat });
            rawCoords.Add(new[] { points[points.Count - 1].lon, points[points.Count - 1].lat });

            MapRouteManager.instance?.ClearJourneyRoute();
            MapRouteManager.instance?.SetJourneyTrailRoute(rawCoords);
        }
    }

    private static int FindClosestCoordIndex(List<float[]> coords, float lat, float lon)
    {
        int best = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < coords.Count; i++)
        {
            float dlat = coords[i][1] - lat;
            float dlon = coords[i][0] - lon;
            float d = dlat * dlat + dlon * dlon;

            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private void SaveProgressToPrefs(string journeyId, List<string> completed)
    {
        PlayerPrefs.SetString(PrefKeyProgressPfx + journeyId, string.Join(",", completed));
        PlayerPrefs.Save();
    }

    private List<string> LoadProgressFromPrefs(string journeyId)
    {
        string raw = PlayerPrefs.GetString(PrefKeyProgressPfx + journeyId, "");

        if (string.IsNullOrEmpty(raw))
            return new List<string>();

        return new List<string>(raw.Split(','));
    }

    private static float DistMetres(float lat1, float lon1, float lat2, float lon2)
    {
        float dy = (lat1 - lat2) * 111320f;
        float dx = (lon1 - lon2) * (111320f * Mathf.Cos(lat1 * Mathf.Deg2Rad));

        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}