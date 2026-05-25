using System;
using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to the invisible drag-handle image at the top of the slider panel.
/// Assign 'panel' to the slider RectTransform whose height is animated.
/// Panel must be anchored at the bottom of its parent.
/// </summary>
public class MapBottomSlider : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum SliderState { Hidden, Bottom, Lower, Quarter, Half, Full }

    [Header("References")]
    [SerializeField] RectTransform panel;
    [SerializeField] RectTransform mapViewport;
    [SerializeField] GameObject dragHandle;

    [Header("Snap Heights (pixels)")]
    [SerializeField] float heightHidden   = 0f;
    [SerializeField] float heightBottom   = 80f;
    [SerializeField] float heightLower    = 160f;
    [SerializeField] float heightQuarter  = 240f;
    [SerializeField] float heightHalf     = 480f;
    [SerializeField] float heightFull     = 860f;
    [SerializeField] float heightKeyboard = 320f;

    [Header("Animation")]
    [SerializeField] float lerpDuration = 0.25f;

    [FoldoutGroup("State Events")] public UnityEvent onHidden;
    [FoldoutGroup("State Events")] public UnityEvent onBottom;
    [FoldoutGroup("State Events")] public UnityEvent onLower;
    [FoldoutGroup("State Events")] public UnityEvent onQuarter;
    [FoldoutGroup("State Events")] public UnityEvent onHalf;
    [FoldoutGroup("State Events")] public UnityEvent onFull;

    public static MapBottomSlider instance { get; private set; }
    public static event Action<SliderState> onStateChanged;

    public SliderState CurrentState { get; private set; } = SliderState.Hidden;
    public float CurrentHeight => panel != null ? panel.sizeDelta.y : 0f;

    private SliderState? _lockedState   = null;
    private SliderState  _maxState      = SliderState.Full;
    private bool         _keyboardMode  = false;
    private SliderState  _preKeyboard   = SliderState.Hidden;
    private bool         _isDragging    = false;
    private float        _dragStartY;
    private float        _dragStartHeight;
    private Coroutine    _lerpCoroutine;

    private readonly System.Collections.Generic.Queue<(float y, float t)> _dragHistory
        = new System.Collections.Generic.Queue<(float, float)>();
    private const float VelocityWindow     = 0.12f; // seconds of history to keep
    private const float VelocityStationary = 100f;  // px/s — treat as stationary, snap to nearest
    private const float VelocityFlick      = 500f;  // px/s — jump an extra stop

    void Awake() => instance = this;

    // ── Public API ────────────────────────────────────────────────────────

    public void SetState(SliderState state)          { if (!_keyboardMode) GoToState(Clamp(state), animate: true); }
    public void SetStateImmediate(SliderState state) { if (!_keyboardMode) GoToState(Clamp(state), animate: false); }

    // Convenience wrappers for Inspector UnityEvent bindings
    public void SetHidden()  => SetState(SliderState.Hidden);
    public void SetBottom()  => SetState(SliderState.Bottom);
    public void SetLower()   => SetState(SliderState.Lower);
    public void SetQuarter() => SetState(SliderState.Quarter);
    public void SetHalf()    => SetState(SliderState.Half);
    public void SetFull()    => SetState(SliderState.Full);

    /// Locks the slider at a given state. Drag is disabled while locked.
    public void LockAtState(SliderState state)
    {
        _lockedState = state;
        GoToState(Clamp(state), animate: true);
    }

    public void Unlock() => _lockedState = null;

    public void SetDragEnabled(bool enabled)
    {
        _lockedState = enabled ? (SliderState?)null : CurrentState;
        if (dragHandle != null) dragHandle.SetActive(enabled);
    }

    /// Prevents the slider from going above the given state.
    public void SetMaxState(SliderState state)
    {
        _maxState = state;
        if (CurrentState > state) GoToState(state, animate: true);
    }

    public void ClearMaxState() => _maxState = SliderState.Full;

    /// Locks slider to keyboard height. Drag and all other state changes are ignored until ExitKeyboardMode() is called.
    public void EnterKeyboardMode()
    {
        if (_keyboardMode) return;
        _keyboardMode = true;
        _preKeyboard  = CurrentState;
        if (panel.sizeDelta.y >= heightKeyboard) return;
        StopLerp();
        _lerpCoroutine = StartCoroutine(LerpTo(heightKeyboard, CurrentState));
    }

    /// Restores the state that was active before keyboard mode was entered.
    public void ExitKeyboardMode()
    {
        if (!_keyboardMode) return;
        _keyboardMode = false;
        GoToState(_preKeyboard, animate: true);
    }

    // ── Drag ──────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e)
    {
        if (_lockedState.HasValue || _keyboardMode) return;
        _isDragging      = true;
        _dragStartY      = e.position.y;
        _dragStartHeight = panel.sizeDelta.y;
        _dragHistory.Clear();
        StopLerp();
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_isDragging || _lockedState.HasValue) return;
        float delta  = e.position.y - _dragStartY;
        float target = Mathf.Clamp(_dragStartHeight + delta, heightHidden, HeightForState(_maxState));
        SetPanelHeight(target);

        float now = Time.unscaledTime;
        _dragHistory.Enqueue((e.position.y, now));
        while (_dragHistory.Count > 0 && now - _dragHistory.Peek().t > VelocityWindow)
            _dragHistory.Dequeue();
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        float currentH = panel.sizeDelta.y;
        float velocity = CalculateVelocity(); // positive = upward

        SliderState next;
        if (Mathf.Abs(velocity) < VelocityStationary)
        {
            next = NearestState(currentH);
        }
        else
        {
            bool up = velocity > 0f;
            next = up ? NearestStateAbove(currentH) : NearestStateBelow(currentH);

            if (Mathf.Abs(velocity) >= VelocityFlick)
            {
                next = up ? NearestStateAbove(HeightForState(next) + 1f)
                          : NearestStateBelow(HeightForState(next) - 1f);
            }
        }

        GoToState(Clamp(next), animate: true);
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private void GoToState(SliderState state, bool animate)
    {
        CurrentState = state;
        FireEvent(state);
        onStateChanged?.Invoke(state);
        StopLerp();

        if (animate)
            _lerpCoroutine = StartCoroutine(LerpTo(HeightForState(state), state));
        else
            SetPanelHeight(HeightForState(state));
    }

    private IEnumerator LerpTo(float target, SliderState state)
    {
        float start   = panel.sizeDelta.y;
        float elapsed = 0f;

        while (elapsed < lerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / lerpDuration));
            SetPanelHeight(Mathf.Lerp(start, target, t));
            yield return null;
        }

        SetPanelHeight(target);
        FireEvent(state);
    }

    private void SetPanelHeight(float h)
    {
        var sd = panel.sizeDelta;
        sd.y = h;
        panel.sizeDelta = sd;

        if (mapViewport != null)
        {
            var min = mapViewport.offsetMin;
            min.y = h;
            mapViewport.offsetMin = min;
        }
    }

    private void StopLerp()
    {
        if (_lerpCoroutine != null) { StopCoroutine(_lerpCoroutine); _lerpCoroutine = null; }
    }

    private void FireEvent(SliderState state)
    {
        switch (state)
        {
            case SliderState.Hidden:  onHidden.Invoke();  break;
            case SliderState.Bottom:  onBottom.Invoke();  break;
            case SliderState.Lower:   onLower.Invoke();   break;
            case SliderState.Quarter: onQuarter.Invoke(); break;
            case SliderState.Half:    onHalf.Invoke();    break;
            case SliderState.Full:    onFull.Invoke();    break;
        }
    }

    private float HeightForState(SliderState state) => state switch
    {
        SliderState.Hidden  => heightHidden,
        SliderState.Bottom  => heightBottom,
        SliderState.Lower   => heightLower,
        SliderState.Quarter => heightQuarter,
        SliderState.Half    => heightHalf,
        SliderState.Full    => heightFull,
        _                   => heightHidden
    };

    private SliderState NearestStateAbove(float h)
    {
        if (h <= heightBottom)  return SliderState.Bottom;
        if (h <= heightLower)   return SliderState.Lower;
        if (h <= heightQuarter) return SliderState.Quarter;
        if (h <= heightHalf)    return SliderState.Half;
        return SliderState.Full;
    }

    private SliderState NearestStateBelow(float h)
    {
        if (h >= heightFull)    return SliderState.Half;
        if (h >= heightHalf)    return SliderState.Quarter;
        if (h >= heightQuarter) return SliderState.Lower;
        if (h >= heightLower)   return SliderState.Bottom;
        if (h >= heightBottom)  return SliderState.Hidden;
        return SliderState.Hidden;
    }

    private SliderState Clamp(SliderState state) =>
        state > _maxState ? _maxState : state;

    private float CalculateVelocity()
    {
        if (_dragHistory.Count < 2) return 0f;
        var arr = _dragHistory.ToArray();
        float dy = arr[arr.Length - 1].y - arr[0].y;
        float dt = arr[arr.Length - 1].t - arr[0].t;
        return dt > 0f ? dy / dt : 0f;
    }

    private SliderState NearestState(float h)
    {
        SliderState best  = SliderState.Hidden;
        float       bestD = float.MaxValue;
        foreach (SliderState s in System.Enum.GetValues(typeof(SliderState)))
        {
            float d = Mathf.Abs(h - HeightForState(s));
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }
}
