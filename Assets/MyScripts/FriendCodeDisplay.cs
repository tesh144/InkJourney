using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TMPro;
using UnityEngine;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

[RequireComponent(typeof(TextMeshProUGUI))]
public class FriendCodeDisplay : MonoBehaviour
{
    const string FriendCodeKey           = "UserProfile.FriendCode";
    const string UserProfilesCollection  = "UserProfiles";

    [TextArea, SerializeField] string shareMessageTemplate = "Connect with me on Ink Journey! Just enter my code: {0}";

    TextMeshProUGUI label;

    void Awake() => label = GetComponent<TextMeshProUGUI>();

    void OnEnable()
    {
        // Show cached code immediately while we verify with Firestore
        string cached = PlayerPrefs.GetString(FriendCodeKey, "");
        if (!string.IsNullOrEmpty(cached))
            Display(cached);

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;
            LoadOrGenerateFriendCode(FirebaseFirestore.DefaultInstance);
        });
    }

    void LoadOrGenerateFriendCode(FirebaseFirestore db)
    {
        string userId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        db.Collection(UserProfilesCollection).Document(userId)
            .GetSnapshotAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled) return;

                string code = "";
                if (task.Result.Exists)
                {
                    var data = task.Result.ToDictionary();
                    if (data.TryGetValue("friendCode", out var v) && v != null)
                        code = v.ToString();
                }

                if (string.IsNullOrEmpty(code))
                {
                    code = GenerateCode();
                    db.Collection(UserProfilesCollection).Document(userId)
                        .SetAsync(new Dictionary<string, object> { { "friendCode", code } }, SetOptions.MergeAll);
                }

                PlayerPrefs.SetString(FriendCodeKey, code);
                PlayerPrefs.Save();
                Display(code);
            });
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void _ShareText(string text);
#endif

    public void CopyToClipboard()
    {
        string code = PlayerPrefs.GetString(FriendCodeKey, "");
        if (string.IsNullOrEmpty(code)) return;
        GUIUtility.systemCopyBuffer = code.Length == 8
            ? $"{code.Substring(0, 4)}-{code.Substring(4, 4)}"
            : code;
    }

    public void ShareFriendCode()
    {
        string code = PlayerPrefs.GetString(FriendCodeKey, "");
        if (string.IsNullOrEmpty(code)) return;

        string formatted = code.Length == 8 ? $"{code.Substring(0, 4)}-{code.Substring(4, 4)}" : code;
        string message   = string.Format(shareMessageTemplate, formatted);

#if UNITY_ANDROID && !UNITY_EDITOR
        var intentClass  = new AndroidJavaClass("android.content.Intent");
        var intentObject = new AndroidJavaObject("android.content.Intent");
        intentObject.Call<AndroidJavaObject>("setAction",   intentClass.GetStatic<string>("ACTION_SEND"));
        intentObject.Call<AndroidJavaObject>("putExtra",    intentClass.GetStatic<string>("EXTRA_TEXT"), message);
        intentObject.Call<AndroidJavaObject>("setType",     "text/plain");
        var unity   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
        var chooser  = intentClass.CallStatic<AndroidJavaObject>("createChooser", intentObject, "Share Friend Code");
        activity.Call("startActivity", chooser);
#elif UNITY_IOS && !UNITY_EDITOR
        _ShareText(message);
#else
        GUIUtility.systemCopyBuffer = formatted;
#endif
    }

    void Display(string code)
    {
        // code is stored without hyphen; add it for readability
        if (label == null) return;
        label.text = code.Length == 8 ? $"{code.Substring(0, 4)}-{code.Substring(4, 4)}" : code;
    }

    static string GenerateCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rng = new System.Random();
        var sb  = new StringBuilder(8);
        for (int i = 0; i < 8; i++) sb.Append(chars[rng.Next(chars.Length)]);
        return sb.ToString(); // stored without hyphen
    }
}
