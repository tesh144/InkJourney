using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

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

    private readonly Dictionary<string, List<string>> progressCache = new Dictionary<string, List<string>>();

    private Coroutine _routeCoroutine;

    private readonly List<MapPointer> spawnedChapterPointers = new List<MapPointer>();

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

        SpawnChapterPointers();

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

        LoadProgressFromFirestore(journey.ID, () =>
        {
            mapJourneyCard?.RefreshProgress();
            DrawJourneyRoute();
            GoogleSheetsFetcher.instance?.RefreshMap();
        });
    }

    public void DeactivateJourney()
    {
        if (activeJourney == null)
            return;

        activeJourney = null;

        pendingJourneyPopup = null;

        PlayerPrefs.DeleteKey(PrefKeyActiveId);
        PlayerPrefs.Save();

        int prevStyle = PlayerPrefs.GetInt(PrefKeyPrevStyle, 0);

        if (MapLoader.instance != null && MapLoader.instance.currentStyleIndex != prevStyle)
            MapLoader.instance.ChooseStyle(prevStyle);

        ClearChapterPointers();
        ClearRoute();

        if (mapJourneyCard != null)
            mapJourneyCard.gameObject.SetActive(false);

        GoogleSheetsFetcher.instance?.RefreshMap();
        onJourneyDeactivated?.Invoke();
    }

    public void OnJourneysLoaded()
    {
        string savedId = PlayerPrefs.GetString(PrefKeyActiveId, "");

        if (string.IsNullOrEmpty(savedId))
            return;

        var journey = GoogleSheetsFetcher.instance?.journeysList?.Find(j => j != null && j.ID == savedId);

        if (journey != null)
            ActivateJourney(journey, showPopup: false);
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
                .DeleteAsync();
        }

        JourneyLibraryPanel.instance?.RefreshJourneyProgress();
        mapJourneyCard?.RefreshProgress();
        DrawJourneyRoute();
        GoogleSheetsFetcher.instance?.RefreshMap();
    }

    public float GetProgressPercent(string journeyId)
    {
        var journey = GoogleSheetsFetcher.instance?.journeysList?.Find(j => j != null && j.ID == journeyId);

        if (journey == null || journey.Chapters == null || journey.Chapters.Count == 0)
            return 0f;

        if (!progressCache.TryGetValue(journeyId, out var completed))
            return 0f;

        return (float)completed.Count / journey.Chapters.Count;
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

        foreach (var journey in GoogleSheetsFetcher.instance.journeysList)
        {
            if (journey?.Chapters == null)
                continue;

            foreach (var chapter in journey.Chapters)
            {
                if (chapter.StoryId != storyId)
                    continue;

                isAnyJourneyChapter = true;

                if (chapter.Order == 0)
                    return true;

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

    private void SpawnChapterPointers()
    {
        spawnedChapterPointers.Clear();
    }

    private void ClearChapterPointers()
    {
        spawnedChapterPointers.Clear();
    }

    private void PersistProgressToFirestore(string journeyId, List<string> completedChapterIds)
    {
        string userId = UserProfileManager.instance?.UserId;

        if (string.IsNullOrEmpty(userId))
            return;

        var data = new Dictionary<string, object>
        {
            { "CompletedChapterIds", completedChapterIds },
            { "LastUpdatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
        };

        FirebaseFirestore.DefaultInstance
            .Collection(UserProfilesCollection).Document(userId)
            .Collection(ProgressSubcollection).Document(journeyId)
            .SetAsync(data, SetOptions.MergeAll)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning($"[Journey] Failed to persist progress: {task.Exception}");
            });
    }

    public void LoadProgressFromFirestore(string journeyId, Action onComplete = null)
    {
        string userId = UserProfileManager.instance?.UserId;

        if (string.IsNullOrEmpty(userId))
        {
            onComplete?.Invoke();
            return;
        }

        FirebaseFirestore.DefaultInstance
            .Collection(UserProfilesCollection).Document(userId)
            .Collection(ProgressSubcollection).Document(journeyId)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsFaulted && !task.IsCanceled && task.Result.Exists)
                {
                    var data = task.Result.ToDictionary();

                    if (data.TryGetValue("CompletedChapterIds", out object raw) && raw is IEnumerable<object> items)
                    {
                        var list = new List<string>();

                        foreach (var item in items)
                        {
                            if (item != null)
                                list.Add(item.ToString());
                        }

                        progressCache[journeyId] = list;
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
                yield break;

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