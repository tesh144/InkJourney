using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Extensions;
using Firebase.Storage;

public class GoogleSheetsFetcher : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject storyPrefab;
    public GameObject landmarkPrefab;

    [Header("Settings")]
    public RectTransform mapParentTransform;
    public RectTransform landmarkMapParentTransform;
    public RectTransform journeyMapParentTransform;
    public float maxDistanceMeters = 600f;
    public float maxJourneyDistanceMeters = 25000f;
    public float storyConflictDistanceMeters = 50f;

    [System.Serializable]
    public class Entry
    {
        [System.Serializable]
        public class Comment
        {
            public string CommentId;
            public string UserId;
            public string UserName;
            public string Text;
            public long Created;
        }

        public string ID;
        public string User;
        public string UserName;
        public float Latitude;
        public float Longitude;
        public string Title;
        public string Content;
        public string Theme;
        public string Track;
        public string Font;
        public int Saves;
        public List<string> SavedByUserIds = new List<string>();
        public int LikesCount;
        public int Views;
        public long Created;
        public long LastUpdated;
        public long Expire;
        public string PhotoUrl;
        public int StickerID;
        public int FontID;
        public List<string> Tags = new List<string>();
        public List<Comment> Comments = new List<Comment>();
        public MapPointer pointer;
        public string cachedLocation;
    }

    public List<Entry> landmarksList = new List<Entry>();
    public List<Entry> storiesList = new List<Entry>();
    public List<JourneyEntry> journeysList = new List<JourneyEntry>();
    public List<MapPointer> instancedPointers;

    private bool _storiesReady = false;
    private bool _landmarksReady = false;
    private bool _journeysReady = false;

    private Coroutine _refreshCoroutine;

    private Dictionary<Vector2Int, List<Entry>> gridCells = new Dictionary<Vector2Int, List<Entry>>();

    public static GoogleSheetsFetcher instance;
    private FirebaseFirestore db;
    private bool firebaseReady = false;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                db = FirebaseFirestore.DefaultInstance;

                FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWithOnMainThread(authTask =>
                {
                    if (authTask.IsFaulted || authTask.IsCanceled)
                        Debug.LogWarning("[Firebase] Anonymous sign-in failed — story writes may be blocked: " + authTask.Exception);

                    firebaseReady = true;

                    PushNotificationReceiver.instance?.FetchAndSaveToken();

                    Debug.Log("[Firebase] Ready. Fetching Stories, Landmarks, and Journeys.");

                    FetchData("Stories");
                    FetchData("Landmarks");
                    FetchJourneys();

                    RefreshMap();
                    StartCoroutine(RetryPendingPhotoUploads());
                });
            }
            else
            {
                Debug.LogError($"[Firebase] Could not resolve dependencies: {task.Result}");
            }
        });
    }

    void OnEnable()
    {
        if (LocationUpdateManager.instance != null)
            LocationUpdateManager.instance.OnLocationUpdate += BeginMapRefresh;
    }

    void OnDisable()
    {
        if (LocationUpdateManager.instance != null)
            LocationUpdateManager.instance.OnLocationUpdate -= BeginMapRefresh;
    }

    public void BeginMapRefresh()
    {
        if (MainMapUserCursorController.Instance != null && !MainMapUserCursorController.Instance.ShouldAllowAutomaticMapReload)
        {
            RefreshMap();
            return;
        }

        StartCoroutine(ConductMapRefresh());
    }

    IEnumerator ConductMapRefresh()
    {
        if (ObjectManager.instance?.writePostButton != null)
            ObjectManager.instance.writePostButton.SetActive(false);

        if (MapLoader.instance != null)
        {
            MapLoader.instance.Refresh();
            yield return new WaitUntil(() => MapLoader.instance.mapLoaded);
        }

        RefreshMap();
    }

    private void FetchData(string collectionName)
    {
        if (!firebaseReady)
        {
            Debug.LogWarning($"[Firebase] FetchData({collectionName}) skipped — Firebase not ready.");
            return;
        }

        Debug.Log($"[Firebase] Fetching {collectionName}...");

        db.Collection(collectionName).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"[Firebase] Failed to fetch {collectionName}: {task.Exception}");
                return;
            }

            List<Entry> targetList = (collectionName == "Stories") ? storiesList : landmarksList;
            targetList.Clear();

            foreach (DocumentSnapshot doc in task.Result.Documents)
            {
                try
                {
                    Entry entry = DocumentToEntry(doc);

                    if (collectionName == "Landmarks")
                    {
                        if (entry.Tags == null)
                            entry.Tags = new List<string>();

                        if (!entry.Tags.Exists(t => string.Equals(t?.Trim(), "landmark", StringComparison.OrdinalIgnoreCase)))
                            entry.Tags.Add("landmark");
                    }

                    targetList.Add(entry);

                    if (collectionName == "Landmarks" && IsWithinRange(entry.Latitude, entry.Longitude))
                        InstantiatePrefab(entry, collectionName);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Firebase] Error parsing doc {doc.Id}: {e.Message}");
                }
            }

            Debug.Log($"[Firebase] Finished fetching {collectionName}. Count={targetList.Count}");

            RebuildGridCells();

            if (collectionName == "Stories")
            {
                DeduplicateStories();
                LibraryManager.instance?.PopulateList();
                LibraryManager.instance?.PopulateLikedList();
                RefreshMap();
                RefreshWriteButton();

                _storiesReady = true;
                TryActivateJourney();
            }
            else
            {
                LibraryManager.instance?.PopulateList();

                _landmarksReady = true;
                TryActivateJourney();
            }
        });
    }

    private Entry DocumentToEntry(DocumentSnapshot doc)
    {
        Dictionary<string, object> data = doc.ToDictionary();

        var entry = new Entry
        {
            ID = doc.Id,
            User = GetString(data, "User"),
            UserName = GetString(data, "UserName"),
            Latitude = GetFloat(data, "Latitude"),
            Longitude = GetFloat(data, "Longitude"),
            Title = GetString(data, "Title"),
            Content = GetString(data, "Content"),
            Theme = GetString(data, "Theme"),
            Track = GetString(data, "Track"),
            Font = GetString(data, "Font"),
            Saves = GetInt(data, "Saves"),
            SavedByUserIds = GetTags(data, "SavedByUserIds"),
            LikesCount = GetInt(data, "LikesCount"),
            PhotoUrl = GetString(data, "PhotoUrl"),
            StickerID = GetInt(data, "StickerID"),
            FontID = GetInt(data, "FontID"),
            Tags = GetTags(data, "Tags"),
            Comments = GetComments(data, "Comments"),
            Views = GetInt(data, "Views"),
            Created = GetLong(data, "Created"),
            LastUpdated = GetLong(data, "LastUpdated"),
            Expire = GetLong(data, "Expire"),
        };

        if (ShouldMigrateLegacyUserId(entry))
        {
            entry.User = UserProfileManager.instance.UserId;

            if (string.IsNullOrWhiteSpace(entry.UserName) && UserProfileManager.instance.HasUsername)
                entry.UserName = UserProfileManager.instance.Username;

            entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            UpdateEntryInFirestore(entry);
        }

        if (string.IsNullOrWhiteSpace(entry.UserName))
        {
            if (LooksLikeFriendlyUsername(entry.User))
            {
                entry.UserName = entry.User;
            }
            else if (UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(entry.User))
            {
                entry.UserName = UserProfileManager.instance.Username;

                if (!string.IsNullOrWhiteSpace(entry.UserName))
                    UpdateEntryInFirestore(entry);
            }
        }

        return entry;
    }

    private bool ShouldMigrateLegacyUserId(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null)
            return false;

        string storedUser = entry.User?.Trim();
        if (string.IsNullOrWhiteSpace(storedUser))
            return false;

        string hashedUserId = UserProfileManager.instance.UserId?.Trim();
        if (string.IsNullOrWhiteSpace(hashedUserId))
            return false;

        if (storedUser.Equals(hashedUserId, StringComparison.OrdinalIgnoreCase))
            return false;

        string rawDeviceId = SystemInfo.deviceUniqueIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(rawDeviceId))
            return false;

        return storedUser.Equals(rawDeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFriendlyUsername(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();

        if (trimmed.Length < 3 || trimmed.Length > 40)
            return false;

        if (Regex.IsMatch(trimmed, "^[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}$"))
            return false;

        if (Regex.IsMatch(trimmed, "^[0-9A-Fa-f]{32}$"))
            return false;

        if (Regex.IsMatch(trimmed, "^[0-9A-Fa-f]+$") && trimmed.Length >= 16)
            return false;

        return Regex.IsMatch(trimmed, "[A-Za-z]");
    }

    public void SpawnNewMapPointer(Entry newEntry)
    {
        if (newEntry == null || string.IsNullOrEmpty(newEntry.Title) || newEntry.Latitude == 0f || newEntry.Longitude == 0f)
        {
            Debug.LogWarning("Invalid entry data. Cannot spawn new MapPointer.");
            return;
        }

        if (LibraryManager.instance != null && LocalStoryStore.LoadOwned().Count >= LibraryManager.instance.maxOwnedStories)
        {
            Debug.LogWarning("[Stories] Owned story limit reached. Cannot post new story.");
            return;
        }

        GameObject go = Instantiate(storyPrefab, mapParentTransform);
        MapPointer mapPointer = go.GetComponent<MapPointer>();
        GoogleSheetManager sheetManager = go.GetComponent<GoogleSheetManager>();

        if (mapPointer != null)
        {
            mapPointer.latitude = newEntry.Latitude;
            mapPointer.longitude = newEntry.Longitude;
            mapPointer.mapTransform = mapParentTransform;
            mapPointer.BindEntry(newEntry);
            mapPointer.UpdatePosition();
        }

        if (sheetManager != null)
        {
            sheetManager.id = newEntry.ID;
            sheetManager.latitude = newEntry.Latitude;
            sheetManager.longitude = newEntry.Longitude;
            sheetManager.userId = newEntry.User ?? string.Empty;
            sheetManager.title = newEntry.Title;
            sheetManager.content = newEntry.Content;
            sheetManager.theme = newEntry.Theme;
            sheetManager.photoUrl = newEntry.PhotoUrl ?? "";
            sheetManager.username = newEntry.UserName ?? string.Empty;
        }

        newEntry.pointer = mapPointer;
        instancedPointers.Add(mapPointer);
        storiesList.Add(newEntry);
        LocalStoryStore.SaveOwned(newEntry);
        mapPointer?.onPosted?.Invoke();

        string collection = IsLandmark(newEntry) ? "Landmarks" : "Stories";

        AddEntryToFirestore(newEntry, collection);
        LibraryManager.instance?.PopulateList();
        RefreshWriteButton();
    }

    private void AddEntryToFirestore(Entry entry, string collectionName)
    {
        if (!firebaseReady)
        {
            Debug.LogError("[Firebase] Not ready — cannot save entry.");
            return;
        }

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "User", entry.User ?? "" },
            { "UserName", entry.UserName ?? "" },
            { "Latitude", entry.Latitude },
            { "Longitude", entry.Longitude },
            { "Title", entry.Title ?? "" },
            { "Content", entry.Content ?? "" },
            { "Theme", entry.Theme ?? "" },
            { "Track", entry.Track ?? "" },
            { "Font", entry.Font ?? "" },
            { "FontID", entry.FontID },
            { "StickerID", entry.StickerID },
            { "Saves", entry.Saves },
            { "SavedByUserIds", entry.SavedByUserIds ?? new List<string>() },
            { "LikesCount", entry.LikesCount },
            { "Views", entry.Views },
            { "Created", entry.Created },
            { "LastUpdated", entry.Created },
            { "Expire", entry.Expire },
            { "PhotoUrl", entry.PhotoUrl ?? "" },
            { "Tags", entry.Tags ?? new List<string>() },
            { "Comments", SerializeComments(entry.Comments) },
        };

        db.Collection(collectionName).Document(entry.ID).SetAsync(data).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
                Debug.LogError($"[Firebase] Failed to save entry: {task.Exception}");
        });
    }

    public void DeleteEntryFromFirestore(Entry entry, string collectionName = "Stories")
    {
        if (!firebaseReady)
        {
            Debug.LogError("[Firebase] Not ready — cannot delete entry.");
            return;
        }

        db.Collection(collectionName).Document(entry.ID).DeleteAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
                Debug.LogError($"[Firebase] Failed to delete '{entry.Title}': {task.Exception}");
        });

        if (!string.IsNullOrEmpty(entry.PhotoUrl))
            DeletePhotoFromStorage(entry.ID);

        storiesList.Remove(entry);
        LocalStoryStore.RemoveOwned(entry.ID);

        if (entry.pointer != null)
        {
            instancedPointers.Remove(entry.pointer);
            Destroy(entry.pointer.gameObject);
            entry.pointer = null;
        }

        Vector2Int cell = GetGridCellIndex(entry.Latitude, entry.Longitude);

        if (gridCells.ContainsKey(cell))
            gridCells[cell].Remove(entry);
    }

    public void DeletePhotoFromStorage(string entryId)
    {
        string path = $"photos/{entryId}.jpg";

        FirebaseStorage.DefaultInstance.GetReference(path)
            .DeleteAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                    Debug.LogWarning($"[Photo] Failed to delete photo for {entryId}: {task.Exception?.InnerException?.Message}");
            });
    }

    private void DeduplicateStories()
    {
        const float latThreshold = 0.0001f;
        const float lonThreshold = 0.0001f;

        var toDelete = new List<Entry>();

        for (int i = 0; i < storiesList.Count; i++)
        {
            if (toDelete.Contains(storiesList[i]))
                continue;

            for (int j = i + 1; j < storiesList.Count; j++)
            {
                if (toDelete.Contains(storiesList[j]))
                    continue;

                var a = storiesList[i];
                var b = storiesList[j];

                bool similar = a.ID == b.ID ||
                    (a.User == b.User &&
                     Mathf.Abs(a.Latitude - b.Latitude) < latThreshold &&
                     Mathf.Abs(a.Longitude - b.Longitude) < lonThreshold);

                if (!similar)
                    continue;

                long aTime = a.LastUpdated > 0 ? a.LastUpdated : a.Created;
                long bTime = b.LastUpdated > 0 ? b.LastUpdated : b.Created;

                toDelete.Add(aTime >= bTime ? b : a);
            }
        }

        foreach (var e in toDelete)
            DeleteEntryFromFirestore(e);
    }

    public void DeleteEntriesByOwner(string ownerId, string ownerName)
    {
        if (!firebaseReady)
        {
            Debug.LogError("[Firebase] Not ready — cannot delete user entries.");
            return;
        }

        if (string.IsNullOrEmpty(ownerId) && string.IsNullOrEmpty(ownerName))
        {
            Debug.LogWarning("[Firebase] No owner ID or name specified for deletion.");
            return;
        }

        var entriesToDelete = new List<Entry>();

        foreach (var entry in storiesList)
        {
            if (entry == null)
                continue;

            if (!string.IsNullOrEmpty(ownerId) && entry.User == ownerId)
            {
                entriesToDelete.Add(entry);
                continue;
            }

            if (string.IsNullOrEmpty(ownerId) &&
                !string.IsNullOrEmpty(ownerName) &&
                (entry.User == ownerName || entry.UserName == ownerName))
            {
                entriesToDelete.Add(entry);
            }
        }

        foreach (var entry in entriesToDelete)
            DeleteEntryFromFirestore(entry);
    }

    public void UpdatePhotoUrl(string storyId, string photoUrl, string collectionName = "Stories")
    {
        if (!firebaseReady)
            return;

        db.Collection(collectionName).Document(storyId)
          .UpdateAsync(new Dictionary<string, object> { { "PhotoUrl", photoUrl } })
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted || task.IsCanceled)
                  Debug.LogError($"[Firebase] Failed to update PhotoUrl for {storyId}: {task.Exception}");
          });
    }

    private IEnumerator RetryPendingPhotoUploads()
    {
        var pending = PhotoUploadQueue.GetPendingIds();

        if (pending.Count == 0)
            yield break;

        Debug.Log($"[Photo] Retrying {pending.Count} pending upload(s).");

        var owned = LocalStoryStore.LoadOwned();

        foreach (string storyId in pending)
        {
            var stored = owned.Find(s => s.ID == storyId);

            if (stored == null)
            {
                PhotoUploadQueue.Dequeue(storyId);
                continue;
            }

            byte[] bytes = PhotoUploadQueue.GetBytes(storyId);

            if (bytes == null || bytes.Length == 0)
            {
                PhotoUploadQueue.Dequeue(storyId);
                continue;
            }

            var e = LocalStoryStore.ToLiveEntry(stored);
            string path = $"photos/{e.ID}.jpg";
            var storageRef = FirebaseStorage.DefaultInstance.GetReference(path);

            var uploadTask = storageRef.PutBytesAsync(bytes);
            yield return new WaitUntil(() => uploadTask.IsCompleted);

            if (uploadTask.IsFaulted || uploadTask.IsCanceled)
            {
                Debug.LogError($"[Photo] Retry failed for {storyId}: {uploadTask.Exception}");
                continue;
            }

            var urlTask = storageRef.GetDownloadUrlAsync();
            yield return new WaitUntil(() => urlTask.IsCompleted);

            if (!urlTask.IsFaulted && !urlTask.IsCanceled)
            {
                string photoUrl = urlTask.Result.ToString();

                UpdatePhotoUrl(storyId, photoUrl);
                e.PhotoUrl = photoUrl;
                LocalStoryStore.SaveOwned(e);
                PhotoUploadQueue.Dequeue(storyId);

                Debug.Log($"[Photo] Retry complete for {storyId}");
            }
        }
    }

    public void UpdateEntryInFirestore(Entry entry, string collectionName = "Stories")
    {
        if (!firebaseReady)
        {
            Debug.LogError("[Firebase] Not ready — cannot update entry.");
            return;
        }

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "User", entry.User ?? "" },
            { "UserName", entry.UserName ?? "" },
            { "Latitude", entry.Latitude },
            { "Longitude", entry.Longitude },
            { "Title", entry.Title ?? "" },
            { "Content", entry.Content ?? "" },
            { "Theme", entry.Theme ?? "" },
            { "Track", entry.Track ?? "" },
            { "Font", entry.Font ?? "" },
            { "FontID", entry.FontID },
            { "StickerID", entry.StickerID },
            { "Saves", entry.Saves },
            { "SavedByUserIds", entry.SavedByUserIds ?? new List<string>() },
            { "LikesCount", entry.LikesCount },
            { "Views", entry.Views },
            { "Created", entry.Created },
            { "LastUpdated", entry.LastUpdated },
            { "Expire", entry.Expire },
            { "PhotoUrl", entry.PhotoUrl ?? "" },
            { "Tags", entry.Tags ?? new List<string>() },
            { "Comments", SerializeComments(entry.Comments) },
        };

        db.Collection(collectionName).Document(entry.ID).SetAsync(data).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
                Debug.LogError($"[Firebase] Failed to update '{entry.Title}': {task.Exception}");
        });
    }

    public void RefreshMap()
    {
        if (_refreshCoroutine != null)
            StopCoroutine(_refreshCoroutine);

        _refreshCoroutine = StartCoroutine(Refresh());
    }

    private IEnumerator Refresh()
    {
        while (GPSManager.Instance.latitude == 0 || GPSManager.Instance.longitude == 0)
            yield return new WaitForSeconds(0.5f);

        List<Vector2Int> nearbyCells = GetNearbyGridCells(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
        var entriesInRange = new List<Entry>();

        foreach (MapPointer mp in instancedPointers)
        {
            if (mp != null)
                mp.UpdatePosition();
        }

        foreach (Vector2Int cell in nearbyCells)
        {
            if (!gridCells.ContainsKey(cell))
                continue;

            foreach (Entry entry in gridCells[cell])
            {
                if (IsWithinRange(entry.Latitude, entry.Longitude))
                    entriesInRange.Add(entry);
            }
        }

        var storyCandidates = new List<Entry>();

        foreach (Entry entry in entriesInRange)
        {
            if (IsLandmark(entry))
                continue;

            if (IsStoryVisibleOnMap(entry))
                storyCandidates.Add(entry);
        }

        List<Entry> visibleStories = ResolveVisibleStories(storyCandidates);

        foreach (Entry entry in entriesInRange)
        {
            if (IsLandmark(entry))
                UpdateEntry(entry, "Landmarks", true);
            else
                UpdateEntry(entry, "Stories", visibleStories.Contains(entry));
        }

        var activeJourney = JourneyManager.instance?.activeJourney;

        if (activeJourney?.Chapters != null)
        {
            foreach (var chapter in activeJourney.Chapters)
            {
                if (string.IsNullOrEmpty(chapter.StoryId))
                    continue;

                var story = storiesList.Find(e => e?.ID == chapter.StoryId)
                         ?? landmarksList.Find(e => e?.ID == chapter.StoryId);

                if (story == null)
                    continue;

                bool visible = JourneyManager.instance.IsJourneyStoryVisible(chapter.StoryId);

                if (journeyMapParentTransform != null &&
                    story.pointer != null &&
                    story.pointer.transform.parent != journeyMapParentTransform)
                {
                    story.pointer.transform.SetParent(journeyMapParentTransform, false);
                    story.pointer.mapTransform = journeyMapParentTransform;
                }

                if (visible)
                {
                    if (story.pointer == null)
                    {
                        InstantiatePrefab(story, IsLandmark(story) ? "Landmarks" : "Stories");
                    }
                    else
                    {
                        if (!story.pointer.gameObject.activeSelf)
                            story.pointer.gameObject.SetActive(true);

                        story.pointer.UpdatePosition();
                    }
                }
                else
                {
                    if (story.pointer != null && story.pointer.gameObject.activeSelf)
                        story.pointer.gameObject.SetActive(false);
                }
            }
        }

        RefreshWriteButton();
    }

    public void RefreshWriteButton()
    {
        if (ObjectManager.instance?.writePostButton == null)
            return;

        bool anyNearbyOwnedStory = false;

        foreach (var mp in instancedPointers)
        {
            if (mp == null || !mp.gameObject.activeSelf || !mp.IsWithinProximity() || mp.entry == null)
                continue;

            if (UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(mp.entry.User))
            {
                anyNearbyOwnedStory = true;
                break;
            }
        }

        ObjectManager.instance.writePostButton.SetActive(!anyNearbyOwnedStory);
    }

    public static bool IsLandmark(Entry entry)
    {
        if (entry == null)
            return false;

        bool hasTag = entry.Tags != null && entry.Tags.Exists(t =>
            string.Equals(t?.Trim(), "landmark", StringComparison.OrdinalIgnoreCase));

        if (hasTag)
            return true;

        if (instance == null || string.IsNullOrEmpty(entry.ID))
            return false;

        return instance.landmarksList.Exists(e => e != null && e.ID == entry.ID);
    }

    private bool IsStoryVisibleOnMap(Entry entry)
    {
        if (entry == null)
            return false;

        if (JourneyManager.instance != null)
        {
            bool isAnyJourneyChapter = journeysList?.Exists(j =>
                j?.Chapters?.Find(c => c.StoryId == entry.ID) != null) ?? false;

            if (isAnyJourneyChapter)
                return JourneyManager.instance.IsJourneyStoryVisible(entry.ID);
        }

        bool isOwned = UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(entry.User);

        if (isOwned)
            return true;

        if (IsSavedByCurrentUser(entry))
            return false;

        if (entry.Tags != null)
        {
            bool isPrivate = false;
            bool isFriendsOnly = false;

            foreach (string tag in entry.Tags)
            {
                if (string.Equals(tag?.Trim(), "private", StringComparison.OrdinalIgnoreCase))
                    isPrivate = true;

                if (string.Equals(tag?.Trim(), "friends_only", StringComparison.OrdinalIgnoreCase))
                    isFriendsOnly = true;
            }

            if (isPrivate)
                return false;

            if (isFriendsOnly && !FriendsManager.IsFriend(entry.User))
                return false;
        }

        var interests = UserProfileManager.instance?.SelectedInterests;

        if (interests == null || interests.Count == 0 || entry.Tags == null || entry.Tags.Count == 0)
            return false;

        var interestSet = new HashSet<string>(interests, StringComparer.OrdinalIgnoreCase);

        foreach (string tag in entry.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (interestSet.Contains(tag.Trim()))
                return true;
        }

        return false;
    }

    private List<Entry> ResolveVisibleStories(List<Entry> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return new List<Entry>();

        var winners = new List<Entry>();
        var visited = new HashSet<Entry>();

        for (int i = 0; i < candidates.Count; i++)
        {
            Entry root = candidates[i];

            if (visited.Contains(root))
                continue;

            var cluster = new List<Entry>();
            var queue = new Queue<Entry>();

            queue.Enqueue(root);
            visited.Add(root);

            while (queue.Count > 0)
            {
                Entry current = queue.Dequeue();
                cluster.Add(current);

                for (int j = 0; j < candidates.Count; j++)
                {
                    Entry other = candidates[j];

                    if (visited.Contains(other))
                        continue;

                    if (DistanceMeters(current.Latitude, current.Longitude, other.Latitude, other.Longitude) <= storyConflictDistanceMeters)
                    {
                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }

            winners.AddRange(ResolveCluster(cluster));
        }

        return EnforceMinimumSeparation(winners);
    }

    private List<Entry> EnforceMinimumSeparation(List<Entry> winners)
    {
        if (winners == null || winners.Count <= 1)
            return winners ?? new List<Entry>();

        var owned = new List<Entry>();
        var others = new List<Entry>();

        foreach (Entry entry in winners)
        {
            if (entry == null)
                continue;

            bool isOwned = UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(entry.User);

            if (isOwned)
                owned.Add(entry);
            else
                others.Add(entry);
        }

        others.Sort((a, b) =>
        {
            int scoreA = GetInterestMatchCount(a);
            int scoreB = GetInterestMatchCount(b);

            if (scoreA != scoreB)
                return scoreB.CompareTo(scoreA);

            return b.Created.CompareTo(a.Created);
        });

        var filtered = new List<Entry>(owned);

        foreach (Entry candidate in others)
        {
            bool tooClose = false;

            foreach (Entry kept in filtered)
            {
                if (DistanceMeters(candidate.Latitude, candidate.Longitude, kept.Latitude, kept.Longitude) <= storyConflictDistanceMeters)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                filtered.Add(candidate);
        }

        return filtered;
    }

    private List<Entry> ResolveCluster(List<Entry> cluster)
    {
        if (cluster == null || cluster.Count == 0)
            return new List<Entry>();

        var ownedEntries = new List<Entry>();

        foreach (Entry entry in cluster)
        {
            if (UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(entry.User))
                ownedEntries.Add(entry);
        }

        if (ownedEntries.Count > 0)
            return ownedEntries;

        Entry best = null;
        int bestScore = -1;

        foreach (Entry entry in cluster)
        {
            int score = GetInterestMatchCount(entry);

            if (best == null || score > bestScore || (score == bestScore && entry.Created > best.Created))
            {
                best = entry;
                bestScore = score;
            }
        }

        return best != null ? new List<Entry> { best } : new List<Entry>();
    }

    private int GetInterestMatchCount(Entry entry)
    {
        if (entry == null)
            return 0;

        var interests = UserProfileManager.instance?.SelectedInterests;

        if (interests == null || interests.Count == 0 || entry.Tags == null || entry.Tags.Count == 0)
            return 0;

        int count = 0;
        var interestSet = new HashSet<string>(interests, StringComparer.OrdinalIgnoreCase);

        foreach (string tag in entry.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (interestSet.Contains(tag.Trim()))
                count++;
        }

        return count;
    }

    private float DistanceMeters(float lat1, float lon1, float lat2, float lon2)
    {
        float latDiff = (lat1 - lat2) * 111320f;
        float lonDiff = (lon1 - lon2) * (111320f * Mathf.Cos(lat1 * Mathf.Deg2Rad));

        return Mathf.Sqrt(latDiff * latDiff + lonDiff * lonDiff);
    }

    public Entry GetStoryById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        string trimmed = id.Trim();

        return storiesList.Find(e => e != null && string.Equals(e.ID, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? landmarksList.Find(e => e != null && string.Equals(e.ID, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsSavedByCurrentUser(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null)
            return false;

        string userId = UserProfileManager.instance.UserId;

        if (string.IsNullOrWhiteSpace(userId) || entry.SavedByUserIds == null)
            return false;

        if (entry.SavedByUserIds.Exists(u => string.Equals(u?.Trim(), userId, StringComparison.OrdinalIgnoreCase)))
            return true;

        string rawDeviceId = SystemInfo.deviceUniqueIdentifier?.Trim();

        if (string.IsNullOrWhiteSpace(rawDeviceId))
            return false;

        return entry.SavedByUserIds.Exists(u => string.Equals(u?.Trim(), rawDeviceId, StringComparison.OrdinalIgnoreCase));
    }

    public bool ToggleSave(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null)
            return false;

        string userId = UserProfileManager.instance.UserId;

        if (string.IsNullOrWhiteSpace(userId))
            return false;

        if (entry.SavedByUserIds == null)
            entry.SavedByUserIds = new List<string>();

        bool isOwn = UserProfileManager.instance.IsCurrentUser(entry.User);

        int existingIndex = entry.SavedByUserIds.FindIndex(u => string.Equals(u?.Trim(), userId, StringComparison.OrdinalIgnoreCase));
        bool saved;

        if (existingIndex >= 0)
        {
            entry.SavedByUserIds.RemoveAt(existingIndex);
            entry.Saves = Mathf.Max(0, entry.Saves - 1);
            saved = false;
        }
        else
        {
            entry.SavedByUserIds.Add(userId);
            entry.Saves = Mathf.Max(0, entry.Saves + 1);
            saved = true;
            if (!isOwn) StoryLifetimeManager.instance?.RecordSave(entry);
        }

        entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UpdateEntryInFirestore(entry);

        if (entry.pointer != null)
            entry.pointer.RefreshLikeDisplay();

        return saved;
    }

    public void AddLike(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null) return;
        bool isOwn = UserProfileManager.instance.IsCurrentUser(entry.User);
        entry.LikesCount = Mathf.Max(0, entry.LikesCount + 1);
        entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!isOwn) StoryLifetimeManager.instance?.RecordLike(entry);
        UpdateEntryInFirestore(entry);
    }

    public void RemoveLike(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null) return;
        bool isOwn = UserProfileManager.instance.IsCurrentUser(entry.User);
        entry.LikesCount = Mathf.Max(0, entry.LikesCount - 1);
        entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!isOwn) StoryLifetimeManager.instance?.RecordUnlike(entry);
        UpdateEntryInFirestore(entry);
    }

    public bool AddComment(Entry entry, string commentText)
    {
        if (entry == null || UserProfileManager.instance == null)
            return false;

        string trimmed = string.IsNullOrWhiteSpace(commentText) ? string.Empty : commentText.Trim();

        if (trimmed.Length < 10)
            return false;

        if (entry.Comments == null)
            entry.Comments = new List<Entry.Comment>();

        string userId = UserProfileManager.instance.UserId;

        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var comment = new Entry.Comment
        {
            CommentId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            UserName = UserProfileManager.instance.HasUsername ? UserProfileManager.instance.Username : string.Empty,
            Text = trimmed,
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        entry.Comments.Add(comment);
        entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        StoryLifetimeManager.instance?.RecordComment(entry);
        UpdateEntryInFirestore(entry);
        PersistStoryLocally(entry);

        return true;
    }

    public bool DeleteComment(Entry entry, string commentId)
    {
        if (entry == null || string.IsNullOrWhiteSpace(commentId) || UserProfileManager.instance == null)
            return false;

        if (entry.Comments == null)
            return false;

        int idx = entry.Comments.FindIndex(c =>
            c != null &&
            string.Equals(c.CommentId, commentId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (idx < 0)
            return false;

        Entry.Comment existing = entry.Comments[idx];

        if (existing == null || !UserProfileManager.instance.IsCurrentUser(existing.UserId))
            return false;

        entry.Comments.RemoveAt(idx);
        entry.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        UpdateEntryInFirestore(entry);
        PersistStoryLocally(entry);

        return true;
    }

    private void PersistStoryLocally(Entry entry)
    {
        if (entry == null || UserProfileManager.instance == null)
            return;

        bool isOwned = UserProfileManager.instance.IsCurrentUser(entry.User);

        if (isOwned)
        {
            LocalStoryStore.SaveOwned(entry);
            return;
        }

        if (IsSavedByCurrentUser(entry))
            LocalStoryStore.SaveCollected(entry);
    }

    private List<Vector2Int> GetNearbyGridCells(float playerLat, float playerLon)
    {
        Vector2Int playerCell = GetGridCellIndex(playerLat, playerLon);
        List<Vector2Int> nearbyCells = new List<Vector2Int>();

        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                nearbyCells.Add(new Vector2Int(playerCell.x + dx, playerCell.y + dy));

        return nearbyCells;
    }

    private void RebuildGridCells()
    {
        gridCells.Clear();

        StoreEntriesInGridCells(storiesList);
        StoreEntriesInGridCells(landmarksList);
    }

    private void StoreEntriesInGridCells(List<Entry> entries)
    {
        foreach (Entry entry in entries)
        {
            Vector2Int gridCell = GetGridCellIndex(entry.Latitude, entry.Longitude);

            if (!gridCells.ContainsKey(gridCell))
                gridCells[gridCell] = new List<Entry>();

            gridCells[gridCell].Add(entry);
        }
    }

    private Vector2Int GetGridCellIndex(float latitude, float longitude)
    {
        int x = Mathf.FloorToInt(longitude / maxDistanceMeters);
        int y = Mathf.FloorToInt(latitude / maxDistanceMeters);

        return new Vector2Int(x, y);
    }

    private void UpdateEntry(Entry entry, string collectionName, bool shouldShow)
    {
        bool inRange = IsWithinRange(entry.Latitude, entry.Longitude);

        if (inRange && shouldShow)
        {
            if (entry.pointer == null)
            {
                InstantiatePrefab(entry, collectionName);
            }
            else if (!entry.pointer.gameObject.activeSelf)
            {
                entry.pointer.gameObject.SetActive(true);
                entry.pointer.UpdatePosition();
            }
            else
            {
                entry.pointer.UpdatePosition();
            }
        }
        else if (entry.pointer != null && entry.pointer.gameObject.activeSelf)
        {
            entry.pointer.gameObject.SetActive(false);
        }
    }

    private bool IsWithinRange(float entryLat, float entryLon)
    {
        if (GPSManager.Instance == null)
            return false;

        float playerLat = GPSManager.Instance.latitude;
        float playerLon = GPSManager.Instance.longitude;

        if (playerLat == 0f && playerLon == 0f)
            return false;

        float latDiffMeters = Mathf.Abs((entryLat - playerLat) * 111320f);
        float lonDiffMeters = Mathf.Abs((entryLon - playerLon) * (111320f * Mathf.Cos(playerLat * Mathf.Deg2Rad)));

        return latDiffMeters <= maxDistanceMeters && lonDiffMeters <= maxDistanceMeters;
    }

    private void InstantiatePrefab(Entry entry, string collectionName)
    {
        if (string.IsNullOrEmpty(entry.Title) || entry.Latitude == 0f || entry.Longitude == 0f)
        {
            Debug.LogWarning($"Skipping instantiation for {collectionName}: Invalid data");
            return;
        }

        if (JourneyManager.instance != null && !JourneyManager.instance.IsJourneyStoryVisible(entry.ID))
            return;

        bool isLandmarkEntry = collectionName == "Landmarks" || IsLandmark(entry);
        bool isJourneyChapter = journeyMapParentTransform != null &&
                                (journeysList?.Exists(j => j?.Chapters?.Find(c => c.StoryId == entry.ID) != null) ?? false);

        GameObject prefabToInstantiate = isLandmarkEntry ? landmarkPrefab : storyPrefab;

        RectTransform parent = isLandmarkEntry && landmarkMapParentTransform != null ? landmarkMapParentTransform
                             : isJourneyChapter ? journeyMapParentTransform
                             : mapParentTransform;

        GameObject go = Instantiate(prefabToInstantiate, parent);

        MapPointer mapPointer = go.GetComponent<MapPointer>();
        GoogleSheetManager sheetManager = go.GetComponent<GoogleSheetManager>();

        if (mapPointer != null)
        {
            mapPointer.latitude = entry.Latitude;
            mapPointer.longitude = entry.Longitude;
            mapPointer.mapTransform = isJourneyChapter && journeyMapParentTransform != null
                ? journeyMapParentTransform
                : mapParentTransform;

            mapPointer.BindEntry(entry);
            mapPointer.UpdatePosition();
        }

        if (sheetManager != null)
        {
            sheetManager.id = entry.ID;
            sheetManager.latitude = entry.Latitude;
            sheetManager.longitude = entry.Longitude;
            sheetManager.userId = entry.User ?? string.Empty;
            sheetManager.username = entry.UserName ?? string.Empty;
            sheetManager.title = entry.Title;
            sheetManager.content = entry.Content;
            sheetManager.theme = entry.Theme;
            sheetManager.photoUrl = entry.PhotoUrl ?? "";
        }

        entry.pointer = mapPointer;
        instancedPointers.Add(mapPointer);
    }

    private string GetString(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) ? v?.ToString() ?? "" : "";

    private List<object> SerializeComments(List<Entry.Comment> comments)
    {
        var list = new List<object>();

        if (comments == null)
            return list;

        foreach (Entry.Comment c in comments)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Text))
                continue;

            var map = new Dictionary<string, object>
            {
                { "CommentId", c.CommentId ?? string.Empty },
                { "UserId", c.UserId ?? string.Empty },
                { "UserName", c.UserName ?? string.Empty },
                { "Text", c.Text ?? string.Empty },
                { "Created", c.Created },
            };

            list.Add(map);
        }

        return list;
    }

    private List<Entry.Comment> GetComments(Dictionary<string, object> d, string key)
    {
        var result = new List<Entry.Comment>();

        if (!d.TryGetValue(key, out object raw) || raw == null)
            return result;

        if (!(raw is IEnumerable<object> items))
            return result;

        foreach (object item in items)
        {
            if (!(item is Dictionary<string, object> map))
                continue;

            var comment = new Entry.Comment
            {
                CommentId = map.TryGetValue("CommentId", out object idVal) ? idVal?.ToString() ?? string.Empty : string.Empty,
                UserId = map.TryGetValue("UserId", out object uidVal) ? uidVal?.ToString() ?? string.Empty : string.Empty,
                UserName = map.TryGetValue("UserName", out object nameVal) ? nameVal?.ToString() ?? string.Empty : string.Empty,
                Text = map.TryGetValue("Text", out object txtVal) ? txtVal?.ToString() ?? string.Empty : string.Empty,
                Created = map.TryGetValue("Created", out object createdVal) && long.TryParse(createdVal?.ToString(), out long createdLong)
                    ? createdLong
                    : 0L,
            };

            if (string.IsNullOrWhiteSpace(comment.CommentId))
                comment.CommentId = Guid.NewGuid().ToString("N");

            if (!string.IsNullOrWhiteSpace(comment.Text))
                result.Add(comment);
        }

        result.Sort((a, b) => a.Created.CompareTo(b.Created));
        return result;
    }

    private List<string> GetTags(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out object v) || v == null)
            return new List<string>();

        var result = new List<string>();

        if (v is IEnumerable<object> list)
        {
            foreach (object item in list)
            {
                if (item == null)
                    continue;

                string tag = item.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(tag) && !result.Contains(tag))
                    result.Add(tag);
            }

            return result;
        }

        string raw = v.ToString();

        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        foreach (string tag in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = tag.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed) && !result.Contains(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    private float GetFloat(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && float.TryParse(v?.ToString(), out float r) ? r : 0f;

    private int GetInt(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && int.TryParse(v?.ToString(), out int r) ? r : 0;

    private long GetLong(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out object v) && long.TryParse(v?.ToString(), out long r) ? r : 0L;

    // ── Journeys ──────────────────────────────────────────────────────────

    public void FetchJourneys()
    {
        if (!firebaseReady)
        {
            Debug.LogWarning("[Journeys] FetchJourneys skipped — Firebase not ready.");
            return;
        }

        Debug.Log("[Journeys] Fetching Journeys from Firestore...");

        db.Collection("Journeys").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError($"[Firebase] Failed to fetch Journeys: {task.Exception}");
                return;
            }

            journeysList.Clear();

            foreach (DocumentSnapshot doc in task.Result.Documents)
            {
                try
                {
                    JourneyEntry journey = DocumentToJourneyEntry(doc);

                    if (journey != null)
                        journeysList.Add(journey);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Firebase] Error parsing Journey doc {doc.Id}: {e.Message}");
                }
            }

            Debug.Log($"[Journeys] Fetched journeys from Firestore. Count={journeysList.Count}");

            _journeysReady = true;
            TryActivateJourney();
        });
    }

    private void TryActivateJourney()
    {
        Debug.Log(
            $"[Journeys] TryActivateJourney storiesReady={_storiesReady}, " +
            $"landmarksReady={_landmarksReady}, journeysReady={_journeysReady}, " +
            $"journeysCount={journeysList.Count}, gps={GPSManager.Instance?.latitude},{GPSManager.Instance?.longitude}"
        );

        if (!_storiesReady || !_landmarksReady || !_journeysReady)
            return;

        ResolveJourneyLocations();
        SortJourneysByDistance();

        JourneyLibraryPanel.instance?.PopulateJourneysList();
        JourneyManager.instance?.OnJourneysLoaded();

        RefreshMap();
    }

    private JourneyEntry DocumentToJourneyEntry(DocumentSnapshot doc)
    {
        var data = doc.ToDictionary();

        var journey = new JourneyEntry
        {
            ID = doc.Id,
            Title = GetString(data, "Title"),
            Description = GetString(data, "Description"),
            StickerID = GetInt(data, "StickerID"),
            MapStyleIndex = GetInt(data, "MapStyleIndex"),
            Tags = GetTags(data, "Tags"),
            Created = GetLong(data, "Created"),
        };

        if (data.TryGetValue("Chapters", out object chaptersRaw) && chaptersRaw is IEnumerable<object> chapList)
        {
            foreach (object chapObj in chapList)
            {
                if (!(chapObj is Dictionary<string, object> chapMap))
                    continue;

                int chapOrder = GetInt(chapMap, "Order");

                var chapter = new JourneyEntry.ChapterDef
                {
                    Id = GetString(chapMap, "Id"),
                    StoryId = GetString(chapMap, "StoryId"),
                    Order = chapOrder,
                    InteractionType = GetString(chapMap, "InteractionType"),
                    PrerequisiteChapterId = GetString(chapMap, "PrerequisiteChapterId"),
                };

                if (string.IsNullOrEmpty(chapter.Id))
                    chapter.Id = $"ch_{chapOrder}";

                if (chapMap.TryGetValue("UnlockCondition", out object condRaw) && condRaw is Dictionary<string, object> condMap)
                {
                    chapter.Condition = new JourneyEntry.UnlockCondition
                    {
                        Type = GetString(condMap, "Type"),
                        Latitude = GetFloat(condMap, "Latitude"),
                        Longitude = GetFloat(condMap, "Longitude"),
                        RadiusMetres = GetFloat(condMap, "RadiusMetres"),
                        HourFrom = GetInt(condMap, "HourFrom"),
                        HourTo = GetInt(condMap, "HourTo"),
                        DelayMinutes = GetInt(condMap, "DelayMinutes"),
                        MonthFrom = GetInt(condMap, "MonthFrom"),
                        MonthTo = GetInt(condMap, "MonthTo"),
                    };
                }

                journey.Chapters.Add(chapter);
            }

            journey.Chapters.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        return journey;
    }

    private void ResolveJourneyLocations()
    {
        foreach (var journey in journeysList)
        {
            if (journey == null || journey.Chapters == null || journey.Chapters.Count == 0)
                continue;

            var firstChapter = journey.Chapters.Find(c => c.Order == 0) ?? journey.Chapters[0];

            if (string.IsNullOrEmpty(firstChapter.StoryId))
            {
                Debug.LogWarning($"[Journeys] Journey '{journey.Title}' has no first chapter StoryId.");
                continue;
            }

            var story = storiesList.Find(e => e != null && e.ID == firstChapter.StoryId)
                     ?? landmarksList.Find(e => e != null && e.ID == firstChapter.StoryId);

            if (story != null)
            {
                journey.Latitude = story.Latitude;
                journey.Longitude = story.Longitude;

                Debug.Log($"[Journeys] Resolved location for '{journey.Title}' to {journey.Latitude},{journey.Longitude}");
            }
            else
            {
                Debug.LogWarning($"[Journeys] Could not resolve location for '{journey.Title}'. Missing StoryId={firstChapter.StoryId}");
            }
        }
    }

    private void SortJourneysByDistance()
    {
        if (GPSManager.Instance == null)
        {
            Debug.LogWarning("[Journeys] Cannot sort journeys — GPSManager is null.");
            return;
        }

        float playerLat = GPSManager.Instance.latitude;
        float playerLon = GPSManager.Instance.longitude;

        if (playerLat == 0f && playerLon == 0f)
        {
            Debug.LogWarning("[Journeys] GPS not ready yet. Skipping journey distance filtering/sorting for now.");
            return;
        }

        // IMPORTANT:
        // Do not remove journeys from journeysList here.
        // On device, GPS may be late or inaccurate during startup.
        // Removing from journeysList makes journeys disappear until the next Firebase fetch.

        journeysList.Sort((a, b) =>
        {
            float da = DistanceMeters(a.Latitude, a.Longitude, playerLat, playerLon);
            float db = DistanceMeters(b.Latitude, b.Longitude, playerLat, playerLon);

            return da.CompareTo(db);
        });

        Debug.Log($"[Journeys] Sorted journeys by distance. Count still={journeysList.Count}");
    }

    // ── View tracking ─────────────────────────────────────────────────────

    private static readonly HashSet<string> _viewedThisSession = new HashSet<string>();

    public void RecordStoryView(string storyId, string authorId)
    {
        if (!firebaseReady || db == null)
            return;

        if (string.IsNullOrEmpty(storyId) || string.IsNullOrEmpty(authorId))
            return;

        if (UserProfileManager.instance != null && UserProfileManager.instance.IsCurrentUser(authorId))
            return;

        if (!_viewedThisSession.Add(storyId))
            return;

        string readerId = UserProfileManager.instance?.UserId
                       ?? SystemInfo.deviceUniqueIdentifier;

        string docId = $"{storyId}_{readerId}";

        db.Collection("StoryReads").Document(docId)
          .SetAsync(new Dictionary<string, object>
          {
              { "storyId", storyId },
              { "authorId", authorId },
              { "readerId", readerId },
              { "readAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
          })
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted)
                  Debug.LogWarning($"[StoryRead] Failed to record view: {task.Exception}");
          });
    }
}