using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    public GameObject museumLabelPrefab;
    public GameObject libraryLabelPrefab;
    public GameObject worshipLabelPrefab;
    public GameObject bookstoreLabelPrefab;
    public GameObject cafeLabelPrefab;
    public GameObject movieTheatreLabelPrefab;

    [Header("References")]
    public RectTransform mapParent;

    [Header("Settings")]
    public float searchRadiusMeters   = 1500f;
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
    public bool spawnMuseums        = true;
    public bool spawnLibraries      = true;
    public bool spawnWorship        = true;
    public bool spawnBookstores     = true;
    public bool spawnCafes          = true;
    public bool spawnMovieTheatres  = true;

    private struct LabelData { public GameObject obj; public int priority; }

    private readonly List<LabelData> spawnedLabels = new List<LabelData>();
    private Vector2 lastFetchLatLon;
    public static MapLabelSpawner instance;

    private bool isFetching = false;
    private bool hasFetched = false;
    public bool LabelsReady => hasFetched && !isFetching;

    public HashSet<string> NearbyPlaceTypes { get; } = new HashSet<string>();

    private void Awake() => instance = this;

    private void OnEnable()  => MapLoader.onStyleChanged += RefreshLabelColors;
    private void OnDisable() => MapLoader.onStyleChanged -= RefreshLabelColors;

    private void Start() => StartCoroutine(WaitAndFetch());

    private void Update()
    {
        CounterScaleLabels();

        if (!hasFetched || isFetching) return;

        float lat  = GPSManager.Instance.latitude;
        float lon  = GPSManager.Instance.longitude;
        float dist = DistanceMeters(lat, lon, lastFetchLatLon.x, lastFetchLatLon.y);

        if (dist > refetchThresholdMeters)
            StartCoroutine(FetchAll());
    }

    private void CounterScaleLabels()
    {
        if (mapParent == null || spawnedLabels.Count == 0) return;
        float parentScale = mapParent.localScale.x;
        if (parentScale <= 0f) return;

        float    inv        = 1f / parentScale;
        Vector3  s          = Vector3.one * inv;
        float    counterRot = -mapParent.eulerAngles.z;

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
        isFetching      = true;
        hasFetched      = true;
        lastFetchLatLon = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);

        ClearLabels();

        yield return StartCoroutine(FetchPOILabels());
        if (spawnStreets) yield return StartCoroutine(FetchStreets());

        yield return null;
        Canvas.ForceUpdateCanvases();
        DeduplicateByProximity();
        SortLabelsByPriority();

        isFetching = false;
    }

    // ── POI (single Overpass query for all types) ──────────────────────────

    private IEnumerator FetchPOILabels()
    {
        float lat = lastFetchLatLon.x;
        float lon = lastFetchLatLon.y;
        float r   = searchRadiusMeters;
        float nr  = r * 2f; // wider radius for neighbourhood labels

        var q = new StringBuilder("[out:json][timeout:30];(");

        if (spawnParks)
        {
            q.Append($"node[\"leisure\"=\"park\"](around:{r:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"leisure\"=\"park\"](around:{r:F0},{lat:F6},{lon:F6});");
        }
        if (spawnBusStops)
            q.Append($"node[\"highway\"=\"bus_stop\"](around:{r:F0},{lat:F6},{lon:F6});");

        if (spawnStations)
            q.Append($"node[\"railway\"~\"station|halt|tram_stop\"](around:{r:F0},{lat:F6},{lon:F6});");

        if (spawnMuseums)
        {
            q.Append($"node[\"tourism\"=\"museum\"](around:{r:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"tourism\"=\"museum\"](around:{r:F0},{lat:F6},{lon:F6});");
        }
        if (spawnLibraries)
        {
            q.Append($"node[\"amenity\"=\"library\"](around:{r:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"amenity\"=\"library\"](around:{r:F0},{lat:F6},{lon:F6});");
        }
        if (spawnWorship)
        {
            q.Append($"node[\"amenity\"=\"place_of_worship\"](around:{r:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"amenity\"=\"place_of_worship\"](around:{r:F0},{lat:F6},{lon:F6});");
        }
        if (spawnBookstores)
            q.Append($"node[\"shop\"=\"books\"](around:{r:F0},{lat:F6},{lon:F6});");

        if (spawnCafes)
            q.Append($"node[\"amenity\"=\"cafe\"](around:{r:F0},{lat:F6},{lon:F6});");

        if (spawnMovieTheatres)
        {
            q.Append($"node[\"amenity\"=\"cinema\"](around:{r:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"amenity\"=\"cinema\"](around:{r:F0},{lat:F6},{lon:F6});");
        }
        if (spawnNeighbourhoods)
        {
            q.Append($"node[\"place\"~\"neighbourhood|suburb\"](around:{nr:F0},{lat:F6},{lon:F6});");
            q.Append($"way[\"place\"~\"neighbourhood|suburb\"](around:{nr:F0},{lat:F6},{lon:F6});");
        }

        q.Append(");out center tags;");

        string url = "https://overpass-api.de/api/interpreter?data=" + Uri.EscapeDataString(q.ToString());

        using var req = UnityWebRequest.Get(url);
        req.timeout = 35;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[MapLabels] Overpass POI fetch failed: {req.error}");
            yield break;
        }

        var response = JsonUtility.FromJson<OverpassResponse>(req.downloadHandler.text);
        if (response?.elements == null) yield break;

        var seen = new HashSet<string>();
        NearbyPlaceTypes.Clear();

        foreach (var el in response.elements)
        {
            if (el.tags == null) continue;
            string name = el.tags.name;
            if (string.IsNullOrEmpty(name) || seen.Contains(name)) continue;

            float elLat = el.type == "node" ? el.lat : el.center.lat;
            float elLon = el.type == "node" ? el.lon : el.center.lon;

            var (prefab, priority, placeType) = ClassifyElement(el.tags);
            if (prefab == null) continue;

            seen.Add(name);
            if (!string.IsNullOrEmpty(placeType)) NearbyPlaceTypes.Add(placeType);
            SpawnLabel(prefab, priority, name, elLat, elLon);
        }
    }

    private (GameObject prefab, int priority, string placeType) ClassifyElement(OverpassTags t)
    {
        if (spawnParks         && t.leisure  == "park")                                                            return (parkLabelPrefab,         1, "park");
        if (spawnBusStops      && t.highway  == "bus_stop")                                                        return (busStopLabelPrefab,       2, "bus_stop");
        if (spawnStations      && (t.railway == "station" || t.railway == "halt" || t.railway == "tram_stop"))     return (stationLabelPrefab,       3, "train_station");
        if (spawnMuseums       && t.tourism  == "museum")                                                          return (museumLabelPrefab,        2, "museum");
        if (spawnLibraries     && t.amenity  == "library")                                                         return (libraryLabelPrefab,       2, "library");
        if (spawnWorship       && t.amenity  == "place_of_worship")                                                return (worshipLabelPrefab,       1, "place_of_worship");
        if (spawnBookstores    && t.shop     == "books")                                                            return (bookstoreLabelPrefab,     1, "book_store");
        if (spawnCafes         && t.amenity  == "cafe")                                                            return (cafeLabelPrefab,          1, "cafe");
        if (spawnMovieTheatres && t.amenity  == "cinema")                                                          return (movieTheatreLabelPrefab,  1, "movie_theater");
        if (spawnNeighbourhoods && (t.place  == "neighbourhood" || t.place == "suburb"))                           return (neighbourhoodLabelPrefab, 4, null);
        return (null, 0, null);
    }

    // ── Streets (Overpass way geometry) ───────────────────────────────────

    private IEnumerator FetchStreets()
    {
        if (streetLabelPrefab == null) yield break;

        float lat = lastFetchLatLon.x;
        float lon = lastFetchLatLon.y;

        string query = $"[out:json][timeout:15];way[\"highway\"][\"name\"](around:500,{lat:F6},{lon:F6});out geom tags;";
        string url   = "https://overpass-api.de/api/interpreter?data=" + Uri.EscapeDataString(query);

        using var req = UnityWebRequest.Get(url);
        req.timeout = 20;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) yield break;

        var response = JsonUtility.FromJson<OverpassGeomResponse>(req.downloadHandler.text);
        if (response?.elements == null) yield break;

        var seen = new HashSet<string>();

        foreach (var way in response.elements)
        {
            if (way.tags == null || string.IsNullOrEmpty(way.tags.name)) continue;
            string name = way.tags.name;
            if (seen.Contains(name) || way.geometry == null || way.geometry.Length < 2) continue;
            seen.Add(name);

            int   mid  = way.geometry.Length / 2;
            float wLat = way.geometry[mid].lat;
            float wLon = way.geometry[mid].lon;

            var a = way.geometry[Mathf.Max(0, mid - 1)];
            var b = way.geometry[Mathf.Min(way.geometry.Length - 1, mid + 1)];

            float cosLat   = Mathf.Cos(wLat * Mathf.Deg2Rad);
            float rotation = Mathf.Atan2(b.lat - a.lat, (b.lon - a.lon) * cosLat) * Mathf.Rad2Deg;
            if (rotation >  90f) rotation -= 180f;
            if (rotation < -90f) rotation += 180f;

            SpawnLabel(streetLabelPrefab, 1, name, wLat, wLon, rotation);
        }
    }

    // ── Deduplication ──────────────────────────────────────────────────────

    private void DeduplicateByProximity()
    {
        var toDestroy = new List<GameObject>();
        float threshold = Mathf.Max(1f, proximityThresholdPixels);

        for (int i = 0; i < spawnedLabels.Count; i++)
        {
            if (spawnedLabels[i].obj == null) continue;
            RectTransform rtI = spawnedLabels[i].obj.GetComponent<RectTransform>();
            if (rtI == null) continue;

            for (int j = 0; j < spawnedLabels.Count; j++)
            {
                if (i == j || spawnedLabels[j].obj == null) continue;
                if (spawnedLabels[j].priority < spawnedLabels[i].priority) continue;
                RectTransform rtJ = spawnedLabels[j].obj.GetComponent<RectTransform>();
                if (rtJ == null) continue;

                if (Vector2.Distance(rtI.anchoredPosition, rtJ.anchoredPosition) < threshold)
                {
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
    }

    public List<Vector2> GetSpawnedLatLons()
    {
        var positions = new List<Vector2>();
        foreach (var data in spawnedLabels)
        {
            if (data.obj == null) continue;
            var pp = data.obj.GetComponent<PlacePointer>();
            if (pp != null) positions.Add(new Vector2(pp.latitude, pp.longitude));
        }
        return positions;
    }

    // ── Sorting ────────────────────────────────────────────────────────────

    private void SortLabelsByPriority()
    {
        spawnedLabels.Sort((a, b) => a.priority.CompareTo(b.priority));
        for (int i = 0; i < spawnedLabels.Count; i++)
            if (spawnedLabels[i].obj != null)
                spawnedLabels[i].obj.transform.SetSiblingIndex(i);
    }

    // ── Shared spawn ───────────────────────────────────────────────────────

    private void SpawnLabel(GameObject prefab, int priority, string labelText, float lat, float lon, float rotation = 0f)
    {
        GameObject    obj = Instantiate(prefab, mapParent);
        RectTransform rt  = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = GPSToMapPosition(lat, lon);
        rt.localEulerAngles = new Vector3(0f, 0f, rotation);

        PlacePointer pp = obj.GetComponent<PlacePointer>();
        if (pp != null) { pp.latitude = lat; pp.longitude = lon; pp.mapTransform = mapParent; }

        TextMeshProUGUI tmp = obj.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) { tmp.text = labelText; tmp.color = CurrentLabelColor(); }

        spawnedLabels.Add(new LabelData { obj = obj, priority = priority });
    }

    private void RefreshLabelColors()
    {
        Color c = CurrentLabelColor();
        foreach (var data in spawnedLabels)
        {
            if (data.obj == null) continue;
            var tmp = data.obj.GetComponentInChildren<TextMeshProUGUI>();
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
        float   centerLat    = GPSManager.Instance.latitude;
        float   centerLon    = GPSManager.Instance.longitude;
        int     zoom         = MapLoader.instance.CurrentMapZoom;
        Vector2 p            = MapLoader.instance.LatLonToPixel(lat, lon, zoom);
        Vector2 c            = MapLoader.instance.LatLonToPixel(centerLat, centerLon, zoom);
        float   contentWidth = mapParent != null ? mapParent.rect.width : 640f;
        float   scale        = contentWidth / 320f;
        return new Vector2((p.x - c.x) * scale, (c.y - p.y) * scale);
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

    // ── Overpass JSON types ────────────────────────────────────────────────

    [Serializable] class OverpassResponse    { public OverpassElement[] elements; }
    [Serializable] class OverpassElement     { public string type; public float lat; public float lon; public OverpassCenter center; public OverpassTags tags; }
    [Serializable] class OverpassCenter      { public float lat; public float lon; }
    [Serializable] class OverpassTags        { public string name; public string leisure; public string amenity; public string tourism; public string shop; public string highway; public string railway; public string place; }

    [Serializable] class OverpassGeomResponse { public OverpassWay[]  elements; }
    [Serializable] class OverpassWay          { public OverpassNode[] geometry; public OverpassTags tags; }
    [Serializable] class OverpassNode         { public float lat; public float lon; }
}
