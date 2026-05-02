using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class ConnectPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_InputField codeInput;
    [SerializeField] TextMeshProUGUI feedbackText;
    [SerializeField] Button sendButton;

    [Header("Feedback")]
    [SerializeField] Color successColor = new Color(0f, 0.78f, 0.78f, 1f);
    [SerializeField] Color errorColor   = Color.red;
    [TextArea, SerializeField] string successMessage = "Request sent!\n\nThey'll appear in your friends list once they accept your request.";
    [TextArea, SerializeField] string errorMessage   = "Friend code invalid";

    const string UserProfilesCollection   = "UserProfiles";
    const string FriendRequestsCollection = "FriendRequests";
    const string FriendsSubcollection     = "friends";

    FirebaseFirestore db;
    bool firebaseReady;

    void Awake()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;
            db = FirebaseFirestore.DefaultInstance;
            firebaseReady = true;
        });

        if (codeInput != null)
        {
            codeInput.characterLimit = 9; // XXXX-XXXX
            codeInput.onValueChanged.AddListener(OnCodeInputChanged);
            codeInput.onEndEdit.AddListener(_ => OnSendRequest());
        }

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendRequest);
    }

    void OnCodeInputChanged(string val)
    {
        // Strip everything that isn't alphanumeric, work in upper case
        var stripped = new System.Text.StringBuilder();
        foreach (char c in val.ToUpperInvariant())
            if (char.IsLetterOrDigit(c)) stripped.Append(c);

        // Cap at 8 raw characters
        if (stripped.Length > 8) stripped.Length = 8;

        // Re-insert hyphen after 4th character
        string formatted = stripped.Length > 4
            ? $"{stripped.ToString().Substring(0, 4)}-{stripped.ToString().Substring(4)}"
            : stripped.ToString();

        if (codeInput.text != formatted)
        {
            codeInput.SetTextWithoutNotify(formatted);
            StartCoroutine(SetCaretNextFrame(formatted.Length));
        }
    }

    IEnumerator SetCaretNextFrame(int position)
    {
        yield return null;
        codeInput.caretPosition = position;
    }

    void OnEnable()
    {
        if (codeInput    != null) codeInput.text    = "";
        if (feedbackText != null) feedbackText.text = "";
    }

    public void OnSendRequest()
    {
        if (!firebaseReady || db == null) return;

        string code = codeInput != null ? codeInput.text.Trim().ToUpperInvariant().Replace("-", "") : "";
        if (string.IsNullOrEmpty(code)) { ShowFeedback(false); return; }

        string myId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(myId)) return;

        // Try without hyphen first (new format), then with hyphen (legacy format)
        string codeWithHyphen = code.Length == 8 ? $"{code.Substring(0, 4)}-{code.Substring(4)}" : code;
        QueryFriendCode(code, myId, found =>
        {
            if (!found)
                QueryFriendCode(codeWithHyphen, myId, stillNotFound =>
                {
                    if (!stillNotFound) ShowFeedback(false);
                });
        });
    }

    void QueryFriendCode(string code, string myId, System.Action<bool> onFound)
    {
        Query q = db.Collection(UserProfilesCollection).WhereEqualTo("friendCode", code);
        q.GetSnapshotAsync().ContinueWithOnMainThread(queryTask =>
        {
            if (queryTask.IsFaulted || queryTask.IsCanceled) { onFound(false); return; }

            DocumentSnapshot targetDoc = null;
            foreach (var doc in queryTask.Result.Documents) { targetDoc = doc; break; }

            if (targetDoc == null) { onFound(false); return; }

            string targetId = targetDoc.Id;
            if (targetId == myId) { ShowFeedback(false); onFound(true); return; }

            db.Collection(UserProfilesCollection).Document(myId)
                .Collection(FriendsSubcollection).Document(targetId)
                .GetSnapshotAsync().ContinueWithOnMainThread(friendCheck =>
                {
                    if (!friendCheck.IsFaulted && !friendCheck.IsCanceled && friendCheck.Result.Exists)
                    {
                        onFound(true); // already friends — silent fail
                        return;
                    }

                    // Check for existing requests between these two users (single-field queries, no composite index)
                    db.Collection(FriendRequestsCollection)
                        .WhereEqualTo("fromUserId", myId)
                        .GetSnapshotAsync().ContinueWithOnMainThread(outgoingTask =>
                        {
                            if (!outgoingTask.IsFaulted && !outgoingTask.IsCanceled)
                            {
                                foreach (var doc in outgoingTask.Result.Documents)
                                {
                                    var rd = doc.ToDictionary();
                                    if (GetString(rd, "toUserId") == targetId && GetString(rd, "status") == "pending")
                                    {
                                        onFound(true); // request already sent — silent fail
                                        return;
                                    }
                                }
                            }

                            // Check if target already sent us a request — if so, auto-accept
                            db.Collection(FriendRequestsCollection)
                                .WhereEqualTo("fromUserId", targetId)
                                .GetSnapshotAsync().ContinueWithOnMainThread(incomingTask =>
                                {
                                    if (!incomingTask.IsFaulted && !incomingTask.IsCanceled)
                                    {
                                        foreach (var doc in incomingTask.Result.Documents)
                                        {
                                            var rd = doc.ToDictionary();
                                            if (GetString(rd, "toUserId") == myId && GetString(rd, "status") == "pending")
                                            {
                                                var friendData = new FriendData
                                                {
                                                    userId       = targetId,
                                                    username     = GetString(targetDoc.ToDictionary(), "UserName"),
                                                    profilePicId = GetInt(targetDoc.ToDictionary(), "profilePicID"),
                                                    status       = FriendData.FriendStatus.PendingIncoming,
                                                    requestId    = doc.Id,
                                                };
                                                if (FriendsManager.instance != null)
                                                    FriendsManager.instance.AcceptRequest(friendData);
                                                ShowFeedback(true);
                                                onFound(true);
                                                return;
                                            }
                                        }
                                    }

                                    SendRequest(myId, targetId, targetDoc.ToDictionary());
                                    onFound(true);
                                });
                        });
                });
        });
    }

    void SendRequest(string myId, string targetId, Dictionary<string, object> targetProfile)
    {
        string targetUsername = GetString(targetProfile, "UserName");
        int    targetPicId    = GetInt(targetProfile, "profilePicID");

        var request = new Dictionary<string, object>
        {
            { "fromUserId",      myId },
            { "toUserId",        targetId },
            { "fromUsername",    UserProfileManager.instance.DisplayName },
            { "fromProfilePicId", UserProfileManager.instance.ProfilePicID },
            { "toUsername",      targetUsername },
            { "toProfilePicId",  targetPicId },
            { "status",          "pending" },
            { "createdAt",       System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        };

        db.Collection(FriendRequestsCollection).AddAsync(request).ContinueWithOnMainThread(addTask =>
        {
            ShowFeedback(!addTask.IsFaulted && !addTask.IsCanceled);
        });
    }

    void ShowFeedback(bool success)
    {
        if (feedbackText == null) return;
        feedbackText.text  = success ? successMessage : errorMessage;
        feedbackText.color = success ? successColor   : errorColor;
    }

    public void Close() => gameObject.SetActive(false);

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
}
