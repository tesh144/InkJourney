using System.Collections;
using TMPro;
using UnityEngine;

public class CollectibleInk : MonoBehaviour
{
    public float Latitude  { get; private set; }
    public float Longitude { get; private set; }

    public TMP_Text rewardLabel;

    RectTransform _rt;
    RectTransform _inkCounterTarget;
    float         _collectRadiusMetres;
    int           _inkAmount;
    bool          _collected;

    void Awake() => _rt = GetComponent<RectTransform>();

    public void Initialise(float lat, float lon, RectTransform inkTarget, float collectRadius, int minInk = 5, int maxInk = 15)
    {
        Latitude             = lat;
        Longitude            = lon;
        _inkCounterTarget    = inkTarget;
        _collectRadiusMetres = collectRadius;
        _inkAmount           = Random.Range(minInk, Mathf.Max(maxInk, minInk) + 1);
        if (rewardLabel != null) rewardLabel.text = $"+{_inkAmount}";
        RelocateIfNeeded();
        UpdatePosition();
    }

    void RelocateIfNeeded()
    {
        var fetcher  = GoogleSheetsFetcher.instance;
        if (fetcher == null) return;

        float threshold = CollectibleInkSpawner.instance != null
            ? CollectibleInkSpawner.instance.minSpacingMetres
            : 100f;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (!IsNearAnyObstacle(fetcher, threshold)) return;

            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float km    = threshold / 1000f;
            Latitude  += km / 111.0f * Mathf.Cos(angle);
            Longitude += km / (111.0f * Mathf.Cos(Latitude * Mathf.Deg2Rad)) * Mathf.Sin(angle);
        }
    }

    bool IsNearAnyObstacle(GoogleSheetsFetcher fetcher, float thresholdMetres)
    {
        foreach (var e in fetcher.storiesList)
            if (e != null && DistMetres(Latitude, Longitude, e.Latitude, e.Longitude) < thresholdMetres)
                return true;
        foreach (var e in fetcher.landmarksList)
            if (e != null && DistMetres(Latitude, Longitude, e.Latitude, e.Longitude) < thresholdMetres)
                return true;

        var spawner = MapLabelSpawner.instance;
        if (spawner != null)
            foreach (var pos in spawner.GetSpawnedLatLons())
                if (DistMetres(Latitude, Longitude, pos.x, pos.y) < thresholdMetres)
                    return true;

        return false;
    }

    static float DistMetres(float lat1, float lon1, float lat2, float lon2)
    {
        float dy = (lat1 - lat2) * 111320f;
        float dx = (lon1 - lon2) * 111320f * Mathf.Cos(lat1 * Mathf.Deg2Rad);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    void Update()
    {
        if (_collected) return;
        UpdatePosition();
        ApplyCounterTransform();
        CheckProximity();
    }

    void UpdatePosition()
    {
        if (MapLoader.instance == null || GPSManager.Instance == null) return;

        float centerLat = GPSManager.Instance.latitude;
        float centerLon = GPSManager.Instance.longitude;
        if (centerLat == 0f) return;

        int     zoom  = MapLoader.instance.CurrentMapZoom;
        Vector2 p     = MapLoader.instance.LatLonToPixel(Latitude,  Longitude,  zoom);
        Vector2 c     = MapLoader.instance.LatLonToPixel(centerLat, centerLon,  zoom);
        float   w     = _rt.parent is RectTransform pr ? pr.rect.width : 640f;

        _rt.anchoredPosition = new Vector2((p.x - c.x) * (w / 320f), (c.y - p.y) * (w / 320f));
    }

    void ApplyCounterTransform()
    {
        Transform p = transform.parent;
        if (p == null) return;
        float s = p.localScale.x;
        if (s > 0f) transform.localScale = Vector3.one / s;
        transform.localEulerAngles = new Vector3(0f, 0f, -p.eulerAngles.z);
    }

    void CheckProximity()
    {
        if (GPSManager.Instance == null) return;
        float dy   = (Latitude  - GPSManager.Instance.latitude)  * 111320f;
        float dx   = (Longitude - GPSManager.Instance.longitude) * 111320f * Mathf.Cos(Latitude * Mathf.Deg2Rad);
        float dist = Mathf.Sqrt(dx * dx + dy * dy);
        if (dist <= _collectRadiusMetres)
            StartCoroutine(Collect());
    }

    IEnumerator Collect()
    {
        _collected = true;
        CollectibleInkSpawner.instance?.OnCollected(this);

        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (rootCanvas == null) { Destroy(gameObject); yield break; }

        // Reparent to root canvas so it renders above everything
        Vector3 worldPos = transform.position;
        transform.SetParent(rootCanvas.transform, worldPositionStays: true);
        transform.position = worldPos;
        transform.SetAsLastSibling();
        transform.localScale = Vector3.one;

        // Fly to ink counter
        Vector3 startPos = transform.position;
        float   t        = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            Vector3 target = _inkCounterTarget != null ? _inkCounterTarget.position : startPos;
            transform.position   = Vector3.LerpUnclamped(startPos, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(t));
            yield return null;
        }

        InkManager.instance?.AddInk(_inkAmount);
        Destroy(gameObject);
    }
}
