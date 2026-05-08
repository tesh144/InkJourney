using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class FriendsManager : MonoBehaviour
{
    public static FriendsManager instance;

    [Header("List")]
    [SerializeField] Transform listParent;
    [SerializeField] Transform suggestedListParent;
    [SerializeField] GameObject friendItemPrefab;
    [SerializeField] GameObject addFriendFeedback;

    [Header("Search & Header")]
    [SerializeField] TMP_InputField searchInput;
    [SerializeField] TextMeshProUGUI friendCountText;
    [SerializeField] string friendCountPrefix = "Friends: ";

    [Header("Panels")]
    [SerializeField] ConnectPanel connectPanel;
    [SerializeField] FriendLibraryPanel friendLibraryPanel;
    [SerializeField] UnfriendConfirmPanel unfriendConfirmPanel;

    [Header("Selected Friend Header")]
    [SerializeField] FriendListItem selectedFriendHeader;

    [Header("Buttons")]
    [SerializeField] UnityEngine.UI.Button addFriendButton;

    const string UserProfilesCollection   = "UserProfiles";
    const string FriendsSubcollection     = "friends";
    const string FriendRequestsCollection = "FriendRequests";
    const int    OnlineThresholdSeconds   = 180;

    FirebaseFirestore db;
    bool firebaseReady;
    readonly List<FriendListItem> spawnedItems = new List<FriendListItem>();
    readonly HashSet<string> acceptedFriendIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    FriendListItem selectedItem;
    ListenerRegistration _requestsListener;

    public static bool IsFriend(string userId)
    {
        if (instance == null || string.IsNullOrEmpty(userId)) return false;
        return instance.acceptedFriendIds.Contains(userId);
    }

    void Awake()
    {
        instance = this;

        if (addFriendButton != null)
            addFriendButton.onClick.AddListener(OpenConnectPanel);

        RefreshFriendCount(0);

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;
            db = FirebaseFirestore.DefaultInstance;
            firebaseReady = true;
            if (gameObject.activeInHierarchy)
            {
                LoadFriends();
                StartRequestsListener();
            }
        });
    }

    Coroutine _refreshCoroutine;
    Coroutine _loadCoroutine;
    Coroutine _diffCoroutine;

    void OnEnable()
    {
        if (searchInput != null)
        {
            searchInput.text = "";
            searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        if (selectedFriendHeader != null)
            selectedFriendHeader.gameObject.SetActive(false);

        RefreshFriendCount(0);
        LoadFriends();
        _refreshCoroutine = StartCoroutine(PeriodicRefresh());
    }

    void OnDisable()
    {
        if (searchInput != null)
            searchInput.onValueChanged.RemoveListener(OnSearchChanged);

        if (_refreshCoroutine != null)
        {
            StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = null;
        }

        if (_diffCoroutine != null)
        {
            StopCoroutine(_diffCoroutine);
            _diffCoroutine = null;
        }

        _requestsListener?.Stop();
        _requestsListener = null;
    }

    void OnDestroy()
    {
        _requestsListener?.Stop();
        _requestsListener = null;
    }

    IEnumerator PeriodicRefresh()
    {
        while (true)
        {
            yield return new WaitForSeconds(10f);
            RefreshFriends();
        }
    }

    void RefreshFriends()
    {
        if (!firebaseReady || db == null) return;
        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;
        if (_diffCoroutine != null) StopCoroutine(_diffCoroutine);
        _diffCoroutine = StartCoroutine(DiffFriendsCoroutine(myId));
    }

    IEnumerator DiffFriendsCoroutine(string myId)
    {
        var task = db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).GetSnapshotAsync();
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted || task.IsCanceled) { _diffCoroutine = null; yield break; }

        var firestoreIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var newDocs      = new Dictionary<string, Dictionary<string, object>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var doc in task.Result.Documents)
        {
            var d  = doc.ToDictionary();
            string fid = GetString(d, "userId");
            if (string.IsNullOrEmpty(fid)) continue;
            firestoreIds.Add(fid);
            if (!spawnedItems.Exists(i => i != null && !i.IsRequest &&
                    string.Equals(i.Data?.userId, fid, System.StringComparison.OrdinalIgnoreCase)))
                newDocs[fid] = d;
        }

        // Remove friends no longer in Firestore
        var toRemove = spawnedItems.FindAll(i => i != null && !i.IsRequest &&
            !firestoreIds.Contains(i.Data?.userId ?? ""));
        foreach (var item in toRemove)
        {
            acceptedFriendIds.Remove(item.Data.userId);
            spawnedItems.Remove(item);
            Destroy(item.gameObject);
        }

        // Spawn new friends
        foreach (var kvp in newDocs)
        {
            var d = kvp.Value;
            SpawnItem(new FriendData
            {
                userId       = kvp.Key,
                username     = GetString(d, "username"),
                profilePicId = GetInt(d, "profilePicId"),
                status       = FriendData.FriendStatus.Accepted,
            });
            acceptedFriendIds.Add(kvp.Key);
        }

        if (toRemove.Count > 0 || newDocs.Count > 0)
        {
            SortAcceptedItems();
            RefreshFriendCount(acceptedFriendIds.Count);
        }

        _diffCoroutine = null;
    }

    // -------------------------------------------------------------------------
    // Loading
    // -------------------------------------------------------------------------

    public void LoadFriends()
    {
        if (_loadCoroutine != null) { StopCoroutine(_loadCoroutine); _loadCoroutine = null; }
        if (_diffCoroutine  != null) { StopCoroutine(_diffCoroutine);  _diffCoroutine  = null; }
        ClearList();
        if (!firebaseReady || db == null) return;
        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        _loadCoroutine = StartCoroutine(LoadFriendsCoroutine(myId));
    }

    IEnumerator LoadFriendsCoroutine(string myId)
    {
        // --- Accepted friends (fast path: use cached subcollection data) ---
        var friendsTask = db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).GetSnapshotAsync();

        yield return new WaitUntil(() => friendsTask.IsCompleted);

        if (friendsTask.IsFaulted || friendsTask.IsCanceled)
        {
            Debug.LogWarning($"[FriendsManager] Friends query failed: {friendsTask.Exception}");
            yield break;
        }

        var loaded = new List<FriendData>();

        foreach (var doc in friendsTask.Result.Documents)
        {
            var d = doc.ToDictionary();
            string friendId = GetString(d, "userId");
            if (string.IsNullOrEmpty(friendId)) continue;
            loaded.Add(new FriendData
            {
                userId       = friendId,
                username     = GetString(d, "username"),
                profilePicId = GetInt(d, "profilePicId"),
                status       = FriendData.FriendStatus.Accepted,
            });
        }

        acceptedFriendIds.Clear();
        foreach (var f in loaded) acceptedFriendIds.Add(f.userId);

        loaded.Sort((a, b) => string.Compare(a.username, b.username, System.StringComparison.OrdinalIgnoreCase));
        foreach (var f in loaded) SpawnItem(f);
        RefreshFriendCount(loaded.Count);

        // Background: refresh online status + sync usernames without blocking the list
        StartCoroutine(RefreshProfilesInBackground(myId, loaded));

        // --- Pending incoming requests ---
        var requestsTask = db.Collection(FriendRequestsCollection)
            .WhereEqualTo("toUserId", myId)
            .GetSnapshotAsync();

        yield return new WaitUntil(() => requestsTask.IsCompleted);

        if (!requestsTask.IsFaulted && !requestsTask.IsCanceled)
        {
            foreach (var doc in requestsTask.Result.Documents)
            {
                var d = doc.ToDictionary();
                if (GetString(d, "status") != "pending") continue;
                string fromId = GetString(d, "fromUserId");
                if (string.IsNullOrEmpty(fromId) || acceptedFriendIds.Contains(fromId)) continue;
                SpawnItem(new FriendData
                {
                    userId       = fromId,
                    username     = GetString(d, "fromUsername"),
                    profilePicId = GetInt(d, "fromProfilePicId"),
                    status       = FriendData.FriendStatus.PendingIncoming,
                    requestId    = doc.Id,
                });
            }
        }

        _loadCoroutine = null;
        StartRequestsListener();
    }

    IEnumerator RefreshProfilesInBackground(string myId, List<FriendData> friends)
    {
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var friend in friends)
        {
            var task = db.Collection(UserProfilesCollection).Document(friend.userId).GetSnapshotAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.IsFaulted || task.IsCanceled || !task.Result.Exists) continue;

            var profile = task.Result.ToDictionary();
            bool isOnline = (now - GetLong(profile, "lastSeen")) < OnlineThresholdSeconds;
            string freshName = GetString(profile, "UserName");

            var item = spawnedItems.Find(i => i != null && !i.IsRequest &&
                string.Equals(i.Data?.userId, friend.userId, System.StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;

            if (!string.IsNullOrEmpty(freshName) && freshName != friend.username)
                UpdateCachedUsername(myId, friend.userId, freshName);

            item.RefreshOnlineStatus(isOnline);
        }
    }

    void StartRequestsListener()
    {
        _requestsListener?.Stop();
        if (!firebaseReady || db == null) return;

        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        _requestsListener = db.Collection(FriendRequestsCollection)
            .WhereEqualTo("toUserId", myId)
            .Listen(snapshot =>
            {
                // Remove all currently spawned pending-request items
                var toRemove = spawnedItems.FindAll(i => i != null && i.IsRequest);
                foreach (var item in toRemove)
                {
                    spawnedItems.Remove(item);
                    Destroy(item.gameObject);
                }

                foreach (var doc in snapshot.Documents)
                {
                    var d = doc.ToDictionary();
                    if (GetString(d, "status") != "pending") continue;

                    string fromId = GetString(d, "fromUserId");

                    if (acceptedFriendIds.Contains(fromId))
                    {
                        doc.Reference.DeleteAsync();
                        continue;
                    }

                    SpawnItem(new FriendData
                    {
                        userId       = fromId,
                        username     = GetString(d, "fromUsername"),
                        profilePicId = GetInt(d, "fromProfilePicId"),
                        status       = FriendData.FriendStatus.PendingIncoming,
                        requestId    = doc.Id,
                    });
                }

                FriendRequestBadge.instance?.Refresh();
            });
    }

    void SpawnItem(FriendData data)
    {
        if (listParent == null || friendItemPrefab == null) return;

        var go   = Object.Instantiate(friendItemPrefab, listParent);
        go.SetActive(true);
        var item = go.GetComponent<FriendListItem>();
        if (item == null) return;

        item.SetFriendLibraryPanel(friendLibraryPanel);
        item.SetUnfriendConfirmPanel(unfriendConfirmPanel);
        item.Bind(data, this);
        spawnedItems.Add(item);

        if (data.status == FriendData.FriendStatus.PendingIncoming)
            go.transform.SetAsLastSibling();

        ApplyFilter(item, searchInput != null ? searchInput.text : "");
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    void OnSearchChanged(string query)
    {
        foreach (var item in spawnedItems)
            ApplyFilter(item, query);
    }

    void ApplyFilter(FriendListItem item, string query)
    {
        if (item == null) return;

        if (string.IsNullOrEmpty(query) || query.Equals("Search", System.StringComparison.OrdinalIgnoreCase))
        {
            item.gameObject.SetActive(true);
            return;
        }

        if (item.IsRequest)
            item.gameObject.SetActive(false);
        else
            item.gameObject.SetActive(item.Username.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // -------------------------------------------------------------------------
    // Actions (called by FriendListItem)
    // -------------------------------------------------------------------------

    public void AcceptRequest(FriendData data)
    {
        if (!firebaseReady || db == null || data == null) return;

        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        long now       = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string myName  = UserProfileManager.instance.DisplayName;
        int    myPicId = UserProfileManager.instance.ProfilePicID;

        db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).Document(data.userId)
            .SetAsync(new Dictionary<string, object>
            {
                { "userId", data.userId }, { "username", data.username },
                { "profilePicId", data.profilePicId }, { "addedAt", now }
            });

        db.Collection(UserProfilesCollection).Document(data.userId)
            .Collection(FriendsSubcollection).Document(myId)
            .SetAsync(new Dictionary<string, object>
            {
                { "userId", myId }, { "username", myName },
                { "profilePicId", myPicId }, { "addedAt", now }
            });

        if (!string.IsNullOrEmpty(data.requestId))
            db.Collection(FriendRequestsCollection).Document(data.requestId).DeleteAsync();

        // Immediately show as accepted — listener removes the pending item when the request doc is deleted.
        acceptedFriendIds.Add(data.userId);
        SpawnItem(new FriendData
        {
            userId       = data.userId,
            username     = data.username,
            profilePicId = data.profilePicId,
            status       = FriendData.FriendStatus.Accepted,
        });
        SortAcceptedItems();
        RefreshFriendCount(acceptedFriendIds.Count);
        FriendRequestBadge.instance?.Refresh();
    }

    public void RejectRequest(FriendData data)
    {
        if (!firebaseReady || db == null || data == null) return;

        if (!string.IsNullOrEmpty(data.requestId))
            db.Collection(FriendRequestsCollection).Document(data.requestId).DeleteAsync();

        LoadFriends();
        FriendRequestBadge.instance?.Refresh();
    }

    public void Unfriend(FriendData data)
    {
        if (!firebaseReady || db == null || data == null) return;

        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).Document(data.userId).DeleteAsync();

        db.Collection(UserProfilesCollection).Document(data.userId)
            .Collection(FriendsSubcollection).Document(myId).DeleteAsync();

        LoadFriends();
    }

    public void OpenConnectPanel()
    {
        if (connectPanel != null) connectPanel.gameObject.SetActive(true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public void SelectFriend(FriendListItem item)
    {
        if (selectedItem != null) selectedItem.SetSelected(false);
        selectedItem = item;
        if (selectedItem != null) selectedItem.SetSelected(true);

        if (selectedFriendHeader != null && item != null)
        {
            selectedFriendHeader.gameObject.SetActive(true);
            selectedFriendHeader.Bind(item.Data, this);
        }
    }

    void SortAcceptedItems()
    {
        var accepted = new List<FriendListItem>();
        foreach (var item in spawnedItems)
            if (item != null && !item.IsRequest) accepted.Add(item);

        accepted.Sort((a, b) => string.Compare(a.Username, b.Username, System.StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < accepted.Count; i++)
            accepted[i].transform.SetSiblingIndex(i);
    }

    void ClearList()
    {
        foreach (var item in spawnedItems)
            if (item != null) Destroy(item.gameObject);
        spawnedItems.Clear();
        selectedItem = null;

        if (selectedFriendHeader != null)
            selectedFriendHeader.gameObject.SetActive(false);
    }

    void RefreshFriendCount(int acceptedCount)
    {
        if (friendCountText != null)
            friendCountText.text = $"{friendCountPrefix}<color=white>x{acceptedCount}</color>";
    }

    void RemoveStaleAcceptedFriend(string myId, string staleId)
    {
        db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).Document(staleId).DeleteAsync();
    }

    void UpdateCachedUsername(string myId, string friendId, string newUsername)
    {
        db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).Document(friendId)
            .UpdateAsync(new Dictionary<string, object> { { "username", newUsername } });
    }

    static string GetString(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

    static int GetInt(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0;
        if (v is long l) return (int)l;
        if (v is int  i) return i;
        int.TryParse(v.ToString(), out int r);
        return r;
    }

    static long GetLong(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0;
        if (v is long l) return l;
        long.TryParse(v.ToString(), out long r);
        return r;
    }

    // ── Suggested friends ─────────────────────────────────────────────────

    const string PendingSuggestionsKey   = "SuggestedFriends.Pending";
    const string DismissedSuggestionsKey = "SuggestedFriends.Dismissed";

    static HashSet<string> LoadIdSet(string key)
    {
        var raw = PlayerPrefs.GetString(key, "");
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var id in raw.Split(','))
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        return set;
    }

    static void SaveIdSet(string key, HashSet<string> set)
    {
        PlayerPrefs.SetString(key, string.Join(",", set));
        PlayerPrefs.Save();
    }

    public void FetchSuggestedFriends()
    {
        if (!firebaseReady || db == null) return;
        if (suggestedListParent == null || friendItemPrefab == null) return;

        for (int i = suggestedListParent.childCount - 1; i >= 0; i--)
            Destroy(suggestedListParent.GetChild(i).gameObject);

        var pending   = LoadIdSet(PendingSuggestionsKey);
        var dismissed = LoadIdSet(DismissedSuggestionsKey);

        db.Collection(UserProfilesCollection)
            .WhereEqualTo("isFakeUser", true)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled) return;

                foreach (var doc in task.Result.Documents)
                {
                    string uid = doc.Id;
                    if (uid == UserProfileManager.instance?.UserId) continue;
                    if (acceptedFriendIds.Contains(uid)) continue;
                    if (pending.Contains(uid))   continue;
                    if (dismissed.Contains(uid)) continue;

                    var d = doc.ToDictionary();
                    var data = new FriendData
                    {
                        userId       = uid,
                        username     = GetString(d, "UserName"),
                        profilePicId = GetInt(d, "profilePicID"),
                        isFakeUser   = true,
                        status       = FriendData.FriendStatus.Suggested
                    };

                    var go   = Object.Instantiate(friendItemPrefab, suggestedListParent);
                    go.SetActive(true);
                    var item = go.GetComponent<FriendListItem>();
                    item?.Bind(data, this);
                }
            });
    }

    public void SendAddFriendRequest(FriendData data)
    {
        if (!firebaseReady || db == null || data == null) return;

        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        // Remove item from suggested list immediately
        if (suggestedListParent != null)
        {
            for (int i = suggestedListParent.childCount - 1; i >= 0; i--)
            {
                var item = suggestedListParent.GetChild(i).GetComponent<FriendListItem>();
                if (item != null && item.Data?.userId == data.userId)
                {
                    Destroy(item.gameObject);
                    break;
                }
            }
        }

        // Track as pending so it's filtered on next open
        var pending = LoadIdSet(PendingSuggestionsKey);
        pending.Add(data.userId);
        SaveIdSet(PendingSuggestionsKey, pending);

        // Show feedback
        if (addFriendFeedback != null)
        {
            addFriendFeedback.SetActive(true);
            StartCoroutine(HideFeedbackAfterDelay());
        }

        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var request = new Dictionary<string, object>
        {
            { "fromUserId",       myId },
            { "toUserId",         data.userId },
            { "fromUsername",     UserProfileManager.instance.DisplayName },
            { "fromProfilePicId", UserProfileManager.instance.ProfilePicID },
            { "toUsername",       data.username },
            { "toProfilePicId",   data.profilePicId },
            { "status",           "pending" },
            { "createdAt",        now }
        };

        db.Collection(FriendRequestsCollection).AddAsync(request).ContinueWithOnMainThread(addTask =>
        {
            if (addTask.IsFaulted || addTask.IsCanceled) return;
            if (data.isFakeUser)
                StartCoroutine(AutoAcceptCoroutine(addTask.Result.Id, data));
        });
    }

    // Call this if a real user ever rejects our outgoing request.
    public void DismissSuggestion(string userId)
    {
        var pending   = LoadIdSet(PendingSuggestionsKey);
        var dismissed = LoadIdSet(DismissedSuggestionsKey);
        pending.Remove(userId);
        dismissed.Add(userId);
        SaveIdSet(PendingSuggestionsKey, pending);
        SaveIdSet(DismissedSuggestionsKey, dismissed);
    }

    IEnumerator HideFeedbackAfterDelay()
    {
        yield return new WaitForSeconds(3f);
        if (addFriendFeedback != null) addFriendFeedback.SetActive(false);
    }

    IEnumerator AutoAcceptCoroutine(string requestId, FriendData fakeUser)
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(30f, 120f));

        if (!firebaseReady || db == null) yield break;

        string myId    = UserProfileManager.instance?.UserId;
        string myName  = UserProfileManager.instance?.DisplayName;
        int    myPicId = UserProfileManager.instance?.ProfilePicID ?? 0;
        if (string.IsNullOrEmpty(myId)) yield break;

        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).Document(fakeUser.userId)
            .SetAsync(new Dictionary<string, object>
            {
                { "userId", fakeUser.userId }, { "username", fakeUser.username },
                { "profilePicId", fakeUser.profilePicId }, { "addedAt", now }
            });

        db.Collection(UserProfilesCollection).Document(fakeUser.userId)
            .Collection(FriendsSubcollection).Document(myId)
            .SetAsync(new Dictionary<string, object>
            {
                { "userId", myId }, { "username", myName },
                { "profilePicId", myPicId }, { "addedAt", now }
            });

        db.Collection(FriendRequestsCollection).Document(requestId).DeleteAsync();

        // Clear from pending now that it's accepted
        var pending = LoadIdSet(PendingSuggestionsKey);
        pending.Remove(fakeUser.userId);
        SaveIdSet(PendingSuggestionsKey, pending);

        acceptedFriendIds.Add(fakeUser.userId);
        SpawnItem(new FriendData
        {
            userId       = fakeUser.userId,
            username     = fakeUser.username,
            profilePicId = fakeUser.profilePicId,
            isFakeUser   = true,
            status       = FriendData.FriendStatus.Accepted,
        });
        SortAcceptedItems();
        RefreshFriendCount(acceptedFriendIds.Count);
        FriendRequestBadge.instance?.Refresh();
    }
}
