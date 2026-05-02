using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

/// <summary>
/// Fetches accepted friends' last known locations from Firestore and spawns
/// FriendMapPointer instances on the map. Refreshes every refreshIntervalSeconds.
/// </summary>
public class FriendLocationManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] RectTransform pinParent;       // new RectTransform in the map hierarchy
    [SerializeField] GameObject friendPinPrefab;    // prefab with FriendMapPointer component

    [Header("Settings")]
    [SerializeField] float refreshIntervalSeconds = 10f;
    [SerializeField] float expireAfterSeconds     = 3600f; // pins older than this are skipped

    const string UserProfilesCollection = "UserProfiles";
    const string FriendsSubcollection   = "friends";

    FirebaseFirestore db;
    bool firebaseReady;
    readonly Dictionary<string, FriendMapPointer> spawnedPins = new Dictionary<string, FriendMapPointer>();

    void Awake()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;
            db = FirebaseFirestore.DefaultInstance;
            firebaseReady = true;
        });
    }

    void OnEnable()
    {
        StartCoroutine(RefreshLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return new WaitUntil(() => firebaseReady && db != null);
            yield return StartCoroutine(FetchAndSpawnPins());
            yield return new WaitForSeconds(refreshIntervalSeconds);
        }
    }

    IEnumerator FetchAndSpawnPins()
    {
        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) yield break;

        // Fetch accepted friends list
        var friendsTask = db.Collection(UserProfilesCollection).Document(myId)
            .Collection(FriendsSubcollection).GetSnapshotAsync();

        yield return new WaitUntil(() => friendsTask.IsCompleted);
        if (friendsTask.IsFaulted || friendsTask.IsCanceled) yield break;

        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var activeFriendIds = new HashSet<string>();

        foreach (var doc in friendsTask.Result.Documents)
        {
            string friendId = doc.GetValue<string>("userId");
            if (string.IsNullOrEmpty(friendId)) continue;

            activeFriendIds.Add(friendId);

            // Fetch their profile for lastLocation
            var profileTask = db.Collection(UserProfilesCollection).Document(friendId).GetSnapshotAsync();
            yield return new WaitUntil(() => profileTask.IsCompleted);
            if (profileTask.IsFaulted || profileTask.IsCanceled || !profileTask.Result.Exists) continue;

            var profile = profileTask.Result.ToDictionary();
            if (!profile.TryGetValue("lastLocation", out var locObj) || locObj == null) continue;

            var loc = locObj as Dictionary<string, object>;
            if (loc == null) continue;

            float lat = GetFloat(loc, "lat");
            float lon = GetFloat(loc, "lon");
            long  ts  = GetLong(loc, "timestamp");

            // Skip expired pins
            if (now - ts > (long)expireAfterSeconds)
            {
                RemovePin(friendId);
                continue;
            }

            if (spawnedPins.TryGetValue(friendId, out var existing) && existing != null)
            {
                // Update existing pin's time — it refreshes itself via coroutine
                continue;
            }

            SpawnPin(friendId, lat, lon, ts);
        }

        // Remove pins for friends no longer in the list
        var toRemove = new List<string>();
        foreach (var key in spawnedPins.Keys)
            if (!activeFriendIds.Contains(key)) toRemove.Add(key);
        foreach (var key in toRemove) RemovePin(key);
    }

    void SpawnPin(string friendId, float lat, float lon, long timestamp)
    {
        if (pinParent == null || friendPinPrefab == null) return;

        var go  = Instantiate(friendPinPrefab, pinParent);
        go.SetActive(true);
        var pin = go.GetComponent<FriendMapPointer>();
        if (pin == null) { Destroy(go); return; }

        pin.Bind(friendId, lat, lon, timestamp);
        spawnedPins[friendId] = pin;
    }

    void RemovePin(string friendId)
    {
        if (spawnedPins.TryGetValue(friendId, out var pin))
        {
            if (pin != null) Destroy(pin.gameObject);
            spawnedPins.Remove(friendId);
        }
    }

    static float GetFloat(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0f;
        if (v is double dbl) return (float)dbl;
        if (v is float  f)   return f;
        float.TryParse(v.ToString(), out float r);
        return r;
    }

    static long GetLong(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0;
        if (v is long l) return l;
        long.TryParse(v.ToString(), out long r);
        return r;
    }
}
