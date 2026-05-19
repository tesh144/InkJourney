using System.Collections;
using TMPro;
using UnityEngine;

public class CollectibleGoldenQuill : MonoBehaviour
{
    public float Latitude  { get; private set; }
    public float Longitude { get; private set; }

    RectTransform _rt;
    RectTransform _quillCounterTarget;
    float         _collectRadiusMetres;
    bool          _collected;

    public TMP_Text rewardLabel;

    void Awake() => _rt = GetComponent<RectTransform>();

    public void Initialise(float lat, float lon, RectTransform counterTarget, float collectRadius)
    {
        Latitude             = lat;
        Longitude            = lon;
        _quillCounterTarget  = counterTarget;
        _collectRadiusMetres = collectRadius;
        if (rewardLabel != null) rewardLabel.text = "+1";
        UpdatePosition();
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

        int     zoom = MapLoader.instance.CurrentMapZoom;
        Vector2 p    = MapLoader.instance.LatLonToPixel(Latitude,  Longitude,  zoom);
        Vector2 c    = MapLoader.instance.LatLonToPixel(centerLat, centerLon,  zoom);
        float   w    = _rt.parent is RectTransform pr ? pr.rect.width : 640f;

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
        if (Mathf.Sqrt(dx * dx + dy * dy) <= _collectRadiusMetres)
            StartCoroutine(Collect());
    }

    IEnumerator Collect()
    {
        _collected = true;
        CollectibleInkSpawner.instance?.OnQuillCollected();

        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (rootCanvas == null) { Destroy(gameObject); yield break; }

        Vector3 worldPos = transform.position;
        transform.SetParent(rootCanvas.transform, worldPositionStays: true);
        transform.position = worldPos;
        transform.SetAsLastSibling();
        transform.localScale = Vector3.one;

        Vector3 startPos = transform.position;
        float   t        = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            Vector3 target = _quillCounterTarget != null ? _quillCounterTarget.position : startPos;
            transform.position   = Vector3.LerpUnclamped(startPos, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(t));
            yield return null;
        }

        GoldenQuillManager.instance?.AddQuill(1);
        Destroy(gameObject);
    }
}
