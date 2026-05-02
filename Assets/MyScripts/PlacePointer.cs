using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Place on any POI prefab spawned by PlacesFetcher.
/// Positions the pin on the map at its GPS coordinates and counter-scales/rotates
/// against the parent mapContent so it always faces up at a consistent visual size.
/// </summary>
public class PlacePointer : MonoBehaviour
{
    [HideInInspector] public float latitude;
    [HideInInspector] public float longitude;
    [HideInInspector] public RectTransform mapTransform;

    public TextMeshProUGUI label;

    [Header("Map Style Sprites")]
    [Tooltip("One sprite per map style, matched by index to MapLoader.mapStyles")]
    public Sprite[] styleSprites;
    public Image labelImage;

    [Header("Scaling")]
    [Tooltip("When true the pointer scales with the map instead of staying a fixed screen size")]
    public bool scaleWithMap = false;

    private RectTransform _rt;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        MapLoader.onMainMapReloadStateChanged += OnReloadStateChanged;
        RefreshSprite();
    }

    private void OnDisable()
    {
        MapLoader.onMainMapReloadStateChanged -= OnReloadStateChanged;
    }

    private void OnReloadStateChanged(bool reloading)
    {
        if (!reloading) RefreshSprite();
    }

    private void Update()
    {
        ApplyCounterTransform();
    }

    public void UpdatePosition()
    {
        if (latitude == 0f && longitude == 0f) return;
        if (_rt == null) _rt = GetComponent<RectTransform>();

        float centerLat = GPSManager.Instance.latitude;
        float centerLon = GPSManager.Instance.longitude;
        if (centerLat == 0f && centerLon == 0f) return;

        float latM  = (latitude  - centerLat) * 111320f;
        float lonM  = (longitude - centerLon) * (111320f * Mathf.Cos(centerLat * Mathf.Deg2Rad));
        float scale = 6f * Mathf.Pow(2f, MapLoader.instance.zoom - 14f);

        _rt.anchoredPosition = new Vector2(lonM * scale, latM * scale);
    }

    private void RefreshSprite()
    {
        if (labelImage == null || styleSprites == null || styleSprites.Length == 0) return;
        if (MapLoader.instance == null) return;

        int idx = Mathf.Clamp(MapLoader.instance.currentStyleIndex, 0, styleSprites.Length - 1);
        if (styleSprites[idx] != null)
            labelImage.sprite = styleSprites[idx];
    }

    private void ApplyCounterTransform()
    {
        Transform p = transform.parent;
        if (p == null) return;

        if (!scaleWithMap)
        {
            float parentScale = p.localScale.x;
            if (parentScale > 0f)
                transform.localScale = Vector3.one / parentScale;
        }

        transform.localEulerAngles = new Vector3(0f, 0f, -p.eulerAngles.z);
    }
}
