using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Fetches real-world points of interest from the Google Places Nearby Search API
/// and spawns a prefab for each type on the map.
///
/// This intentionally uses Google Places (not Mapbox) — Mapbox handles map tile
/// rendering, but Google Places is purpose-built for typed nearby POI search and
/// has significantly better coverage for categories like museum, bar, park, etc.
///
/// Wire up:
///   mapParentTransform — the same RectTransform used by MapBackground
///   googleApiKey       — your Google Maps API key (separate from the Mapbox token)
///   placeTypes         — add one entry per POI type; set the Google type string
///                        (e.g. "train_station") and assign the matching prefab
///
/// The fetch runs once on start and again whenever LocationUpdateManager fires
/// OnLocationUpdate (i.e. after the player has moved far enough to reload the map).
/// Already-spawned pins are reused across refreshes; duplicates are suppressed by
/// place_id so the same station is never pinned twice.
/// </summary>
public class PlacesFetcher : MonoBehaviour
{
    [System.Serializable]
    public class PlaceTypePrefab
    {
        [Tooltip("Google Places type string — e.g. train_station, museum, park")]
        public string placeType;
        [Tooltip("Prefab to spawn for this POI type — needs a PlacePointer component")]
        public GameObject prefab;
        [Tooltip("Higher priority wins when two pins are within the proximity threshold.\n" +
                 "Suggested: Park=1, BusStop=2, TrainStation=3, Building=4")]
        public int priority = 1;
    }

    [Header("References")]
    public RectTransform mapParentTransform;
    public string googleApiKey;

    [Header("POI Types")]
    public PlaceTypePrefab[] placeTypes;

    [Header("Settings")]
    [Tooltip("Radius in metres to search around the player's position")]
    public float searchRadiusMeters = 1000f;
public static PlacesFetcher instance;

    private struct PinData
    {
        public PlacePointer pointer;
        public int priority;
    }

    // placeId → pin data, so we never duplicate
    private readonly Dictionary<string, PinData> _spawned = new Dictionary<string, PinData>();

    // Place types found in the last fetch — used for prompt weighting
    private readonly HashSet<string> _nearbyPlaceTypes = new HashSet<string>();
    public IReadOnlyCollection<string> NearbyPlaceTypes => _nearbyPlaceTypes;

    private void Awake()
    {
        instance = this;
    }

    private void OnEnable()
    {
        if (LocationUpdateManager.instance != null)
            LocationUpdateManager.instance.OnLocationUpdate += OnLocationUpdate;
    }

    private void OnDisable()
    {
        if (LocationUpdateManager.instance != null)
            LocationUpdateManager.instance.OnLocationUpdate -= OnLocationUpdate;
    }

    private void Start()
    {
        StartCoroutine(WaitForGPSThenFetch());
    }

    private IEnumerator WaitForGPSThenFetch()
    {
        while (GPSManager.Instance == null ||
               (GPSManager.Instance.latitude == 0f && GPSManager.Instance.longitude == 0f))
            yield return new WaitForSeconds(0.5f);

        yield return FetchAllTypes();
    }

    private void OnLocationUpdate()
    {
        StartCoroutine(FetchAllTypes());
    }

    // ── Fetch ───────────────────────────────────────────────────────────────

    private IEnumerator FetchAllTypes()
    {
        float lat = GPSManager.Instance.latitude;
        float lon = GPSManager.Instance.longitude;

        _nearbyPlaceTypes.Clear();

        foreach (var entry in placeTypes)
        {
            if (string.IsNullOrEmpty(entry.placeType) || entry.prefab == null) continue;
            yield return FetchType(lat, lon, entry.placeType, entry.prefab, entry.priority);
            yield return new WaitForSeconds(0.2f);
        }

        yield return null;
        Canvas.ForceUpdateCanvases();
        DeduplicateByOverlap();
        ReorderByPriority();
        UpdateAllPositions();
    }

    private IEnumerator FetchType(float lat, float lon, string type, GameObject prefab, int priority)
    {
        string url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
                     $"?location={lat},{lon}" +
                     $"&radius={Mathf.RoundToInt(searchRadiusMeters)}" +
                     $"&type={type}" +
                     $"&key={googleApiKey}";

        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PlacesFetcher] {type} request failed: {req.error}");
                yield break;
            }

            PlacesResponse response = JsonUtility.FromJson<PlacesResponse>(req.downloadHandler.text);
            if (response?.results == null) yield break;

            if (response.results.Length > 0)
                _nearbyPlaceTypes.Add(type);

            foreach (var place in response.results)
            {
                if (string.IsNullOrEmpty(place.place_id)) continue;
                if (_spawned.ContainsKey(place.place_id)) continue;

                SpawnPin(place, prefab, priority);
            }
        }
    }

    private void SpawnPin(PlaceResult place, GameObject prefab, int priority)
    {
        GameObject go = Instantiate(prefab, mapParentTransform);
        PlacePointer pp = go.GetComponent<PlacePointer>();

        if (pp == null)
        {
            Debug.LogWarning($"[PlacesFetcher] Prefab '{prefab.name}' has no PlacePointer component.");
            Destroy(go);
            return;
        }

        pp.latitude     = place.geometry.location.lat;
        pp.longitude    = place.geometry.location.lng;
        pp.mapTransform = mapParentTransform;

        if (pp.label != null)
            pp.label.text = place.name;

        pp.UpdatePosition();

        _spawned[place.place_id] = new PinData { pointer = pp, priority = priority };
    }

    private void DeduplicateByOverlap()
    {
        var toRemove = new HashSet<string>();

        foreach (var kvp in _spawned)
        {
            if (toRemove.Contains(kvp.Key)) continue;
            RectTransform rt = kvp.Value.pointer?.transform as RectTransform;
            if (rt == null) continue;
            Rect myRect = GetLocalRect(rt);

            foreach (var other in _spawned)
            {
                if (other.Key == kvp.Key) continue;
                if (toRemove.Contains(other.Key)) continue;
                if (other.Value.priority <= kvp.Value.priority) continue;
                RectTransform otherRt = other.Value.pointer?.transform as RectTransform;
                if (otherRt == null) continue;
                if (myRect.Overlaps(GetLocalRect(otherRt)))
                {
                    toRemove.Add(kvp.Key);
                    break;
                }
            }
        }

        foreach (string id in toRemove)
        {
            if (_spawned.TryGetValue(id, out PinData pin) && pin.pointer != null)
                Destroy(pin.pointer.gameObject);
            _spawned.Remove(id);
        }
    }

    private static Rect GetLocalRect(RectTransform rt)
    {
        Rect r = rt.rect;
        Vector2 pos = rt.anchoredPosition;
        return new Rect(pos.x + r.x, pos.y + r.y, r.width, r.height);
    }

    public void UpdateAllPositions()
    {
        foreach (var data in _spawned.Values)
            if (data.pointer != null) data.pointer.UpdatePosition();
    }

    private void ReorderByPriority()
    {
        // Sort surviving pins lowest-priority first so higher-priority pins are
        // later siblings in the hierarchy and render on top in Unity UI.
        var sorted = new List<PinData>(_spawned.Values);
        sorted.Sort((a, b) => a.priority.CompareTo(b.priority));
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i].pointer != null)
                sorted[i].pointer.transform.SetSiblingIndex(i);
    }

    // ── JSON types ──────────────────────────────────────────────────────────

    [System.Serializable] private class PlacesResponse  { public PlaceResult[] results; }
    [System.Serializable] private class PlaceResult     { public string place_id; public string name; public PlaceGeometry geometry; }
    [System.Serializable] private class PlaceGeometry   { public PlaceLocation location; }
    [System.Serializable] private class PlaceLocation   { public float lat; public float lng; }
}
