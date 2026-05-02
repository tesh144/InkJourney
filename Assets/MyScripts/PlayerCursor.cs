using UnityEngine;

/// <summary>
/// Place on the player-cursor GameObject inside mapContent.
/// Tracks the player's real-time GPS position as an offset from the map centre,
/// counter-scales/rotates against the map (same pattern as MapPointer) so the
/// cursor stays the same visual size and faces up at all times.
///
/// Wire up:
///   mapContent   — the RectTransform that MapInputController zooms/rotates
///   smoothing    — how quickly the cursor follows the GPS signal (GPS is noisy)
///
/// The heading arrow is handled separately by PlayerCompass on a child object.
/// </summary>
public class PlayerCursor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The RectTransform that MapInputController scales and rotates")]
    public RectTransform mapContent;

    [Header("Smoothing")]
    [Tooltip("How quickly the cursor lerps toward the live GPS position. "
           + "Lower = smoother but laggier; higher = more responsive but jittery.")]
    public float smoothing = 6f;

    private RectTransform _rt;
    private Vector2       _targetPos;
    private bool          _hasTarget;
    private Vector2       _baseSizeDelta;
    private float         _baseWorldScale = 1f;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        if (_rt != null)
            _baseSizeDelta = _rt.sizeDelta;

        _baseWorldScale = transform.lossyScale.x;
        if (_baseWorldScale <= 0f)
            _baseWorldScale = 1f;
    }

    private void OnEnable()
    {
        // Ensure location services and compass are running.
        // GPSManager keeps them alive; this is a safety net for cold-starts.
        Input.compass.enabled = true;
        if (Input.location.status == LocationServiceStatus.Stopped
            && (LocationPermissionScreen.instance == null || LocationPermissionScreen.LocationGranted))
            Input.location.Start(desiredAccuracyInMeters: 5f, updateDistanceInMeters: 1f);
    }

    private void Update()
    {
        UpdatePosition();
        UpdateTransform();
    }

    // ── Position ─────────────────────────────────────────────────────────────

    private void UpdatePosition()
    {
        if (GPSManager.Instance == null) return;

        float mapCentreLat = GPSManager.Instance.latitude;
        float mapCentreLon = GPSManager.Instance.longitude;

        // Map is reloading — GPSManager zeroes coords; hold last known position.
        if (mapCentreLat == 0f && mapCentreLon == 0f) return;

        float liveLat, liveLon;

#if UNITY_EDITOR
        // In-editor the cursor sits at the map centre (no real movement).
        liveLat = mapCentreLat;
        liveLon = mapCentreLon;
#else
        if (Input.location.status != LocationServiceStatus.Running) return;
        liveLat = Input.location.lastData.latitude;
        liveLon = Input.location.lastData.longitude;
#endif

        // Same pixel-to-GPS scale MapPointer uses.
        float scale = 6f * Mathf.Pow(2f, MapLoader.instance.zoom - 14f);
        float latM  = (liveLat - mapCentreLat) * 111320f;
        float lonM  = (liveLon - mapCentreLon)
                    * (111320f * Mathf.Cos(mapCentreLat * Mathf.Deg2Rad));

        _targetPos = new Vector2(lonM * scale, latM * scale);
        _hasTarget = true;
    }

    // ── Transform ────────────────────────────────────────────────────────────

    private void UpdateTransform()
    {
        // Smooth position toward live GPS target.
        if (_hasTarget)
            _rt.anchoredPosition = Vector2.Lerp(
                _rt.anchoredPosition, _targetPos, smoothing * Time.deltaTime);

        if (mapContent == null) return;

        // Keep cursor rect dimensions fixed if any UI layout component tries to resize it.
        if (_rt != null)
            _rt.sizeDelta = _baseSizeDelta;

        // Counter-scale against full world parent scale so the cursor stays a fixed screen size.
        float parentWorldScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        if (parentWorldScale > 0f)
            transform.localScale = Vector3.one * (_baseWorldScale / parentWorldScale);

        // Counter-rotate so the cursor always faces screen-up (north-up),
        // matching MapPointer's behaviour.
        transform.localEulerAngles = new Vector3(0f, 0f, -mapContent.eulerAngles.z);
    }
}
