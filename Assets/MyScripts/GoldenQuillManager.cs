using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

public class GoldenQuillManager : MonoBehaviour
{
    public static GoldenQuillManager instance;

    public static event Action<int, bool> onQuillChanged; // amount, isGain

    const string UserProfilesCollection = "UserProfiles";
    const string QuillField             = "goldenQuills";

    FirebaseFirestore db;
    int _quills;

    public int  CurrentQuills        => _quills;
    public bool CanAfford(int amount) => _quills >= amount;

    void Awake() => instance = this;

    void Start() => StartCoroutine(LoadWhenReady());

    IEnumerator LoadWhenReady()
    {
        yield return new WaitUntil(() =>
            UserProfileManager.instance != null &&
            !string.IsNullOrEmpty(UserProfileManager.instance.UserId) &&
            FirebaseFirestore.DefaultInstance != null);

        db = FirebaseFirestore.DefaultInstance;
        Load();
    }

    void Load()
    {
        string uid = UserProfileManager.instance.UserId;
        db.Collection(UserProfilesCollection).Document(uid)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled) return;
                var d = task.Result.ToDictionary();
                if (d != null && d.TryGetValue(QuillField, out var v))
                    _quills = Convert.ToInt32(v);
                onQuillChanged?.Invoke(_quills, false);
            });
    }

    public void AddQuill(int amount)
    {
        if (amount <= 0) return;
        _quills += amount;
        Save();
        onQuillChanged?.Invoke(_quills, true);
    }

    public void RemoveQuill(int amount)
    {
        if (amount <= 0) return;
        _quills = Mathf.Max(0, _quills - amount);
        Save();
        onQuillChanged?.Invoke(_quills, false);
    }

    void Save()
    {
        if (db == null) return;
        string uid = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(uid)) return;
        db.Collection(UserProfilesCollection).Document(uid)
            .SetAsync(new Dictionary<string, object> { { QuillField, _quills } }, SetOptions.MergeAll);
    }
}
