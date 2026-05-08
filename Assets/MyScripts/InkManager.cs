using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;

public class InkManager : MonoBehaviour
{
    public static InkManager instance;

    public static event Action<int, bool> onInkChanged; // amount, isGain

    const string UserProfilesCollection = "UserProfiles";
    const string InkField               = "ink";

    [Tooltip("Seconds to wait before the gain animation fires — lets the reward panel close first")]
    public float gainDisplayDelay = 1.5f;

    FirebaseFirestore db;
    int _ink;
    int _pendingGain;

    public int CurrentInk  => _ink;
    public int PendingGain  => _pendingGain;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        // Wait for Firebase via UserProfileManager which initialises first.
        StartCoroutine(LoadWhenReady());
    }

    System.Collections.IEnumerator LoadWhenReady()
    {
        yield return new WaitUntil(() =>
            UserProfileManager.instance != null &&
            !string.IsNullOrEmpty(UserProfileManager.instance.UserId) &&
            FirebaseFirestore.DefaultInstance != null);

        db = FirebaseFirestore.DefaultInstance;
        LoadInk();
    }

    void LoadInk()
    {
        string uid = UserProfileManager.instance.UserId;
        db.Collection(UserProfilesCollection).Document(uid)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled) return;
                var d = task.Result.ToDictionary();
                if (d != null && d.TryGetValue(InkField, out var v))
                    _ink = Convert.ToInt32(v);
                onInkChanged?.Invoke(_ink, false);
            });
    }

    // Queue a reward to be committed when the player dismisses the reward panel.
    public void QueueGain(int amount)
    {
        if (amount <= 0) return;
        _pendingGain += amount;
    }

    // Called by the reward panel close button.
    public void ApplyPendingGain()
    {
        if (_pendingGain <= 0) return;
        int amount = _pendingGain;
        _pendingGain = 0;
        ApplyWithDelay(amount);
    }

    public void AddInk(int amount)
    {
        if (amount <= 0) return;
        _ink += amount;
        Save();
        onInkChanged?.Invoke(_ink, true);
    }

    public void ApplyWithDelay(int amount)
    {
        if (amount <= 0) return;
        StartCoroutine(DelayedApply(amount));
    }

    IEnumerator DelayedApply(int amount)
    {
        yield return new WaitForSeconds(gainDisplayDelay);
        AddInk(amount);
    }

    public void RemoveInk(int amount)
    {
        if (amount <= 0) return;
        _ink = Mathf.Max(0, _ink - amount);
        Save();
        onInkChanged?.Invoke(_ink, false);
    }

    void Save()
    {
        if (db == null) return;
        string uid = UserProfileManager.instance?.UserId;
        if (string.IsNullOrEmpty(uid)) return;

        db.Collection(UserProfilesCollection).Document(uid)
            .SetAsync(new Dictionary<string, object> { { InkField, _ink } }, SetOptions.MergeAll);
    }
}
