using System.Collections;
using UnityEngine;
using TMPro;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class FriendRequestBadge : MonoBehaviour
{
    [SerializeField] GameObject body;
    [SerializeField] TMP_Text   countText;

    public static FriendRequestBadge instance;

    const float RefreshInterval = 10f;

    FirebaseFirestore db;
    bool firebaseReady;

    private void Awake()
    {
        instance = this;
        SetCount(0);

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available) return;
            db = FirebaseFirestore.DefaultInstance;
            firebaseReady = true;
            Refresh();
            StartCoroutine(RefreshLoop());
        });
    }

    private IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(RefreshInterval);
            Refresh();
        }
    }

    public void Refresh()
    {
        if (!firebaseReady) return;

        string userId = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        db.Collection("FriendRequests")
          .WhereEqualTo("toUserId", userId)
          .WhereEqualTo("status", "pending")
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted || task.IsCanceled) return;
              SetCount(task.Result.Count);
          });
    }

    private void SetCount(int count)
    {
        bool visible = count > 0;
        if (body != null)      body.SetActive(visible);
        if (countText != null) countText.text = count >= 99 ? "99" : count.ToString();
    }
}
