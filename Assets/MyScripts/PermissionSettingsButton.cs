using UnityEngine;
using System.Runtime.InteropServices;

public class PermissionSettingsButton : MonoBehaviour
{
    public enum PermissionType { Camera, Notifications }

    public PermissionType permissionType;
    public GameObject     onState;
    public GameObject     offState;

    private int _notifStatus = -1; // cached async result; -1 = not yet fetched

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int  _GetCameraAuthorizationStatus();
    [DllImport("__Internal")] static extern void _RequestCameraAuthorization(string goName);
    [DllImport("__Internal")] static extern void _GetNotificationStatus(string goName);
    [DllImport("__Internal")] static extern void _RequestNotificationPermission(string goName);
#endif

    private void OnEnable() => Refresh();

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) Refresh();
    }

    // ── Refresh ────────────────────────────────────────────────────────────

    private void Refresh()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (permissionType == PermissionType.Camera)
            SetState(_GetCameraAuthorizationStatus() == 3);
        else
            _GetNotificationStatus(gameObject.name); // result arrives in OnNotificationStatusResult
#else
        SetState(true);
#endif
    }

    // ── Button tap ─────────────────────────────────────────────────────────

    public void OnButtonTapped()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (permissionType == PermissionType.Camera)
        {
            // Camera can never be re-requested after first decision — always go to Settings
            Application.OpenURL("app-settings:");
        }
        else
        {
            if (_notifStatus == 0) // not yet asked — fire the system prompt
                _RequestNotificationPermission(gameObject.name);
            else
                Application.OpenURL("app-settings:");
        }
#endif
    }

    // ── Native callbacks ───────────────────────────────────────────────────

    // Called by CameraPermissionBridge via UnitySendMessage
    private void OnCameraPermissionResult(string code)
    {
        if (int.TryParse(code, out int status))
            SetState(status == 3);
    }

    // Called by NotificationsBridge via UnitySendMessage
    private void OnNotificationStatusResult(string code)
    {
        if (int.TryParse(code, out int status))
        {
            _notifStatus = status;
            SetState(status == 2);
        }
    }

    // ── State ──────────────────────────────────────────────────────────────

    private void SetState(bool granted)
    {
        if (onState  != null) onState.SetActive(granted);
        if (offState != null) offState.SetActive(!granted);
    }
}
