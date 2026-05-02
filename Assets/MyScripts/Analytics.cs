// To activate real Firebase Analytics:
// 1. Import FirebaseAnalytics.unitypackage from the Firebase Unity SDK
// 2. Add FIREBASE_ANALYTICS to Project Settings → Player → Scripting Define Symbols
#if FIREBASE_ANALYTICS
using Firebase.Analytics;
#endif

using UnityEngine;

public static class Analytics
{
    public static void PromptOpened()     => Log("prompt_opened");
    public static void PromptUsed()       => Log("prompt_used");
    public static void StoryPosted()      => Log("story_posted");
    public static void StoryRead(string storyId)
    {
#if FIREBASE_ANALYTICS
        FirebaseAnalytics.LogEvent("story_read",
            new Parameter("story_id", storyId ?? "unknown"));
#else
        Debug.Log($"[Analytics] story_read  story_id={storyId}");
#endif
    }

    static void Log(string eventName)
    {
#if FIREBASE_ANALYTICS
        FirebaseAnalytics.LogEvent(eventName);
#else
        Debug.Log($"[Analytics] {eventName}");
#endif
    }
}
