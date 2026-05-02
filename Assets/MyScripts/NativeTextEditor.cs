using UnityEngine;
using TMPro;
using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Opens a full-screen native iOS editor for title + content.
/// Auto-creates a "NativeTextEditorBridge" GameObject to receive callbacks.
/// Usage: NativeTextEditor.Show(titleField, contentField);
/// </summary>
public class NativeTextEditor : MonoBehaviour
{
    public static NativeTextEditor instance { get; private set; }

    private TMP_InputField         _titleField;
    private TMP_InputField         _contentField;
    private Action<string, string> _onComplete;
    private Action                 _onCancel;

    // ─── Bootstrap ───────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (instance != null) return;
        var go = new GameObject("NativeTextEditorBridge");
        DontDestroyOnLoad(go);
        go.AddComponent<NativeTextEditor>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Show the native editor pre-filled with both fields' current text.
    /// On Done, both fields are updated and onComplete is called with (title, content).
    /// </summary>
    public static void Show(TMP_InputField titleField,
                            TMP_InputField contentField,
                            string placeholder             = "",
                            Action<string, string> onComplete = null,
                            Action onCancel                = null)
    {
        if (instance == null) { Debug.LogError("[NativeTextEditor] No instance."); return; }

        instance._titleField   = titleField;
        instance._contentField = contentField;
        instance._onComplete   = onComplete;
        instance._onCancel     = onCancel;

#if UNITY_IOS && !UNITY_EDITOR
        NativeTextEditor_Show(
            titleField?.text   ?? "",
            contentField?.text ?? "",
            placeholder);
#else
        // Editor fallback — activate content field directly
        contentField?.Select();
        contentField?.ActivateInputField();
#endif
    }

    public static void Hide()
    {
#if UNITY_IOS && !UNITY_EDITOR
        NativeTextEditor_Hide();
#endif
    }

    public static void Prewarm()
    {
#if UNITY_IOS && !UNITY_EDITOR
        NativeTextEditor_Prewarm();
#endif
    }

    public static void StopPrewarm()
    {
#if UNITY_IOS && !UNITY_EDITOR
        NativeTextEditor_StopPrewarm();
#endif
    }

    // ─── Native bindings ─────────────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void NativeTextEditor_Show(string title, string content, string placeholder);
    [DllImport("__Internal")] static extern void NativeTextEditor_Hide();
    [DllImport("__Internal")] static extern void NativeTextEditor_Prewarm();
    [DllImport("__Internal")] static extern void NativeTextEditor_StopPrewarm();
#endif

    // ─── UnitySendMessage callbacks ──────────────────────────────────────────

    // Obj-C sends: title + '\x1E' + content
    void OnTextEditComplete(string combined)
    {
        var parts   = combined.Split('\x1E');
        string t    = parts.Length > 0 ? parts[0] : "";
        string c    = parts.Length > 1 ? parts[1] : "";

        if (_titleField   != null) _titleField.text   = t;
        if (_contentField != null) _contentField.text = c;

        _onComplete?.Invoke(t, c);
        ClearState();
    }

    void OnTextEditCancelled(string _)
    {
        _onCancel?.Invoke();
        ClearState();
    }

    void ClearState()
    {
        _titleField   = null;
        _contentField = null;
        _onComplete   = null;
        _onCancel     = null;
    }
}
