using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Out-of-range map pointer popup.
/// Place on a panel in the canvas, wire up the fields in the Inspector, then
/// style freely. Show/Hide are driven by MapPointer.OnTapped().
/// </summary>
public class MapPointerPopup : MonoBehaviour
{
    public static MapPointerPopup instance;

    [Header("UI References")]
    public TMP_Text     titleText;
    public TMP_Text     previewText;
    public TMP_Text     locationText;
    public TMP_Text     distanceText;
    public StickerDisplay stickerDisplay;
    public Button       openMapsButton;
    public Button       closeButton;

    [Header("Preview Pointer")]
    [Tooltip("A MapPointer placed inside this popup (staticMode must be ON). " +
             "It will be bound to the tapped entry and scaled to match the tapped pointer's world size.")]
    public MapPointer   previewPointer;


    private float      _targetLat;
    private float      _targetLon;
    private MapPointer _trackedPointer;
    private Canvas        _rootCanvas;
    private RectTransform _previewParentRT;
    private RectTransform _previewRT;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        instance = this;
        gameObject.SetActive(false);

        if (openMapsButton != null) openMapsButton.onClick.AddListener(OpenInMaps);
        if (closeButton    != null) closeButton.onClick.AddListener(Hide);

        _rootCanvas      = GetComponentInParent<Canvas>();
        _previewParentRT = previewPointer != null
            ? previewPointer.transform.parent as RectTransform : null;
        _previewRT       = previewPointer != null
            ? previewPointer.GetComponent<RectTransform>() : null;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void Show(MapPointer pointer)
    {
        if (pointer == null) return;
        var e = pointer.entry;

        _targetLat = pointer.latitude;
        _targetLon = pointer.longitude;

        if (titleText != null)
        {
            titleText.text = e?.Title ?? "";
            var font = FontManager.instance?.GetFont(e?.FontID ?? 0);
            if (font != null) titleText.font = font;
        }
        if (previewText != null) previewText.text  = e?.Content ?? "";
        if (locationText != null)
            locationText.text = !string.IsNullOrEmpty(e?.cachedLocation)
                ? e.cachedLocation : "";

        if (distanceText != null && GPSManager.Instance != null && e != null)
        {
            float dy = (_targetLat - GPSManager.Instance.latitude)  * 111320f;
            float dx = (_targetLon - GPSManager.Instance.longitude) *
                       (111320f * Mathf.Cos(_targetLat * Mathf.Deg2Rad));
            int metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
            distanceText.text = metres > 999
                ? $"{Mathf.RoundToInt(metres / 1000f)}km away"
                : $"{metres}m away";
        }

        if (stickerDisplay != null && e != null)
            stickerDisplay.ShowSticker(e.StickerID);

        if (previewPointer != null)
        {
            // Enforce static mode — no GPS / expansion logic should run inside the popup
            previewPointer.staticMode = true;

            // Bind the same entry so icon, name, sticker etc. all match
            previewPointer.BindEntry(pointer.entry);

            // Always show the route-attached colour state
            previewPointer.ApplyRouteColors();

            // Match the source pointer's backPanel local position (animator-driven)
            if (pointer.backPanel != null && previewPointer.backPanel != null)
                previewPointer.backPanel.transform.localPosition =
                    pointer.backPanel.transform.localPosition;

            _trackedPointer = pointer;

            // Snap position immediately so there is no visible slide-in as the map animates.
            if (_previewParentRT != null && _previewRT != null)
            {
                Camera uiCam = (_rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? _rootCanvas.worldCamera : null;
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, pointer.transform.position);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _previewParentRT, screenPos, uiCam, out Vector2 localPos))
                    _previewRT.anchoredPosition = localPos;
            }
        }

        MapInputController.instance?.SnapToMaxZoom();
        MapInputController.instance?.SetMapInteractionEnabled(false);
        gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (previewPointer == null || _trackedPointer == null || _previewParentRT == null) return;

        Camera uiCam = (_rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _rootCanvas.worldCamera : null;

        // Track position
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(
            uiCam, _trackedPointer.transform.position);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _previewParentRT, screenPos, uiCam, out Vector2 localPos))
            _previewRT.anchoredPosition = localPos;

        // Track scale — keeps preview in sync during pinch-zoom
        float sourceScale  = _trackedPointer.transform.lossyScale.x;
        float parentScale  = previewPointer.transform.parent != null
            ? previewPointer.transform.parent.lossyScale.x : 1f;
        if (parentScale > 0f)
            previewPointer.transform.localScale = Vector3.one * (sourceScale / parentScale);

        // Track backPanel animation state
        if (_trackedPointer.backPanel != null && previewPointer.backPanel != null)
            previewPointer.backPanel.transform.localPosition =
                _trackedPointer.backPanel.transform.localPosition;
    }

    public void Hide()
    {
        MapInputController.instance?.SetMapInteractionEnabled(true);
        gameObject.SetActive(false);
        _trackedPointer = null;
    }

    // ── Maps deep-link ─────────────────────────────────────────────────────

    public void OpenInMaps() => OpenInMaps(_targetLat, _targetLon);

    public static void OpenInMaps(float lat, float lon)
    {
        string sLat = lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        string sLon = lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

#if UNITY_IOS
        Application.OpenURL($"maps://?daddr={sLat},{sLon}&dirflg=w");
#elif UNITY_ANDROID
        Application.OpenURL($"geo:{sLat},{sLon}?q={sLat},{sLon}");
#else
        Application.OpenURL($"https://maps.apple.com/?daddr={sLat},{sLon}&dirflg=w");
#endif
    }
}
