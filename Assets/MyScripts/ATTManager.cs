using UnityEngine;
using System.Runtime.InteropServices;

public class ATTManager : MonoBehaviour
{
    public static ATTManager instance;

    // 0 = not determined, 1 = restricted, 2 = denied, 3 = authorized
    public static int TrackingStatus { get; private set; } = -1;

    private const string PrefKey = "ATT_Requested";

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void _RequestTrackingAuthorization(string goName);
    [DllImport("__Internal")] static extern int  _GetTrackingAuthorizationStatus();
#endif

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_IOS && !UNITY_EDITOR
        TrackingStatus = _GetTrackingAuthorizationStatus();
#endif
    }

    // Call this once, at the right moment (see below).
    // Safe to call multiple times — only fires the system prompt once.
    public void RequestIfNeeded()
    {
        if (PlayerPrefs.GetInt(PrefKey, 0) == 1) return;
        PlayerPrefs.SetInt(PrefKey, 1);
        PlayerPrefs.Save();

#if UNITY_IOS && !UNITY_EDITOR
        _RequestTrackingAuthorization(gameObject.name);
#endif
    }

    // Called by native via UnitySendMessage
    private void OnATTResponse(string code)
    {
        if (int.TryParse(code, out int status))
            TrackingStatus = status;
    }
}
