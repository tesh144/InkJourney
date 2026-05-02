using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add to the ScrollRect GameObject.
/// - Pinch to zoom
/// - Two-finger twist to rotate (Google Maps style)
/// - Compass arrow shows current map heading; tap it to snap back to north
///
/// Wire up:
///   mapContent   — RectTransform holding the map image and all spawned labels
///   compassArrow — optional UI RectTransform; rotates to show current map heading
///   compassButton — optional Button on the compass; call SnapToNorth() from OnClick
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class MapInputController : MonoBehaviour
{
    [Header("References")]
    public RectTransform mapContent;
    [Tooltip("Compass arrow — rotates to reflect the map's current heading")]
    public RectTransform compassArrow;
    [Tooltip("Assign the compass Button here — hides when north-up, shows when rotated")]
    public GameObject compassButton;

    [Header("Style Change UI")]
    [Tooltip("Disabled when a new map style loads")]
    public GameObject mapHUD;
    [Tooltip("Enabled when a new map style loads")]
    public GameObject exploreButton;
    [Tooltip("If false, style changes will not show the Explore button (useful for hotspot map)")]
    public bool enableExploreButtonOnStyleChange = true;

    [Header("Zoom")]
    [Tooltip("Enable/disable zoom gestures and editor zoom input for this map window")]
    public bool allowZoom = true;
    public float minScale = 0.5f;
    public float maxScale = 4f;
    [Tooltip("How quickly zoom lerps toward target during pinch")]
    public float zoomSmoothing = 10f;
    [Tooltip("How quickly zoom lerps when snapping to north — lower = slower and smoother")]
    public float snapZoomSmoothing = 2.5f;

    [Header("Rotation")]
    [Tooltip("How quickly the map snaps back to north or lerps to a new angle")]
    public float rotationSmoothing = 8f;

    [Header("Auto-Rotate")]
    [Tooltip("Enable idle map auto-rotation after style load")]
    public bool enableAutoRotate = true;
    [Tooltip("Degrees per second the map rotates when idle after a map design loads")]
    public float autoRotateSpeed = 10f;

    public static MapInputController instance;

    /// <summary>True while the map's actual scale is at (or very near) minScale.</summary>
    public static bool IsFullyZoomedOut { get; private set; }

    /// <summary>Current localScale.x of the map content (zoom level).</summary>
    public static float CurrentScale =>
        instance != null && instance.mapContent != null ? instance.mapContent.localScale.x : 1f;

    private bool _inputBlocked;

    /// <summary>True while the map HUD is active (explore mode). Map pointers will not expand until this is true.</summary>
    public static bool IsExploreActive => instance != null && instance.mapHUD != null && instance.mapHUD.activeSelf;

    private ScrollRect _scrollRect;
    private Canvas     _rootCanvas;
    private float      _targetScale;
    private float      _targetRotation;   // degrees, map Z euler angle
    private float      _lastPinchDist;
    private float      _lastPinchAngle;
    private Vector2    _pinchMidpoint;    // screen-space midpoint of the two fingers
    private bool       _isPinching;
    private bool       _snapZooming;
    private bool       _autoRotating;

    private enum GestureLock { None, Zoom, Rotate }
    private GestureLock _gestureLock;

    private void Awake()
    {
        instance        = this;
        _scrollRect     = GetComponent<ScrollRect>();
        _rootCanvas     = GetComponentInParent<Canvas>();
        _targetScale    = minScale;
        _targetRotation = 0f;
        _autoRotating   = enableAutoRotate;

        // Snap to zoomed-out immediately so the first frame doesn't show max zoom
        if (mapContent != null)
            mapContent.localScale = Vector3.one * minScale;

        if (enableExploreButtonOnStyleChange)
        {
            MainMapUserCursorController cursorController = GetComponent<MainMapUserCursorController>();
            if (cursorController == null)
                cursorController = gameObject.AddComponent<MainMapUserCursorController>();

            if (cursorController != null)
            {
                cursorController.scrollRect = _scrollRect;
                if (cursorController.mapContent == null)
                    cursorController.mapContent = mapContent;
                if (cursorController.mapLoader == null)
                    cursorController.mapLoader = MapLoader.instance;
            }
        }
    }

    private void OnEnable()  => MapLoader.onStyleChanged += OnMapStyleChanged;
    private void OnDisable() => MapLoader.onStyleChanged -= OnMapStyleChanged;

    private void OnMapStyleChanged()
    {
        _targetScale    = minScale;
        _targetRotation = 0f;
        _snapZooming    = false;
        _autoRotating   = enableAutoRotate;

        // Re-centre the scroll view so the player starts looking at their location
        if (MapLoader.instance != null && MapLoader.instance.resetScrollRect != null)
            MapLoader.instance.resetScrollRect.ResetToCentre();

        if (mapHUD != null)
            mapHUD.SetActive(false);

        if (exploreButton != null)
            exploreButton.SetActive(enableExploreButtonOnStyleChange);
    }

    private void Update()
    {
        // Any touch or key press stops the idle auto-rotation
        if (_autoRotating && (Input.touchCount > 0 || Input.anyKey))
            _autoRotating = false;

        if (_autoRotating && enableAutoRotate)
            _targetRotation += autoRotateSpeed * Time.deltaTime;

        HandleTwoFingers();
#if UNITY_EDITOR
        HandleEditorZoom();
        HandleEditorRotate();
#endif
        ApplyZoom();
        ApplyRotation();
        UpdateCompassVisibility();
    }

    // ── Interaction lock (used by MapPointerPopup) ──────────────────────────

    public void SetMapInteractionEnabled(bool enabled)
    {
        _inputBlocked = !enabled;
        if (_scrollRect != null) _scrollRect.enabled = enabled;
        if (!enabled && _isPinching)
        {
            _isPinching  = false;
            _gestureLock = GestureLock.None;
        }
    }

    // ── Two-finger: pinch = zoom, twist = rotate ────────────────────────────

    private void HandleTwoFingers()
    {
        if (_inputBlocked) { _isPinching = false; return; }

        if (Input.touchCount == 2)
        {
            Vector2 p0 = Input.GetTouch(0).position;
            Vector2 p1 = Input.GetTouch(1).position;

            float dist  = Vector2.Distance(p0, p1);
            float angle = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;

            _pinchMidpoint = (p0 + p1) * 0.5f;

            if (!_isPinching)
            {
                _lastPinchDist  = dist;
                _lastPinchAngle = angle;
                _isPinching     = true;
                _snapZooming    = false;
                _gestureLock    = GestureLock.None;
                _scrollRect.enabled = false;
            }
            else
            {
                float deltaAngle = Mathf.DeltaAngle(_lastPinchAngle, angle);
                float deltaScale = _lastPinchDist > 0f ? Mathf.Abs(dist / _lastPinchDist - 1f) : 0f;

                // Lock to whichever gesture moved more first — stays locked until fingers lift
                if (_gestureLock == GestureLock.None)
                {
                    if (deltaScale > 0.02f || Mathf.Abs(deltaAngle) > 3f)
                    {
                        if (!allowZoom)
                        {
                            _gestureLock = GestureLock.Rotate;
                        }
                        else
                        {
                            _gestureLock = deltaScale > Mathf.Abs(deltaAngle) * 0.01f
                                ? GestureLock.Zoom
                                : GestureLock.Rotate;
                        }
                    }
                }

                if (_gestureLock == GestureLock.Zoom)
                {
                    if (allowZoom && _lastPinchDist > 0f)
                        _targetScale = Mathf.Clamp(_targetScale * (dist / _lastPinchDist), minScale, maxScale);
                }
                else if (_gestureLock == GestureLock.Rotate)
                {
                    _targetRotation += deltaAngle;
                }

                _lastPinchDist  = dist;
                _lastPinchAngle = angle;
            }
        }
        else if (_isPinching)
        {
            _isPinching  = false;
            _gestureLock = GestureLock.None;
            _scrollRect.enabled = true;
        }
    }

    // ── Snap to max zoom ───────────────────────────────────────────────────

    public void SnapToMaxZoom()
    {
        if (!allowZoom) return;
        _targetScale = maxScale;
        _snapZooming = true;
    }

    // ── Snap to north (assign to compass Button OnClick) ───────────────────

    public void ResetRotation()
    {
        _targetRotation = 0f;
    }

    public void SnapToNorth()
    {
        _targetRotation = 0f;
        if (allowZoom)
        {
            _targetScale = maxScale;
            _snapZooming = true;
        }
        else
        {
            _snapZooming = false;
        }
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    private void ApplyZoom()
    {
        if (mapContent == null) return;

        if (!allowZoom)
        {
            _targetScale = mapContent.localScale.x;
            IsFullyZoomedOut = mapContent.localScale.x <= minScale + 0.05f;
            return;
        }

        float speed    = _snapZooming ? snapZoomSmoothing : zoomSmoothing;
        float oldScale = mapContent.localScale.x;
        float newScale = Mathf.Lerp(oldScale, _targetScale, speed * Time.deltaTime);

        // When pinch-zooming, reposition the content so the map point under
        // the fingers stays fixed — this makes zoom feel centred on what you see.
        if (_isPinching && !Mathf.Approximately(oldScale, newScale) && oldScale > 0f)
        {
            RectTransform viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : transform as RectTransform;
            Camera cam = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? Camera.main : null;

            // Map point currently under the pinch midpoint (in viewport local space)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, _pinchMidpoint, cam, out Vector2 viewportPinch);
            Vector2 contentAnchor = (viewportPinch - mapContent.anchoredPosition) / oldScale;

            mapContent.localScale = Vector3.one * newScale;

            // Shift content so the same map point stays under the pinch midpoint
            mapContent.anchoredPosition = viewportPinch - contentAnchor * newScale;
        }
        else
        {
            mapContent.localScale = Vector3.one * newScale;
        }

        IsFullyZoomedOut = newScale <= minScale + 0.05f;

        if (_snapZooming && Mathf.Abs(newScale - _targetScale) < 0.01f)
            _snapZooming = false;
    }

    private void ApplyRotation()
    {
        if (mapContent == null) return;
        float current = mapContent.localEulerAngles.z;
        float smooth  = Mathf.LerpAngle(current, _targetRotation, rotationSmoothing * Time.deltaTime);
        mapContent.localEulerAngles = new Vector3(0f, 0f, smooth);

        // Compass arrow points opposite to map rotation so it reflects the heading
        if (compassArrow != null)
            compassArrow.localEulerAngles = new Vector3(0f, 0f, -smooth);
    }

    private void UpdateCompassVisibility()
    {
        // Compass button is always visible
    }

    // ── Editor helpers ──────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void HandleEditorZoom()
    {
        if (!allowZoom)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
            _targetScale = Mathf.Clamp(_targetScale * (1f + scroll * 3f), minScale, maxScale);

        if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus))
            _targetScale = Mathf.Clamp(_targetScale * (1f + 2f * Time.deltaTime), minScale, maxScale);
        if (Input.GetKey(KeyCode.Minus))
            _targetScale = Mathf.Clamp(_targetScale * (1f - 2f * Time.deltaTime), minScale, maxScale);
    }

    private void HandleEditorRotate()
    {
        if (Input.GetKey(KeyCode.Q)) _targetRotation -= 90f * Time.deltaTime;
        if (Input.GetKey(KeyCode.E)) _targetRotation += 90f * Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.R)) SnapToNorth();
    }
#endif
}
