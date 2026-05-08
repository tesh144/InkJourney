using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

public class StoryLifetimeManager : MonoBehaviour
{
    public static StoryLifetimeManager instance;

    [Header("Config defaults (overwritten by Firestore on load)")]
    public int initialLifetimeDays   = 365;
    public int commentExtensionHours = 24;
    public int saveExtensionHours    = 12;
    public int viewExtensionHours    = 2;
    public int likeExtensionHours    = 6;
    public int boostExtensionHours   = 48;
    public int boostDailyLimit       = 3;

    public bool ConfigLoaded { get; private set; }

    const string ConfigCollection = "Config";
    const string ConfigDoc        = "StoryLifetime";
    const string BoostDateKey     = "BoostDate";
    const string BoostCountKey    = "BoostCount";

    FirebaseFirestore db;

    void Awake() => instance = this;

    void Start() => StartCoroutine(LoadWhenReady());

    IEnumerator LoadWhenReady()
    {
        yield return new WaitUntil(() =>
            FirebaseFirestore.DefaultInstance != null &&
            UserProfileManager.instance != null &&
            !string.IsNullOrEmpty(UserProfileManager.instance.UserId));

        db = FirebaseFirestore.DefaultInstance;
        LoadConfig();
    }

    void LoadConfig()
    {
        db.Collection(ConfigCollection).Document(ConfigDoc)
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (!task.IsFaulted && !task.IsCanceled)
              {
                  var d = task.Result.ToDictionary();
                  if (d != null)
                  {
                      initialLifetimeDays   = GetInt(d, "InitialLifetimeDays",   initialLifetimeDays);
                      commentExtensionHours = GetInt(d, "CommentExtensionHours", commentExtensionHours);
                      saveExtensionHours    = GetInt(d, "SaveExtensionHours",    saveExtensionHours);
                      viewExtensionHours    = GetInt(d, "ViewExtensionHours",    viewExtensionHours);
                      likeExtensionHours    = GetInt(d, "LikeExtensionHours",    likeExtensionHours);
                      boostExtensionHours   = GetInt(d, "BoostExtensionHours",   boostExtensionHours);
                      boostDailyLimit       = GetInt(d, "BoostDailyLimit",       boostDailyLimit);
                  }
              }
              ConfigLoaded = true;
          });
    }

    public long GetInitialExpire()
        => DateTimeOffset.UtcNow.AddDays(initialLifetimeDays).ToUnixTimeSeconds();

    // ── Interaction recorders ─────────────────────────────────────────────────

    public void RecordView(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return;
        string key = $"StoryViewed_{entry.ID}";
        if (PlayerPrefs.GetInt(key, 0) != 0) return;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
        ExtendLifetime(entry, viewExtensionHours);
        InkManager.instance?.AddInk(5);
    }

    public void RecordSave(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return;
        string key = $"StorySaved_{entry.ID}";
        if (PlayerPrefs.GetInt(key, 0) != 0) return;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
        ExtendLifetime(entry, saveExtensionHours);
    }

    public void RecordLike(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return;
        ExtendLifetime(entry, likeExtensionHours);
    }

    public void RecordUnlike(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return;
        ExtendLifetime(entry, -likeExtensionHours);
    }

    public void RecordComment(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return;
        ExtendLifetime(entry, commentExtensionHours);
    }

    // ── Boost ─────────────────────────────────────────────────────────────────

    public int BoostsRemainingToday() => Mathf.Max(0, boostDailyLimit - GetBoostsUsedToday());

    public bool TryBoost(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null || IsOwnStory(entry)) return false;
        if (BoostButton.HasBoostedStoryToday(entry.ID)) return false;
        int used = GetBoostsUsedToday();
        if (used >= boostDailyLimit) return false;
        SetBoostsUsedToday(used + 1);
        ExtendLifetime(entry, boostExtensionHours);
        return true;
    }

    // ── Core extend ───────────────────────────────────────────────────────────

    public void ExtendLifetime(GoogleSheetsFetcher.Entry entry, int hours)
    {
        if (entry == null || db == null || hours == 0) return;

        long now           = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long currentExpire = entry.Expire > now ? entry.Expire : now;
        long newExpire     = Math.Max(now, currentExpire + (long)hours * 3600);
        entry.Expire       = newExpire;

        string col = GoogleSheetsFetcher.IsLandmark(entry) ? "Landmarks" : "Stories";
        db.Collection(col).Document(entry.ID)
          .UpdateAsync(new Dictionary<string, object> { { "Expire", newExpire } });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool IsOwnStory(GoogleSheetsFetcher.Entry entry)
        => UserProfileManager.instance != null &&
           UserProfileManager.instance.IsCurrentUser(entry.User);

    int GetBoostsUsedToday()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return PlayerPrefs.GetString(BoostDateKey, "") == today
            ? PlayerPrefs.GetInt(BoostCountKey, 0)
            : 0;
    }

    void SetBoostsUsedToday(int count)
    {
        PlayerPrefs.SetString(BoostDateKey, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        PlayerPrefs.SetInt(BoostCountKey, count);
        PlayerPrefs.Save();
    }

    static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        if (d.TryGetValue(key, out var v)) try { return Convert.ToInt32(v); } catch { }
        return fallback;
    }
}
