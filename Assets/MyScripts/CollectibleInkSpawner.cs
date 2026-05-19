using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;


public class CollectibleInkSpawner : MonoBehaviour
{
    public static CollectibleInkSpawner instance;

    [Header("Ink Prefab & Containers")]
    public GameObject    collectibleInkPrefab;
    public RectTransform mapParent;
    public RectTransform inkCounterTarget;

    [Header("Ink Settings")]
    public int   spawnCountPerDay    = 10;
    public float spawnRadiusKm       = 3f;
    public float collectRadiusMetres = 50f;
    public float minSpacingMetres    = 200f;
    public int   minInkReward        = 5;
    public int   maxInkReward        = 15;

    [Header("Golden Quill (Rare Weekly)")]
    public GameObject    goldenQuillPrefab;
    public RectTransform quillCounterTarget;
    [Range(0f, 1f)]
    [Tooltip("Probability of a Golden Quill spawning today (rolled once per day)")]
    public float weeklyQuillChance = 0.4f;

    const string DateKey      = "CollectibleInk_Date";
    const string DataKey      = "CollectibleInk_Positions";
    const string QuillWeekKey = "GoldenQuill_Day";
    const string QuillLatKey  = "GoldenQuill_Lat";
    const string QuillLonKey  = "GoldenQuill_Lon";
    const string QuillCollKey = "GoldenQuill_Collected";

    readonly List<CollectibleInk> _spawned = new List<CollectibleInk>();

    [Serializable] class SpawnData        { public List<float> lats = new(); public List<float> lons = new(); }
    [Serializable] class OverpassResponse { public OverpassElement[] elements; }
    [Serializable] class OverpassElement  { public OverpassNode[] geometry; }
    [Serializable] class OverpassNode     { public float lat; public float lon; }

    void Awake() => instance = this;
    void Start() => StartCoroutine(WaitAndSpawn());

    [ContextMenu("Refresh Collectibles")]
    public void RefreshCollectibles()
    {
        PlayerPrefs.DeleteKey(DateKey);
        PlayerPrefs.DeleteKey(DataKey);
        PlayerPrefs.Save();
        StopAllCoroutines();
        StartCoroutine(WaitAndSpawn());
    }

    IEnumerator WaitAndSpawn()
    {
        while (GPSManager.Instance == null || GPSManager.Instance.latitude == 0f)
            yield return new WaitForSeconds(0.5f);

        string today      = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string storedDate = PlayerPrefs.GetString(DateKey, "");

        List<Vector2> positions;

        if (storedDate != today)
            yield return StartCoroutine(FetchAndSave(today));

        SpawnAll(LoadPositions());
        TrySpawnWeeklyQuill();
    }

    // ── Golden Quill ───────────────────────────────────────────────────────

    void TrySpawnWeeklyQuill()
    {
        if (goldenQuillPrefab == null) return;

        string week = DailyKey();

        if (PlayerPrefs.GetString(QuillWeekKey, "") == week)
        {
            // Already determined for this week
            if (PlayerPrefs.GetInt(QuillCollKey, 0) != 0) return; // already collected
            float savedLat = PlayerPrefs.GetFloat(QuillLatKey, 0f);
            float savedLon = PlayerPrefs.GetFloat(QuillLonKey, 0f);
            if (savedLat == 0f) return; // chance failed this week, no quill
            SpawnQuill(savedLat, savedLon);
            return;
        }

        // New week — roll the chance
        PlayerPrefs.SetString(QuillWeekKey, week);
        PlayerPrefs.SetInt(QuillCollKey, 0);

        if (UnityEngine.Random.value > weeklyQuillChance)
        {
            PlayerPrefs.SetFloat(QuillLatKey, 0f);
            PlayerPrefs.SetFloat(QuillLonKey, 0f);
            PlayerPrefs.Save();
            return;
        }

        // Pick a random position within the spawn radius
        float angle     = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float distKm    = UnityEngine.Random.Range(200f, spawnRadiusKm * 1000f) / 1000f;
        float playerLat = GPSManager.Instance.latitude;
        float playerLon = GPSManager.Instance.longitude;
        float qLat      = playerLat + distKm / 111.0f * Mathf.Cos(angle);
        float qLon      = playerLon + distKm / (111.0f * Mathf.Cos(playerLat * Mathf.Deg2Rad)) * Mathf.Sin(angle);

        PlayerPrefs.SetFloat(QuillLatKey, qLat);
        PlayerPrefs.SetFloat(QuillLonKey, qLon);
        PlayerPrefs.Save();

        SpawnQuill(qLat, qLon);
    }

    void SpawnQuill(float lat, float lon)
    {
        var go    = Instantiate(goldenQuillPrefab, mapParent);
        var quill = go.GetComponent<CollectibleGoldenQuill>();
        if (quill != null)
            quill.Initialise(lat, lon, quillCounterTarget, collectRadiusMetres);
    }

    public void OnQuillCollected()
    {
        PlayerPrefs.SetInt(QuillCollKey, 1);
        PlayerPrefs.Save();
    }

    static string DailyKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    IEnumerator FetchAndSave(string today)
    {
        float lat    = GPSManager.Instance.latitude;
        float lon    = GPSManager.Instance.longitude;
        float radius = spawnRadiusKm * 1000f;

        string query = "[out:json][timeout:25];" +
                       $"way[\"highway\"~\"footway|path|pedestrian|residential|living_street|service|cycleway|unclassified|tertiary|secondary\"]" +
                       $"(around:{radius:F0},{lat:F6},{lon:F6});" +
                       "out geom;";

        string url = "https://overpass-api.de/api/interpreter?data=" + Uri.EscapeDataString(query);

        using var req = UnityWebRequest.Get(url);
        req.timeout = 30;
        yield return req.SendWebRequest();

        var data = new SpawnData();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<OverpassResponse>(req.downloadHandler.text);

            // Build segments weighted by length
            var segments  = new List<(Vector2 a, Vector2 b, float len)>();
            float totalLen = 0f;

            if (response?.elements != null)
            {
                foreach (var way in response.elements)
                {
                    if (way.geometry == null || way.geometry.Length < 2) continue;
                    for (int i = 0; i < way.geometry.Length - 1; i++)
                    {
                        var a   = new Vector2(way.geometry[i].lat,     way.geometry[i].lon);
                        var b   = new Vector2(way.geometry[i + 1].lat, way.geometry[i + 1].lon);
                        float l = DistMetres(a.x, a.y, b.x, b.y);
                        if (l > 0f) { segments.Add((a, b, l)); totalLen += l; }
                    }
                }
            }

            if (segments.Count > 0)
            {
                var chosen = new List<Vector2>();
                int maxAttempts = spawnCountPerDay * 60;

                for (int attempt = 0; attempt < maxAttempts && chosen.Count < spawnCountPerDay; attempt++)
                {
                    // Pick a random segment weighted by length
                    float r   = UnityEngine.Random.Range(0f, totalLen);
                    float acc = 0f;
                    var seg   = segments[segments.Count - 1];
                    foreach (var s in segments) { acc += s.len; if (r <= acc) { seg = s; break; } }

                    var candidate = Vector2.Lerp(seg.a, seg.b, UnityEngine.Random.value);
                    if (IsTooClose(candidate, chosen)) continue;
                    if (IsNearStoryOrLandmark(candidate)) continue;
                    chosen.Add(candidate);
                }

                foreach (var p in chosen) { data.lats.Add(p.x); data.lons.Add(p.y); }
            }
        }

        PlayerPrefs.SetString(DateKey, today);
        PlayerPrefs.SetString(DataKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    bool IsNearStoryOrLandmark(Vector2 candidate)
    {
        var fetcher = GoogleSheetsFetcher.instance;
        if (fetcher == null) return false;

        foreach (var entry in fetcher.storiesList)
            if (entry != null && DistMetres(candidate.x, candidate.y, entry.Latitude, entry.Longitude) < minSpacingMetres)
                return true;

        foreach (var entry in fetcher.landmarksList)
            if (entry != null && DistMetres(candidate.x, candidate.y, entry.Latitude, entry.Longitude) < minSpacingMetres)
                return true;

        var labelSpawner = MapLabelSpawner.instance;
        if (labelSpawner != null)
            foreach (var pos in labelSpawner.GetSpawnedLatLons())
                if (DistMetres(candidate.x, candidate.y, pos.x, pos.y) < minSpacingMetres)
                    return true;

        return false;
    }

    bool IsTooClose(Vector2 candidate, List<Vector2> chosen)
    {
        foreach (var existing in chosen)
            if (DistMetres(candidate.x, candidate.y, existing.x, existing.y) < minSpacingMetres)
                return true;
        return false;
    }

    static float DistMetres(float lat1, float lon1, float lat2, float lon2)
    {
        float dy = (lat1 - lat2) * 111320f;
        float dx = (lon1 - lon2) * 111320f * Mathf.Cos(lat1 * Mathf.Deg2Rad);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    List<Vector2> LoadPositions()
    {
        var list = new List<Vector2>();
        var json = PlayerPrefs.GetString(DataKey, "");
        if (string.IsNullOrEmpty(json)) return list;
        var data = JsonUtility.FromJson<SpawnData>(json);
        if (data == null) return list;
        for (int i = 0; i < Mathf.Min(data.lats.Count, data.lons.Count); i++)
            list.Add(new Vector2(data.lats[i], data.lons[i]));
        return list;
    }

    void SpawnAll(List<Vector2> positions)
    {
        ClearSpawned();
        foreach (var pos in positions)
        {
            if (IsCollectedToday(pos.x, pos.y)) continue;
            var go  = Instantiate(collectibleInkPrefab, mapParent);
            var ink = go.GetComponent<CollectibleInk>();
            if (ink == null) continue;
            ink.Initialise(pos.x, pos.y, inkCounterTarget, collectRadiusMetres, minInkReward, maxInkReward);
            _spawned.Add(ink);
        }
    }

    void ClearSpawned()
    {
        foreach (var ink in _spawned)
            if (ink != null) Destroy(ink.gameObject);
        _spawned.Clear();
    }

    public void OnCollected(CollectibleInk ink)
    {
        _spawned.Remove(ink);
        MarkCollectedToday(ink.Latitude, ink.Longitude);
    }

    static string CollectedKey(float lat, float lon)
        => $"CollectibleInk_{lat:F5}_{lon:F5}_{DateTime.UtcNow:yyyy-MM-dd}";

    static bool IsCollectedToday(float lat, float lon)
        => PlayerPrefs.GetInt(CollectedKey(lat, lon), 0) != 0;

    static void MarkCollectedToday(float lat, float lon)
    {
        PlayerPrefs.SetInt(CollectedKey(lat, lon), 1);
        PlayerPrefs.Save();
    }
}
