using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MainMapUserCursorController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    public static MainMapUserCursorController Instance { get; private set; }

    private enum TrackingState
    {
        Following,
        Exploring,
        Reloading,
        Settling
    }

    [Header("References")]
    public ScrollRect scrollRect;
    public RectTransform mapContent;
    public RectTransform userCursor;
    public MapLoader mapLoader;

    [Header("Tracking")]
    public bool autoFollowOnOpen = true;
    public bool autoExitFollowOnUserPan = true;
    public bool allowMapReloadWhileExploring = false;
    public float gpsSampleInterval = 1f;
    public float gpsDeadzoneMeters = 1.5f;
    public float cursorSmoothTime = 0.18f;
    public float cursorMaxSpeedPixelsPerSecond = 1400f;

    [Header("Scroll Behavior")]
    public float followRecenteringSpeed = 8f;
    public float reloadSettleSeconds = 0.2f;

    [Header("Auto-Return to Follow")]
    [Tooltip("Automatically return to Following mode after the user stops interacting with the map")]
    public bool autoReturnToFollow = true;
    [Tooltip("Seconds of inactivity before returning to Following mode")]
    public float autoReturnDelay = 3f;

    [Header("Bounds")]
    [Tooltip("Extra inset from map edges so the cursor never appears clipped")]
    public float cursorEdgePadding = 8f;

    [Header("Debug")]
    public bool verboseLogs = false;

    public bool IsFollowing => _state == TrackingState.Following;
    public bool ShouldAllowAutomaticMapReload => IsFollowing || allowMapReloadWhileExploring;

    private TrackingState _state = TrackingState.Following;
    private Vector2 _targetCursorPos;
    private Vector2 _cursorVelocity;
    private float _sampleTimer;
    private float _settleEndTime;
    private float _lastSampleLat;
    private float _lastSampleLon;
    private bool _hasLastSample;
    private bool _savedInertia;
    private float _lastInteractionTime = float.MinValue;
    private bool _isUserDragging;
    private bool _suppressFollowRecentering;
    private TrackingState _stateBeforeReload = TrackingState.Following;
    private float _preReloadHorizontal = 0.5f;
    private float _preReloadVertical = 0.5f;
    private bool _hasPreReloadScroll;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (scrollRect == null)
            scrollRect = GetComponent<ScrollRect>();

        if (mapLoader == null)
            mapLoader = MapLoader.instance;

        if (mapContent == null && scrollRect != null)
            mapContent = scrollRect.content;

        if (userCursor == null && mapContent != null)
        {
            Transform found = FindByNameRecursive(mapContent, "cursor");
            if (found is RectTransform cursorRect)
                userCursor = cursorRect;
        }

        _state = autoFollowOnOpen ? TrackingState.Following : TrackingState.Exploring;
    }

    private void OnEnable()
    {
        MapLoader.onMainMapReloadStateChanged += OnMainMapReloadStateChanged;
        MapLoader.onStyleChanged += OnMapStyleChanged;
    }

    private void OnDisable()
    {
        MapLoader.onMainMapReloadStateChanged -= OnMainMapReloadStateChanged;
        MapLoader.onStyleChanged -= OnMapStyleChanged;

        if (scrollRect != null)
            scrollRect.inertia = _savedInertia;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _isUserDragging = true;
        MarkUserInteraction();
    }

    public void OnDrag(PointerEventData eventData)
    {
        _isUserDragging = true;
        MarkUserInteraction();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isUserDragging = false;
        _lastInteractionTime = Time.unscaledTime;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isUserDragging = true;
        MarkUserInteraction();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isUserDragging = false;
        _lastInteractionTime = Time.unscaledTime;
    }

    private void MarkUserInteraction()
    {
        _lastInteractionTime = Time.unscaledTime;

        if (!autoExitFollowOnUserPan)
            return;

        if (_state == TrackingState.Following || _state == TrackingState.Settling)
        {
            _state = TrackingState.Exploring;
            if (verboseLogs)
                Debug.Log("[MainMapCursor] Entered Exploring due to user interaction");
        }
    }

    public void SetFollowMode(bool enabled)
    {
        _state = enabled ? TrackingState.Following : TrackingState.Exploring;

        if (enabled)
        {
            _suppressFollowRecentering = false;
            SnapScrollToCenter();
        }
    }

    private void Update()
    {
        if (mapLoader == null)
            mapLoader = MapLoader.instance;

        if (mapLoader == null || !mapLoader.mapLoaded || mapContent == null || userCursor == null)
            return;

        _sampleTimer += Time.unscaledDeltaTime;
        if (_sampleTimer >= Mathf.Max(0.05f, gpsSampleInterval))
        {
            _sampleTimer = 0f;
            UpdateTargetCursorPositionFromGps();
        }

        if (_state == TrackingState.Settling && Time.unscaledTime >= _settleEndTime)
            _state = _stateBeforeReload == TrackingState.Reloading ? TrackingState.Exploring : _stateBeforeReload;

        if (_state == TrackingState.Exploring && autoReturnToFollow &&
            _lastInteractionTime > float.MinValue &&
            Time.unscaledTime - _lastInteractionTime >= autoReturnDelay)
        {
            _state = TrackingState.Following;
            _suppressFollowRecentering = true;
            if (verboseLogs)
                Debug.Log("[MainMapCursor] Auto-returned to Following after idle");
        }

        if (_state != TrackingState.Reloading)
        {
            float maxSpeed = Mathf.Max(100f, cursorMaxSpeedPixelsPerSecond);
            Vector2 smoothed = Vector2.SmoothDamp(
                userCursor.anchoredPosition,
                _targetCursorPos,
                ref _cursorVelocity,
                Mathf.Max(0.02f, cursorSmoothTime),
                maxSpeed,
                Time.unscaledDeltaTime);
            userCursor.anchoredPosition = ClampToMapBounds(smoothed);
        }

        if (_state == TrackingState.Following && !_isUserDragging && !_suppressFollowRecentering)
            SmoothRecenterScroll();
    }

    private void OnMapStyleChanged()
    {
        _stateBeforeReload = _state == TrackingState.Reloading ? TrackingState.Exploring : _state;

        if (scrollRect != null)
        {
            _preReloadHorizontal = scrollRect.horizontalNormalizedPosition;
            _preReloadVertical = scrollRect.verticalNormalizedPosition;
            _hasPreReloadScroll = true;
        }

        _state = _stateBeforeReload;
        _hasLastSample = false;
        _cursorVelocity = Vector2.zero;
        UpdateTargetCursorPositionFromGps();

        if (_hasPreReloadScroll && scrollRect != null)
        {
            scrollRect.horizontalNormalizedPosition = _preReloadHorizontal;
            scrollRect.verticalNormalizedPosition = _preReloadVertical;
            scrollRect.velocity = Vector2.zero;
        }

        _suppressFollowRecentering = true;
    }

    private void OnMainMapReloadStateChanged(bool isReloading)
    {
        if (scrollRect != null)
        {
            if (isReloading)
            {
                _savedInertia = scrollRect.inertia;
                scrollRect.velocity = Vector2.zero;
                scrollRect.inertia = false;
            }
            else
            {
                scrollRect.inertia = _savedInertia;
            }
        }

        if (isReloading)
        {
            _stateBeforeReload = _state == TrackingState.Reloading ? TrackingState.Exploring : _state;

            if (scrollRect != null)
            {
                _preReloadHorizontal = scrollRect.horizontalNormalizedPosition;
                _preReloadVertical = scrollRect.verticalNormalizedPosition;
                _hasPreReloadScroll = true;
            }

            _state = TrackingState.Reloading;
            _cursorVelocity = Vector2.zero;
            if (verboseLogs)
                Debug.Log("[MainMapCursor] Entering Reloading state");
        }
        else
        {
            UpdateTargetCursorPositionFromGps();

            if (_hasPreReloadScroll && scrollRect != null)
            {
                scrollRect.horizontalNormalizedPosition = _preReloadHorizontal;
                scrollRect.verticalNormalizedPosition = _preReloadVertical;
                scrollRect.velocity = Vector2.zero;
            }

            _state = _stateBeforeReload == TrackingState.Reloading ? TrackingState.Exploring : _stateBeforeReload;
            _suppressFollowRecentering = true;
            if (verboseLogs)
                Debug.Log("[MainMapCursor] Reload finished, restored pre-reload viewport/state");
        }
    }

    private void UpdateTargetCursorPositionFromGps()
    {
        if (GPSManager.Instance == null || mapLoader == null || mapContent == null)
            return;

        float lat = GPSManager.Instance.latitude;
        float lon = GPSManager.Instance.longitude;
        if (lat == 0f && lon == 0f)
            return;

        if (_hasLastSample)
        {
            float movedMeters = DistanceMeters(_lastSampleLat, _lastSampleLon, lat, lon);
            if (movedMeters < Mathf.Max(0f, gpsDeadzoneMeters))
                return;
        }

        _lastSampleLat = lat;
        _lastSampleLon = lon;
        _hasLastSample = true;

        Vector2 logical = ProjectToMapLogicalPosition(lat, lon, mapLoader.CurrentMapCenterLat, mapLoader.CurrentMapCenterLon, mapLoader.CurrentMapZoom);
        Rect rect = mapContent.rect;
        float scaleX = rect.width / 640f;
        float scaleY = rect.height / 640f;
        _targetCursorPos = ClampToMapBounds(new Vector2(logical.x * scaleX, logical.y * scaleY));
    }

    private void SmoothRecenterScroll()
    {
        if (scrollRect == null)
            return;

        float t = 1f - Mathf.Exp(-Mathf.Max(1f, followRecenteringSpeed) * Time.unscaledDeltaTime);
        scrollRect.horizontalNormalizedPosition = Mathf.Lerp(scrollRect.horizontalNormalizedPosition, 0.5f, t);
        scrollRect.verticalNormalizedPosition = Mathf.Lerp(scrollRect.verticalNormalizedPosition, 0.5f, t);
    }

    private void SnapScrollToCenter()
    {
        if (scrollRect == null)
            return;

        scrollRect.velocity = Vector2.zero;
        scrollRect.horizontalNormalizedPosition = 0.5f;
        scrollRect.verticalNormalizedPosition = 0.5f;
    }

    private static float DistanceMeters(float latA, float lonA, float latB, float lonB)
    {
        float dy = (latA - latB) * 111320f;
        float avgLat = (latA + latB) * 0.5f;
        float dx = (lonA - lonB) * (111320f * Mathf.Cos(avgLat * Mathf.Deg2Rad));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static Vector2 ProjectToMapLogicalPosition(float latitude, float longitude, float centerLat, float centerLon, int zoomLevel)
    {
        Vector2 point = LatLonToPixel(latitude, longitude, zoomLevel);
        Vector2 center = LatLonToPixel(centerLat, centerLon, zoomLevel);
        return new Vector2(point.x - center.x, center.y - point.y);
    }

    private static Vector2 LatLonToPixel(float latitude, float longitude, int zoomLevel)
    {
        float scale = 256f * Mathf.Pow(2f, zoomLevel);
        float x = (longitude + 180f) / 360f * scale;
        float sinLat = Mathf.Sin(latitude * Mathf.Deg2Rad);
        float y = (0.5f - Mathf.Log((1f + sinLat) / (1f - sinLat)) / (4f * Mathf.PI)) * scale;
        return new Vector2(x, y);
    }

    private Vector2 ClampToMapBounds(Vector2 position)
    {
        if (mapContent == null || userCursor == null)
            return position;

        Rect contentRect = mapContent.rect;
        Rect cursorRect   = userCursor.rect;

        float halfCursorW = cursorRect.width  * 0.5f;
        float halfCursorH = cursorRect.height * 0.5f;
        float pad         = cursorEdgePadding;

        float minX = contentRect.xMin + halfCursorW + pad;
        float maxX = contentRect.xMax - halfCursorW - pad;
        float minY = contentRect.yMin + halfCursorH + pad;
        float maxY = contentRect.yMax - halfCursorH - pad;

        Vector2 clamped = new Vector2(
            Mathf.Clamp(position.x, minX, maxX),
            Mathf.Clamp(position.y, minY, maxY));

        if (verboseLogs && clamped != position)
            Debug.Log($"[MainMapCursor] Clamped {position} → {clamped}  (bounds X[{minX:F0},{maxX:F0}] Y[{minY:F0},{maxY:F0}]  contentRect={contentRect.size}  cursorRect={cursorRect.size})");

        return clamped;
    }

    private static Transform FindByNameRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        if (root.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindByNameRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }
}
