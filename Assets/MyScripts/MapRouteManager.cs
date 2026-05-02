using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MapRouteManager : MonoBehaviour
{
    // ── Route type IDs ─────────────────────────────────────────────────────
    public const string MapPointerRouteType  = "map_pointer";
    public const string JourneyRouteType     = "journey";
    public const string JourneyTrailRouteType = "journey_trail";

    // ── Route type definition ──────────────────────────────────────────────
    [Serializable]
    public class RouteType
    {
        public string id       = "map_pointer";
        public Color  color    = new Color(0.2f, 0.9f, 1f, 0.85f);
        public Color  dimColor = new Color(0.05f, 0.3f, 0.38f, 1f);
        [Tooltip("When false a new route of this type replaces any existing one")]
        public bool allowMultiple = false;
    }

    // ── Internal route record ──────────────────────────────────────────────
    private class ActiveRoute
    {
        public RouteType       type;
        public List<float[]>   rawCoords;      // [lon, lat] pairs
        public UILineRenderer  line;
        public float           drawnZoom;
        public float           drawnCenterLat;
        public float           drawnCenterLon;
        public int             playerCoordIndex = 0; // index updated with live GPS; -1 = static
    }

    // ── Singleton ──────────────────────────────────────────────────────────
    public static MapRouteManager instance;

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Route Types")]
    public List<RouteType> routeTypes = new List<RouteType>
    {
        new RouteType { id = MapPointerRouteType,  color = new Color(0.2f, 0.9f, 1f, 0.85f) },
        new RouteType { id = JourneyRouteType,     color = new Color(1f, 0.82f, 0.2f, 0.9f),
                        dimColor = new Color(0.4f, 0.33f, 0.08f, 1f) },
        new RouteType { id = JourneyTrailRouteType, color = new Color(0.7f, 0.2f, 0.2f, 0.65f),
                        dimColor = new Color(0.4f, 0.1f, 0.1f, 0.65f) }
    };

    [Header("Line Appearance")]
    [Min(1f)]  public float lineWidth        = 10f;
    [Min(1f)]  public float maxSegmentLength = 20f;
    [Min(3)]   public int   roundSegments    = 6;
               public float drawOnDuration   = 0.8f;

    [Space]
               public float waveLength    = 120f;
               public float flowSpeed     = 70f;
               public bool  reverseFlow   = false;

    [Space]
               public float pulseWidthAmount = 2f;
               public float pulseWidthSpeed  = 2f;
               public float pulseAlphaMin    = 0.55f;
               public float pulseAlphaMax    = 1f;
               public float pulseAlphaSpeed  = 1.5f;

    [Header("Route Container")]
    [Tooltip("Parent RectTransform for route lines — keep this separate from Pointers/Landmarks so routes sit behind them. Must be inside MapArea.")]
    public RectTransform routeContainer;

    [Header("Outline")]
    public bool    outlineEnabled  = true;
    public Color   outlineColor    = Color.black;
    public Vector2 outlineDistance = new Vector2(10f, 10f);

    [Header("Shadow")]
    public bool    shadowEnabled  = true;
    public Color   shadowColor    = Color.black;
    public Vector2 shadowDistance = new Vector2(-15f, -15f);

    [Header("UI")]
    [Tooltip("Shown while the Directions API request is in flight")]
    public GameObject loadingIndicator;

    // ── Private state ──────────────────────────────────────────────────────
    private readonly Dictionary<string, List<ActiveRoute>> _routes =
        new Dictionary<string, List<ActiveRoute>>();

    private Coroutine _pendingFetch;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Awake() => instance = this;

    private void Update()
    {
        if (MapLoader.instance == null || GPSManager.Instance == null) return;

        float zoom      = MapLoader.instance.zoom;
        float centerLat = GPSManager.Instance.latitude;
        float centerLon = GPSManager.Instance.longitude;

        foreach (var list in _routes.Values)
            foreach (var r in list)
                if (r.rawCoords != null && r.line != null && NeedsRedraw(r, zoom, centerLat, centerLon))
                    RedrawRoute(r, zoom, centerLat, centerLon);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches a Mapbox walking route from (fromLat, fromLon) → (toLat, toLon)
    /// and draws it as a UILineRenderer on the map.
    /// </summary>
    public void DrawRoute(string typeId, float fromLat, float fromLon, float toLat, float toLon)
    {
        RouteType type = routeTypes.Find(r => r.id == typeId);
        if (type == null)
        {
            Debug.LogWarning($"[MapRouteManager] Unknown route type '{typeId}'");
            return;
        }

        if (!type.allowMultiple) ClearRoutes(typeId);

        if (_pendingFetch != null) StopCoroutine(_pendingFetch);
        _pendingFetch = StartCoroutine(FetchAndDraw(type, fromLat, fromLon, toLat, toLon));
    }

    public void ClearRoutes(string typeId)
    {
        if (!_routes.TryGetValue(typeId, out var list)) return;
        foreach (var r in list) if (r.line != null) Destroy(r.line.gameObject);
        list.Clear();
    }

    public void ClearAllRoutes()
    {
        // Iterate over a copy of keys so we can modify the dict
        foreach (var key in new List<string>(_routes.Keys)) ClearRoutes(key);
    }

    /// <summary>
    /// Draws a journey route from pre-fetched Mapbox GeoJSON coordinates.
    /// rawCoords is a list of [lon, lat] float pairs as returned by ParseCoordinates.
    /// </summary>
    public void SetJourneyRoute(List<float[]> rawCoords, int playerCoordIndex = 0)
    {
        ClearJourneyRoute();
        if (rawCoords == null || rawCoords.Count < 2) return;

        RouteType type = routeTypes.Find(r => r.id == JourneyRouteType);
        if (type == null)
        {
            type = new RouteType
            {
                id       = JourneyRouteType,
                color    = new Color(1f, 0.82f, 0.2f, 0.9f),
                dimColor = new Color(0.4f, 0.33f, 0.08f, 1f),
            };
            routeTypes.Add(type);
        }

        RectTransform container = routeContainer != null
            ? routeContainer
            : GoogleSheetsFetcher.instance?.mapParentTransform;
        if (container == null) return;

        var go = new GameObject($"Route_{JourneyRouteType}", typeof(RectTransform),
                                typeof(CanvasRenderer), typeof(UILineRenderer));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var line = go.GetComponent<UILineRenderer>();
        line.raycastTarget = false;

        if (outlineEnabled)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = outlineColor;
            outline.effectDistance = outlineDistance;
        }

        if (shadowEnabled)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor    = shadowColor;
            shadow.effectDistance = shadowDistance;
        }

        line.lineWidth        = lineWidth;
        line.maxSegmentLength = maxSegmentLength;
        line.roundSegments    = roundSegments;
        line.drawOnDuration   = drawOnDuration;
        line.brightColor      = type.color;
        line.dimColor         = type.dimColor;
        line.waveLength       = waveLength;
        line.flowSpeed        = flowSpeed;
        line.reverseFlow      = reverseFlow;
        line.pulseWidthAmount = pulseWidthAmount;
        line.pulseWidthSpeed  = pulseWidthSpeed;
        line.pulseAlphaMin    = pulseAlphaMin;
        line.pulseAlphaMax    = pulseAlphaMax;
        line.pulseAlphaSpeed  = pulseAlphaSpeed;

        go.transform.SetAsLastSibling(); // live segment renders above the trail

        float zoom      = MapLoader.instance != null ? MapLoader.instance.zoom : 14f;
        float centerLat = GPSManager.Instance != null ? GPSManager.Instance.latitude  : 0f;
        float centerLon = GPSManager.Instance != null ? GPSManager.Instance.longitude : 0f;

        var points = CoordsToUI(rawCoords, centerLat, centerLon, zoom);
        line.SetPoints(points);

        var route = new ActiveRoute
        {
            type             = type,
            rawCoords        = rawCoords,
            line             = line,
            drawnZoom        = zoom,
            drawnCenterLat   = centerLat,
            drawnCenterLon   = centerLon,
            playerCoordIndex = playerCoordIndex,
        };

        if (!_routes.ContainsKey(JourneyRouteType)) _routes[JourneyRouteType] = new List<ActiveRoute>();
        _routes[JourneyRouteType].Add(route);
    }

    public void ClearJourneyRoute() => ClearRoutes(JourneyRouteType);

    /// <summary>
    /// Draws the completed (trail) portion of a journey route in a desaturated style.
    /// Static — no GPS tracking, no flow animation.
    /// </summary>
    public void SetJourneyTrailRoute(List<float[]> rawCoords)
    {
        ClearJourneyTrail();
        if (rawCoords == null || rawCoords.Count < 2) return;

        RouteType type = routeTypes.Find(r => r.id == JourneyTrailRouteType);
        if (type == null)
        {
            type = new RouteType { id = JourneyTrailRouteType,
                color    = new Color(0.7f, 0.2f, 0.2f, 0.65f),
                dimColor = new Color(0.4f, 0.1f, 0.1f, 0.65f) };
            routeTypes.Add(type);
        }

        RectTransform container = routeContainer != null
            ? routeContainer
            : GoogleSheetsFetcher.instance?.mapParentTransform;
        if (container == null) return;

        var go = new GameObject($"Route_{JourneyTrailRouteType}",
                                typeof(RectTransform), typeof(CanvasRenderer), typeof(UILineRenderer));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var line = go.GetComponent<UILineRenderer>();
        line.raycastTarget = false;

        if (outlineEnabled)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = outlineColor;
            outline.effectDistance = outlineDistance;
        }
        if (shadowEnabled)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor    = shadowColor;
            shadow.effectDistance = shadowDistance;
        }

        line.lineWidth        = lineWidth;
        line.maxSegmentLength = maxSegmentLength;
        line.roundSegments    = roundSegments;
        line.drawOnDuration   = drawOnDuration;
        line.brightColor      = type.color;
        line.dimColor         = type.dimColor;
        line.waveLength       = waveLength;
        line.flowSpeed        = 0f;   // no flow — this portion is history
        line.reverseFlow      = reverseFlow;
        line.pulseWidthAmount = 0f;   // no pulse
        line.pulseWidthSpeed  = 0f;
        line.pulseAlphaMin    = 1f;
        line.pulseAlphaMax    = 1f;
        line.pulseAlphaSpeed  = 0f;

        go.transform.SetAsFirstSibling(); // behind everything, under the live segment

        float zoom      = MapLoader.instance != null ? MapLoader.instance.zoom : 14f;
        float centerLat = GPSManager.Instance != null ? GPSManager.Instance.latitude  : 0f;
        float centerLon = GPSManager.Instance != null ? GPSManager.Instance.longitude : 0f;

        line.SetPoints(CoordsToUI(rawCoords, centerLat, centerLon, zoom));

        var route = new ActiveRoute
        {
            type             = type,
            rawCoords        = rawCoords,
            line             = line,
            drawnZoom        = zoom,
            drawnCenterLat   = centerLat,
            drawnCenterLon   = centerLon,
            playerCoordIndex = -1, // static — never updated
        };

        if (!_routes.ContainsKey(JourneyTrailRouteType))
            _routes[JourneyTrailRouteType] = new List<ActiveRoute>();
        _routes[JourneyTrailRouteType].Add(route);
    }

    public void ClearJourneyTrail() => ClearRoutes(JourneyTrailRouteType);

    // ── Fetch & Draw ───────────────────────────────────────────────────────

    private IEnumerator FetchAndDraw(RouteType type,
        float fromLat, float fromLon, float toLat, float toLon)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(true);

        string token = MapLoader.instance != null ? MapLoader.instance.mapboxToken : "";

        string url =
            "https://api.mapbox.com/directions/v5/mapbox/walking/" +
            $"{F(fromLon)},{F(fromLat)};{F(toLon)},{F(toLat)}" +
            $"?geometries=geojson&access_token={token}";

        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (loadingIndicator != null) loadingIndicator.SetActive(false);
        _pendingFetch = null;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[MapRouteManager] Directions fetch failed: {req.error}");
            yield break;
        }

        List<float[]> rawCoords = ParseCoordinates(req.downloadHandler.text);
        if (rawCoords == null || rawCoords.Count < 2)
        {
            Debug.LogWarning("[MapRouteManager] No usable coordinates in directions response.");
            yield break;
        }

        // Bridge the road-snapped start/endpoints to the exact requested positions
        rawCoords.Insert(0, new[] { fromLon, fromLat });
        rawCoords.Add(new[] { toLon, toLat });

        RectTransform container = routeContainer != null
            ? routeContainer
            : GoogleSheetsFetcher.instance?.mapParentTransform;
        if (container == null) yield break;

        // Create line object
        var go = new GameObject($"Route_{type.id}", typeof(RectTransform),
                                typeof(CanvasRenderer), typeof(UILineRenderer));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var line = go.GetComponent<UILineRenderer>();
        line.raycastTarget = false;

        if (outlineEnabled)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = outlineColor;
            outline.effectDistance = outlineDistance;
        }

        if (shadowEnabled)
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor    = shadowColor;
            shadow.effectDistance = shadowDistance;
        }

        // Shared appearance
        line.lineWidth        = lineWidth;
        line.maxSegmentLength = maxSegmentLength;
        line.roundSegments    = roundSegments;
        line.drawOnDuration   = drawOnDuration;
        line.brightColor      = type.color;
        line.dimColor         = type.dimColor;
        line.waveLength       = waveLength;
        line.flowSpeed        = flowSpeed;
        line.reverseFlow      = reverseFlow;
        line.pulseWidthAmount = pulseWidthAmount;
        line.pulseWidthSpeed  = pulseWidthSpeed;
        line.pulseAlphaMin    = pulseAlphaMin;
        line.pulseAlphaMax    = pulseAlphaMax;
        line.pulseAlphaSpeed  = pulseAlphaSpeed;

        // Place behind all map pointers
        go.transform.SetAsFirstSibling();

        float zoom      = MapLoader.instance != null ? MapLoader.instance.zoom : 14f;
        float centerLat = GPSManager.Instance != null ? GPSManager.Instance.latitude  : 0f;
        float centerLon = GPSManager.Instance != null ? GPSManager.Instance.longitude : 0f;

        var points = CoordsToUI(rawCoords, centerLat, centerLon, zoom);
        line.SetPoints(points);

        var route = new ActiveRoute
        {
            type           = type,
            rawCoords      = rawCoords,
            line           = line,
            drawnZoom      = zoom,
            drawnCenterLat = centerLat,
            drawnCenterLon = centerLon
        };

        if (!_routes.ContainsKey(type.id)) _routes[type.id] = new List<ActiveRoute>();
        _routes[type.id].Add(route);
    }

    // ── Redraw ─────────────────────────────────────────────────────────────

    private static bool NeedsRedraw(ActiveRoute r, float zoom, float lat, float lon)
    {
        return !Mathf.Approximately(r.drawnZoom,      zoom) ||
               !Mathf.Approximately(r.drawnCenterLat, lat)  ||
               !Mathf.Approximately(r.drawnCenterLon, lon);
    }

    private void RedrawRoute(ActiveRoute r, float zoom, float centerLat, float centerLon)
    {
        if (GPSManager.Instance != null && r.playerCoordIndex >= 0 && r.playerCoordIndex < r.rawCoords.Count)
            r.rawCoords[r.playerCoordIndex] = new[] { GPSManager.Instance.longitude, GPSManager.Instance.latitude };

        var points = CoordsToUI(r.rawCoords, centerLat, centerLon, zoom);
        r.line.SetPointsSilent(points);
        r.drawnZoom      = zoom;
        r.drawnCenterLat = centerLat;
        r.drawnCenterLon = centerLon;
    }

    // ── Coordinate helpers ─────────────────────────────────────────────────

    private List<Vector2> CoordsToUI(List<float[]> coords,
        float centerLat, float centerLon, float zoom)
    {
        var pts = new List<Vector2>(coords.Count);
        foreach (var c in coords) pts.Add(GPSToUI(c[1], c[0], centerLat, centerLon, zoom));
        return pts;
    }

    private Vector2 GPSToUI(float lat, float lon,
        float centerLat, float centerLon, float zoom)
    {
        if (MapLoader.instance == null) return Vector2.zero;
        int z = Mathf.RoundToInt(zoom);

        Vector2 p = MapLoader.instance.LatLonToPixel(lat,       lon, z);
        Vector2 c = MapLoader.instance.LatLonToPixel(centerLat, centerLon, z);

        // 1280px crop from 4× tiles = 320 virtual 256-px tile pixels span the content
        float contentWidth = routeContainer != null ? routeContainer.rect.width
            : GoogleSheetsFetcher.instance?.mapParentTransform?.rect.width ?? 640f;
        float scale = contentWidth / 320f;

        return new Vector2((p.x - c.x) * scale, (c.y - p.y) * scale);
    }

    // ── JSON parsing ───────────────────────────────────────────────────────
    // Mapbox GeoJSON: "coordinates":[[lon,lat],[lon,lat],...]
    private static List<float[]> ParseCoordinates(string json)
    {
        int start = json.IndexOf("\"coordinates\":[[", StringComparison.Ordinal);
        if (start < 0) return null;
        start += 16;

        int end = json.IndexOf("]]", start);
        if (end < 0) return null;

        string section = json.Substring(start, end - start + 1);
        var matches = Regex.Matches(section, @"\[(-?[\d.]+),(-?[\d.]+)\]");
        if (matches.Count == 0) return null;

        var list = new List<float[]>(matches.Count);
        foreach (Match m in matches)
        {
            if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float lon) &&
                float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float lat))
            {
                list.Add(new[] { lon, lat });
            }
        }
        return list;
    }

    private static string F(float v) =>
        v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
}
