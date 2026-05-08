using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BoostButton : MonoBehaviour
{
    [Header("References — Button")]
    public Button button;
    public GameObject alreadyBoostedIcon; // inside the button — shown when this story was boosted today

    [Header("References — Outside Button")]
    public TextMeshProUGUI boostsRemainingText; // "3/3"
    public TextMeshProUGUI timerText;           // "23h 17m" or "Unavailable"

    GoogleSheetsFetcher.Entry _entry;
    Coroutine _timerCoroutine;

    // ── PlayerPrefs keys ──────────────────────────────────────────────────────

    static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    static string BoostedStoriesKey => $"BoostedStories_{Today}";

    public static bool HasBoostedStoryToday(string storyId)
    {
        string list = PlayerPrefs.GetString(BoostedStoriesKey, "");
        foreach (var id in list.Split(','))
            if (id == storyId) return true;
        return false;
    }

    public static void MarkStoryBoostedToday(string storyId)
    {
        string key  = BoostedStoriesKey;
        string list = PlayerPrefs.GetString(key, "");
        string updated = string.IsNullOrEmpty(list) ? storyId : list + "," + storyId;
        PlayerPrefs.SetString(key, updated);
        PlayerPrefs.Save();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void BindEntry(GoogleSheetsFetcher.Entry entry)
    {
        _entry = entry;
        RefreshState();
        RestartTimerCoroutine();
    }

    void OnEnable()
    {
        RefreshState();
        RestartTimerCoroutine();
    }

    void OnDisable()
    {
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public void RefreshState()
    {
        if (button == null) return;

        bool isOwn = _entry != null &&
                     UserProfileManager.instance != null &&
                     UserProfileManager.instance.IsCurrentUser(_entry.User);

        if (isOwn)
        {
            if (button != null) button.gameObject.SetActive(false);
            return;
        }

        int limit     = StoryLifetimeManager.instance != null
                        ? StoryLifetimeManager.instance.boostDailyLimit : 3;
        int remaining = StoryLifetimeManager.instance != null
                        ? StoryLifetimeManager.instance.BoostsRemainingToday() : 0;

        bool alreadyBoosted = _entry != null && HasBoostedStoryToday(_entry.ID);
        bool canBoost       = _entry != null && remaining > 0 && !alreadyBoosted;

        if (button != null) button.gameObject.SetActive(remaining > 0);
        button.interactable = canBoost;

        if (alreadyBoostedIcon != null)
            alreadyBoostedIcon.SetActive(_entry != null && alreadyBoosted);

        if (boostsRemainingText != null)
            boostsRemainingText.text = $"{remaining}/{limit}";

        if (timerText != null)
            timerText.text = (remaining > 0 && !alreadyBoosted) ? "" : TimeUntilMidnight();
    }

    // ── Timer coroutine — updates every 30 s ─────────────────────────────────

    void RestartTimerCoroutine()
    {
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _timerCoroutine = StartCoroutine(TimerLoop());
    }

    IEnumerator TimerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(30f);
            RefreshState();
        }
    }

    // ── Boost action ──────────────────────────────────────────────────────────

    public void OnBoostTapped()
    {
        if (_entry == null || StoryLifetimeManager.instance == null) return;

        bool applied = StoryLifetimeManager.instance.TryBoost(_entry);
        if (applied)
        {
            MarkStoryBoostedToday(_entry.ID);
            RefreshState();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string TimeUntilMidnight()
    {
        TimeSpan remaining = DateTime.Today.AddDays(1) - DateTime.Now;
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{remaining.Minutes}m";
    }
}
