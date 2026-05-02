using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class MapLabelSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject parkLabelPrefab;
    public GameObject neighbourhoodLabelPrefab;
    public GameObject stationLabelPrefab;
    public GameObject busStopLabelPrefab;
    public GameObject streetLabelPrefab;

    [Header("References")]
    public RectTransform mapParent;

    [Header("Settings")]
    public float searchRadiusMeters = 1500f;
    [Tooltip("Refetch labels if the player moves further than this from the last fetch point")]
    public float refetchThresholdMeters = 400f;
    [Tooltip("Labels closer than this (map canvas pixels) will be deduplicated — lower priority removed")]
    public float proximityThresholdPixels = 80f;

    [Header("Spawn Toggles")]
    public bool spawnParks          = true;
    public bool spawnNeighbourhoods = true;
    public bool spawnStations       = true;
    public bool spawnBusStops       = true;
    public bool spawnStreets        = false;

    private readonly string apiKey = "AIzaSyDenug-6RiFj3ziSxtdrYQnS-qo0WhuBfI";

    private struct LabelData
    {
        public GameObject obj;
        public int priority;
    }

    private readonly List<LabelData> spawnedLabels = new List<LabelData>();
    private Vector2 lastFetchLatLon;
    private bool isFetching = false;
    private bool hasFetched = false;

    private void OnEnable()
    {
        MapLoader.onStyleChanged += RefreshLabelColors;
    }

    private void OnDisable()
    {
        MapLoader.onStyleChanged -= RefreshLabelColors;
    }

    private void Start()
    {
        StartCoroutine(WaitAndFetch());
    }

    private void Update()
    {
        CounterScaleLabels();

        if (!hasFetched || isFetching) return;

        float lat = GPSManager.Instance.latitude;
        float lon = GPSManager.Instance.longitude;
        float dist = DistanceMeters(lat, lon, lastFetchLatLon.x, lastFetchLatLon.y);

        if (dist > refetchThresholdMeters)
            StartCoroutine(FetchAll());
    }

    private void CounterScaleLabels()
    {
        if (mapParent == null || spawnedLabels.Count == 0) return;
        float parentScale = mapParent.localScale.x;
        if (parentScale <= 0f) return;

        float inv        = 1f / parentScale;
        Vector3 s        = Vector3.one * inv;
        float counterRot = -mapParent.eulerAngles.z;

        foreach (var data in spawnedLabels)
        {
            if (data.obj == null) continue;
            data.obj.transform.localScale       = s;
            data.obj.transform.localEulerAngles = new Vector3(0f, 0f, counterRot);
        }
    }

    private IEnumerator WaitAndFetch()
    {
        while (GPSManager.Instance == null || GPSManager.Instance.latitude == 0f)
            yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(FetchAll());
    }

    private IEnumerator FetchAll()
    {
        isFetching = true;
        hasFetched = true;
        lastFetchLatLon = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);

        ClearLabels();

        if (spawnParks)          yield return StartCoroutine(FetchPlacesByType(parkLabelPrefab,      priority: 1, "park"));
        if (spawnBusStops)       yield return StartCoroutine(FetchPlacesByType(busStopLabelPrefab,   priority: 2, "bus_station"));
        if (spawnStations)       yield return StartCoroutine(FetchPlacesByType(stationLabelPrefab,   priority: 3, "subway_station", "train_station"));
        if (spawnNeighbourhoods) yield return StartCoroutine(FetchNeighbourhoods());
        if (spawnStreets)        yield return StartCoroutine(FetchStreets());

        yield return null;
        Canvas.ForceUpdateCanvases();
        DeduplicateByProximity();

        isFetching = false;
    }

    // ── Places (generic) ───────────────────────────────────────────────────

    private IEnumerator FetchPlacesByType(GameObject prefab, int priority, params string[] types)
    {
        if (prefab == null) yield break;

        float lat = lastFetchLatLon.x;
        float lon = lastFetchLatLon.y;
        HashSet<string> seen = new HashSet<string>();

        foreach (string type in types)
        {
            string url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                         $"?location={lat},{lon}&radius={searchRadiusMeters}&type={type}&key={apiKey}";

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[MapLabels] Places fetch failed ({type}): {req.error}");
                    continue;
                }

                PlacesResponse response = JsonUtility.FromJson<PlacesResponse>(req.downloadHandler.text);
                if (response == null) continue;

                if (response.status != "OK" && response.status != "ZERO_RESULTS")
                {
                    Debug.LogWarning($"[MapLabels] Places API error ({type}): {response.status}");
                    continue;
                }

                if (response.results == null) continue;

                foreach (var place in response.results)
                {
                    if (seen.Contains(place.name)) continue;
                    seen.Add(place.name);
                    SpawnLabel(prefab, priority, place.name, place.geometry.location.lat, place.geometry.location.lng);
                }
            }
        }
    }

    // ── Streets ────────────────────────────────────────────────────────────

    private IEnumerator FetchStreets()
    {
        if (streetLabelPrefab == null) yield break;

        float playerLat = lastFetchLatLon.x;
        float playerLon = lastFetchLatLon.y;
        float s = 0.003f; // ~330 m

        Vector2[] offsets =
        {
            Vector2.zero,
            new Vector2( s,  0f), new Vector2(-s,  0f),
            new Vector2( 0f,  s), new Vector2( 0f, -s),
        };

        var roadHits = new Dictionary<string, List<(float sLat, float sLon, float vpRot)>>();

        foreach (var offset in offsets)
        {
            float sLat = playerLat + offset.x;
            float sLon = playerLon + offset.y;

            string url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                         $"?latlng={sLat},{sLon}&result_type=route&key={apiKey}";

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) continue;

                GeocodeResponse resp = JsonUtility.FromJson<GeocodeResponse>(req.downloadHandler.text);
                if (resp?.results == null || resp.results.Length == 0) continue;

                var result = resp.results[0];
                if (result.address_components == null) continue;

                string name = null;
                foreach (var comp in result.address_components)
                    if (comp.types != null && System.Array.IndexOf(comp.types, "route") >= 0)
                    { name = comp.long_name; break; }

                if (string.IsNullOrEmpty(name)) continue;

                float vpRot = 0f;
                var vp = result.geometry?.viewport;
                if (vp != null)
                {
                    float cosLat = Mathf.Cos(sLat * Mathf.Deg2Rad);
                    vpRot = Mathf.Atan2(
                        vp.northeast.lat - vp.southwest.lat,
                        (vp.northeast.lng - vp.southwest.lng) * cosLat
                    ) * Mathf.Rad2Deg;
                    if (vpRot >  90f) vpRot -= 180f;
                    if (vpRot < -90f) vpRot += 180f;
                }

                if (!roadHits.ContainsKey(name))
                    roadHits[name] = new List<(float, float, float)>();
                roadHits[name].Add((sLat, sLon, vpRot));
            }
        }

        foreach (var kvp in roadHits)
        {
            string name    = kvp.Key;
            var    hits    = kvp.Value;
            float  lat     = hits[0].sLat;
            float  lon     = hits[0].sLon;
            float  cosLat  = Mathf.Cos(lat * Mathf.Deg2Rad);
            float  rotation = hits[0].vpRot;

            if (hits.Count >= 2)
            {
                float dLat = hits[1].sLat - hits[0].sLat;
                float dLon = (hits[1].sLon - hits[0].sLon) * cosLat;
                if (dLat * dLat + dLon * dLon > 1e-12f)
                {
                    rotation = Mathf.Atan2(dLat, dLon) * Mathf.Rad2Deg;
                    if (rotation >  90f) rotation -= 180f;
                    if (rotation < -90f) rotation += 180f;
                }
            }

            SpawnLabel(streetLabelPrefab, priority: 1, name, lat, lon, rotation);
        }
    }

    // ── Neighbourhoods ─────────────────────────────────────────────────────

    private IEnumerator FetchNeighbourhoods()
    {
        if (neighbourhoodLabelPrefab == null) yield break;

        float playerLat = lastFetchLatLon.x;
        float playerLon = lastFetchLatLon.y;

        float s = 0.006f;
        Vector2[] offsets = new Vector2[]
        {
            new Vector2( 0,    0   ),
            new Vector2( s,    0   ),
            new Vector2(-s,    0   ),
            new Vector2( 0,    s   ),
            new Vector2( 0,   -s   ),
            new Vector2( s,    s   ),
            new Vector2(-s,   -s   ),
            new Vector2( s,   -s   ),
            new Vector2(-s,    s   ),
        };

        HashSet<string> seen = new HashSet<string>();

        foreach (var offset in offsets)
        {
            float sampleLat = playerLat + offset.x;
            float sampleLon = playerLon + offset.y;

            string url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                         $"?latlng={sampleLat},{sampleLon}&result_type=neighborhood|sublocality&key={apiKey}";

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success) continue;

                GeocodeResponse response = JsonUtility.FromJson<GeocodeResponse>(req.downloadHandler.text);
                if (response?.results == null || response.results.Length == 0) continue;

                string name = response.results[0].address_components[0].long_name;

                if (!seen.Contains(name))
                {
                    seen.Add(name);
                    SpawnLabel(neighbourhoodLabelPrefab, priority: 4, name, sampleLat, sampleLon);
                }
            }
        }
    }

    // ── Deduplication ──────────────────────────────────────────────────────

    private void DeduplicateByProximity()
    {
        var toDestroy = new List<GameObject>();
        float threshold = Mathf.Max(1f, proximityThresholdPixels);

        Debug.Log($"[MapLabels] Dedup: {spawnedLabels.Count} labels, threshold={threshold}px");

        for (int i = 0; i < spawnedLabels.Count; i++)
        {
            if (spawnedLabels[i].obj == null) continue;
            RectTransform rtI = spawnedLabels[i].obj.GetComponent<RectTransform>();
            if (rtI == null) continue;

            for (int j = 0; j < spawnedLabels.Count; j++)
            {
                if (i == j || spawnedLabels[j].obj == null) continue;
                if (spawnedLabels[j].priority <= spawnedLabels[i].priority) continue;
                RectTransform rtJ = spawnedLabels[j].obj.GetComponent<RectTransform>();
                if (rtJ == null) continue;

                float dist = Vector2.Distance(rtI.anchoredPosition, rtJ.anchoredPosition);
                if (dist < threshold)
                {
                    Debug.Log($"[MapLabels] Removing '{spawnedLabels[i].obj.name}' (pri={spawnedLabels[i].priority}) near '{spawnedLabels[j].obj.name}' (pri={spawnedLabels[j].priority}), dist={dist:F1}");
                    toDestroy.Add(spawnedLabels[i].obj);
                    break;
                }
            }
        }

        foreach (var obj in toDestroy)
        {
            spawnedLabels.RemoveAll(d => d.obj == obj);
            Destroy(obj);
        }

        Debug.Log($"[MapLabels] Dedup removed {toDestroy.Count}, {spawnedLabels.Count} remain");
    }

    // ── Shared ─────────────────────────────────────────────────────────────

    private void SpawnLabel(GameObject prefab, int priority, string labelText, float lat, float lon, float rotation = 0f)
    {
        Vector2 pos = GPSToMapPosition(lat, lon);

        GameObject obj = Instantiate(prefab, mapParent);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.localEulerAngles = new Vector3(0f, 0f, rotation);

        TextMeshProUGUI tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text  = labelText;
            tmp.color = CurrentLabelColor();
        }

        spawnedLabels.Add(new LabelData { obj = obj, priority = priority });
    }

    private void RefreshLabelColors()
    {
        Color c = CurrentLabelColor();
        foreach (var data in spawnedLabels)
        {
            if (data.obj == null) continue;
            TextMeshProUGUI tmp = data.obj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.color = c;
        }
    }

    private Color CurrentLabelColor()
    {
        var styles = MapLoader.instance?.mapStyles;
        if (styles == null || styles.Count == 0) return Color.white;
        int idx = Mathf.Clamp(MapLoader.instance.currentStyleIndex, 0, styles.Count - 1);
        return styles[idx].labelColor;
    }

    private Vector2 GPSToMapPosition(float lat, float lon)
    {
        float playerLat = GPSManager.Instance.latitude;
        float playerLon = GPSManager.Instance.longitude;
        float mapScale = 6f * Mathf.Pow(2f, MapLoader.instance.zoom - 14f);

        float x = (lon - playerLon) * (111320f * Mathf.Cos(playerLat * Mathf.Deg2Rad)) * mapScale;
        float y = (lat - playerLat) * 111320f * mapScale;

        return new Vector2(x, y);
    }

    private void ClearLabels()
    {
        foreach (var data in spawnedLabels)
            if (data.obj != null) Destroy(data.obj);
        spawnedLabels.Clear();
    }

    private float DistanceMeters(float lat1, float lon1, float lat2, float lon2)
    {
        float dy = (lat1 - lat2) * 111320f;
        float dx = (lon1 - lon2) * (111320f * Mathf.Cos(lat1 * Mathf.Deg2Rad));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ── JSON types ─────────────────────────────────────────────────────────

    [System.Serializable] class PlacesResponse  { public string status; public PlaceResult[] results; }
    [System.Serializable] class PlaceResult     { public string name; public PlaceGeometry geometry; }
    [System.Serializable] class PlaceGeometry   { public LatLon location; }
    [System.Serializable] class LatLon          { public float lat; public float lng; }

    [System.Serializable] class GeocodeResponse   { public GeocodeResult[]  results; }
    [System.Serializable] class GeocodeResult    { public AddressComponent[] address_components; public GeocodeGeometry geometry; }
    [System.Serializable] class GeocodeGeometry  { public LatLon location; public GeocodeViewport viewport; }
    [System.Serializable] class GeocodeViewport  { public LatLon northeast; public LatLon southwest; }
    [System.Serializable] class AddressComponent { public string long_name; public string[] types; }
}
