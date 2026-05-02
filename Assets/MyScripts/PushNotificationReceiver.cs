// To enable push notifications:
// 1. Import FirebaseMessaging-13.9.0.unitypackage from the Firebase Unity SDK
// 2. Add FIREBASE_MESSAGING to Project Settings → Player → Scripting Define Symbols
// 3. Add this component to a persistent GameObject in the scene
#if FIREBASE_MESSAGING
using Firebase.Messaging;
#endif
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class PushNotificationReceiver : MonoBehaviour
{
    public static PushNotificationReceiver instance;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;

#if FIREBASE_MESSAGING
            FirebaseMessaging.TokenReceived   += OnTokenReceived;
            FirebaseMessaging.MessageReceived += OnMessageReceived;
            FirebaseMessaging.RequestPermissionAsync();

            // Fetch and save the current token immediately — OnTokenReceived only
            // fires when the token is new/refreshed, so existing tokens are missed.
            // Token fetch is deferred — called by GoogleSheetsFetcher after anonymous auth completes
            // to guarantee the Firestore write has auth context.
#endif
        });
    }

    private void OnDestroy()
    {
#if FIREBASE_MESSAGING
        FirebaseMessaging.TokenReceived   -= OnTokenReceived;
        FirebaseMessaging.MessageReceived -= OnMessageReceived;
#endif
    }

    public void FetchAndSaveToken()
    {
#if FIREBASE_MESSAGING
        FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(tokenTask =>
        {
            if (!tokenTask.IsFaulted && !tokenTask.IsCanceled && !string.IsNullOrEmpty(tokenTask.Result))
                SaveTokenToFirestore(tokenTask.Result);
        });
#endif
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) return;
        ResetNotificationSession();
    }

#if FIREBASE_MESSAGING
    private void OnTokenReceived(object sender, TokenReceivedEventArgs e)
    {
        SaveTokenToFirestore(e.Token);
    }

    private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        if (!e.Message.NotificationOpened) return;

        string type = null;
        e.Message.Data?.TryGetValue("type", out type);

        if (type == "story_read")
        {
            StartupManager.PendingScenario = "from_read_notification";
            StartupManager.instance?.ApplyPending();
        }
        else if (type == "story_nearby")
        {
            StartupManager.PendingScenario = "story_nearby";
            StartupManager.instance?.ApplyPending();
        }
        else if (type == "friend_posted")
        {
            StartupManager.PendingScenario = "friend_posted";
            StartupManager.instance?.ApplyPending();
        }
        else if (type == "friend_request")
        {
            StartupManager.PendingScenario = "friend_request";
            StartupManager.instance?.ApplyPending();
            FriendRequestBadge.instance?.Refresh();
        }
        else if (type == "friend_accepted")
        {
            StartupManager.PendingScenario = "friend_accepted";
            StartupManager.instance?.ApplyPending();
        }
    }
#endif

    private void SaveTokenToFirestore(string token)
    {
        string userId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return;

        FirebaseFirestore.DefaultInstance
            .Collection("UserProfiles").Document(userId)
            .SetAsync(new Dictionary<string, object> { { "fcmToken", token } }, SetOptions.MergeAll);
    }

    private void ResetNotificationSession()
    {
        string userId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        long nowSecs = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        FirebaseFirestore.DefaultInstance
            .Collection("UserProfiles").Document(userId)
            .UpdateAsync(new Dictionary<string, object> { { "lastReadNotifiedAt", nowSecs } });
    }
}
