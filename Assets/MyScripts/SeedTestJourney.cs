using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Auth;
using Firebase.Extensions;

// Attach to any GameObject, hit Play, check console for "[Seed] Done".
// Remove this component (or the GameObject) after seeding.
public class SeedTestJourney : MonoBehaviour
{
    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError("[Seed] Firebase unavailable: " + task.Result);
                return;
            }

            FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWithOnMainThread(authTask =>
            {
                if (authTask.IsFaulted || authTask.IsCanceled)
                {
                    Debug.LogError("[Seed] Auth failed: " + authTask.Exception);
                    return;
                }

                var db = FirebaseFirestore.DefaultInstance;
                SeedStories(db);
                SeedJourney(db);
            });
        });
    }

    void SeedStories(FirebaseFirestore db)
    {
        var stories = new[]
        {
            new {
                id      = "test_altrincham_ch1",
                title   = "The Market's Secrets",
                content = "Altrincham Market has traded here since 1319. Stand still long enough and you can almost hear the old vendors calling out their wares between the Victorian ironwork.",
                lat     = 53.3875f,
                lon     = -2.3551f
            },
            new {
                id      = "test_altrincham_ch2",
                title   = "Stamford Road",
                content = "This road has been the spine of Altrincham since the railway arrived in 1849. Every shopfront here has been something else — bakery, cobbler, bank, café — a palimpsest of the town's ambitions.",
                lat     = 53.3874f,
                lon     = -2.3531f
            },
            new {
                id      = "test_altrincham_ch3",
                title   = "The Last Train",
                content = "The interchange opened in 2016, merging three transport networks into one. But the old station building still stands nearby — if you know where to look.",
                lat     = 53.3881f,
                lon     = -2.3516f
            },
        };

        foreach (var s in stories)
        {
            var data = new Dictionary<string, object>
            {
                { "Title",          s.title },
                { "Content",        s.content },
                { "Latitude",       s.lat },
                { "Longitude",      s.lon },
                { "User",           "test" },
                { "UserName",       "Test Journey" },
                { "Tags",           new List<string> { "history", "fiction" } },
                { "Likes",          0 },
                { "Views",          0 },
                { "Created",        1746057600L },
                { "LastUpdated",    1746057600L },
                { "Expire",         0L },
                { "PhotoUrl",       "" },
                { "StickerID",      0 },
                { "FontID",         0 },
                { "LikedByUserIds", new List<string>() },
                { "Comments",       new List<object>() },
            };

            string id = s.id;
            db.Collection("Stories").Document(id).SetAsync(data).ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted) Debug.LogError($"[Seed] Failed to write story {id}: {t.Exception}");
                else             Debug.Log($"[Seed] Story written: {id}");
            });
        }
    }

    void SeedJourney(FirebaseFirestore db)
    {
        var chapters = new List<object>
        {
            new Dictionary<string, object> {
                { "Id",                    "ch_1" },
                { "StoryId",               "test_altrincham_ch1" },
                { "Order",                 0 },
                { "InteractionType",       "read" },
                { "PrerequisiteChapterId", "" },
            },
            new Dictionary<string, object> {
                { "Id",                    "ch_2" },
                { "StoryId",               "test_altrincham_ch2" },
                { "Order",                 1 },
                { "InteractionType",       "read" },
                { "PrerequisiteChapterId", "" },
            },
            new Dictionary<string, object> {
                { "Id",                    "ch_3" },
                { "StoryId",               "test_altrincham_ch3" },
                { "Order",                 2 },
                { "InteractionType",       "read" },
                { "PrerequisiteChapterId", "" },
            },
        };

        var journey = new Dictionary<string, object>
        {
            { "Title",         "Altrincham Town Trail" },
            { "Description",   "A short walk through the hidden history of Altrincham town centre." },
            { "StickerID",     1 },
            { "MapStyleIndex", 0 },
            { "Tags",          new List<string> { "history", "walking" } },
            { "Created",       1746057600L },
            { "Chapters",      chapters },
        };

        db.Collection("Journeys").Document("test_journey_altrincham").SetAsync(journey).ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted) Debug.LogError("[Seed] Failed to write journey: " + t.Exception);
            else             Debug.Log("[Seed] Journey written: test_journey_altrincham");
        });
    }
}
