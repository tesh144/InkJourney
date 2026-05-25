using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Linq;
using TMPro;
using System;
using Sirenix.OdinInspector;

public class MapLoader : MonoBehaviour
{
    public RawImage mapImage;
    public string mapboxToken = "pk.eyJ1IjoidGVzaDE0NCIsImEiOiJjbW55ZDZudzMwMHZyMnBzYWh2M3Yxb2V1In0.b9_qzGiE2GdpL7TfVPgaGA";

    private const string MapboxStaticBase    = "https://api.mapbox.com/styles/v1/";
    private const string MapboxGeocodingBase = "https://api.mapbox.com/geocoding/v5/mapbox.places/";
    private const string MapboxDefaultStyle  = "mapbox/dark-v11";

    public ResetScrollRect resetScrollRect;

    private string areaName = "";
    public TextMeshProUGUI showAreaName;

    private void Start()
    {
        StartCoroutine(WaitForValidGPSData());
    }

    public void Refresh()
    {
        mapLoaded = false;
        SetMainMapReloading(true);
        StartCoroutine(WaitForValidGPSData());
    }


    // ── Map Styles ─────────────────────────────────────────────────────────
    [System.Serializable]
    public class MapStyleEntry
    {
        public string name;
        [TextArea(1, 5)] public string styleString;
        public Color      backgroundColor = new Color(0.1f, 0.1f, 0.1f);
        public bool       showLabels  = false;
        public Color      labelColor  = Color.white;
        public Color      labelStroke = Color.black;
        public AudioClip  music;
        public bool       isLocked    = false;
        public ThemeObject defaultTheme;
    }

    [Header("Map Styles")]
    public List<MapStyleEntry> mapStyles = new List<MapStyleEntry>();
    public int currentStyleIndex = 0;
    [Tooltip("Show road/street name labels on the map")]
    public bool showStreetNames = false;
    [Tooltip("0 = pointers scale with the map, 1 = fixed screen size, values between blend the two")]
    [Range(0f, 1f)] public float pointerCounterScale = 1f;
    [Tooltip("0 = compass scales with the map, 1 = fixed screen size, values between blend the two")]
    [Range(0f, 1f)] public float compassCounterScale = 1f;

    [TextArea(1, 5)]
    [Tooltip("Active style — set automatically by ChooseStyle, or fill manually if mapStyles is empty")]
    public string mapStyle;

    public float zoom = 13f;
    public static MapLoader instance;

    /// <summary>True once the current map texture has finished downloading.</summary>
    public bool mapLoaded { get; private set; }
    public bool IsMainMapReloading { get; private set; }
    public float CurrentMapCenterLat { get; private set; }
    public float CurrentMapCenterLon { get; private set; }
    public int CurrentMapZoom => Mathf.RoundToInt(zoom);

    [Header("Purchase Feedback")]
    public GameObject purchaseSuccessPanel;
    public GameObject purchaseFailPanel;

    [Header("Events")]
    [Tooltip("Fired when a map pointer is tapped but the player is not within range to read it")]
    public UnityEvent onPointerTappedOutOfRange;

    private const string StylePrefKey          = "MapStyleIndex";
    private const string UnlockedStylesPrefKey = "UnlockedStyleIndices";

    public static event System.Action onStyleChanged;
    public static event System.Action<bool> onMainMapReloadStateChanged;
    public static event System.Action onStyleUnlocked;

    private HashSet<int> _unlockedStyleIndices = new HashSet<int>();

    private void Awake()
    {
        SetMainMapReloading(true);
        instance = this;
        LoadUnlockedStyles();
        ApplySavedStyle();
    }

    public bool IsStyleUnlocked(int index)
    {
        if (index < 0 || index >= mapStyles.Count) return false;
        if (!mapStyles[index].isLocked) return true;
        return _unlockedStyleIndices.Contains(index);
    }

    public void UnlockStyle(int index)
    {
        _unlockedStyleIndices.Add(index);
        SaveUnlockedStyles();
        onStyleUnlocked?.Invoke();
    }

    public void LockStyle(int index)
    {
        _unlockedStyleIndices.Remove(index);
        SaveUnlockedStyles();
        onStyleUnlocked?.Invoke();
    }

    public void ShowPurchaseFeedback(bool success)
    {
        if (success)
        {
            if (purchaseSuccessPanel != null) purchaseSuccessPanel.SetActive(true);
        }
        else
        {
            if (purchaseFailPanel != null) purchaseFailPanel.SetActive(true);
        }
    }

    private void LoadUnlockedStyles()
    {
        _unlockedStyleIndices.Clear();
        string raw = PlayerPrefs.GetString(UnlockedStylesPrefKey, "");
        foreach (var part in raw.Split(','))
            if (int.TryParse(part, out int i)) _unlockedStyleIndices.Add(i);
    }

    private void SaveUnlockedStyles()
    {
        PlayerPrefs.SetString(UnlockedStylesPrefKey, string.Join(",", _unlockedStyleIndices));
        PlayerPrefs.Save();
    }

    private void SetMainMapReloading(bool reloading)
    {
        if (IsMainMapReloading == reloading)
            return;

        IsMainMapReloading = reloading;
        onMainMapReloadStateChanged?.Invoke(reloading);
    }

    private void ApplySavedStyle()
    {
        if (mapStyles == null || mapStyles.Count == 0) return;
        int saved = PlayerPrefs.GetInt(StylePrefKey, currentStyleIndex);
        currentStyleIndex = Mathf.Clamp(saved, 0, mapStyles.Count - 1);
        mapStyle = mapStyles[currentStyleIndex].styleString;
    }

    private string GetCurrentStyleId()
    {
        return !string.IsNullOrEmpty(mapStyle) ? mapStyle : MapboxDefaultStyle;
    }

    public Color GetCurrentBackgroundColor()
    {
        if (mapStyles != null && currentStyleIndex >= 0 && currentStyleIndex < mapStyles.Count)
            return mapStyles[currentStyleIndex].backgroundColor;
        return new Color(0.1f, 0.1f, 0.1f);
    }

    public void ChooseStyle(int index)
    {
        if (mapStyles == null || mapStyles.Count == 0) return;
        currentStyleIndex = Mathf.Clamp(index, 0, mapStyles.Count - 1);
        if (!IsStyleUnlocked(currentStyleIndex)) return;
        mapStyle = mapStyles[currentStyleIndex].styleString;
        PlayerPrefs.SetInt(StylePrefKey, currentStyleIndex);
        PlayerPrefs.Save();
        onStyleChanged?.Invoke();

        if (SpriteSheetTransition.instance != null)
            SpriteSheetTransition.instance.DoTransition(Refresh, () => mapLoaded);
        else if (InkTransition.instance != null)
            InkTransition.instance.DoTransition(Refresh, () => mapLoaded);
        else
            Refresh();
    }

    private IEnumerator WaitForValidGPSData()
    {
        float elapsed = 0f;
        while (GPSManager.Instance == null || GPSManager.Instance.latitude == 0 || GPSManager.Instance.longitude == 0)
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
            if (elapsed >= 15f)
            {
                mapLoaded = true;
                SetMainMapReloading(false);
                yield break;
            }
        }

        StartCoroutine(LoadMapAt(GPSManager.Instance.latitude, GPSManager.Instance.longitude));
    }

    public void LoadAtCoordinate(float lat, float lon)
    {
        mapLoaded = false;
        SetMainMapReloading(true);
        StartCoroutine(LoadMapAt(lat, lon));
    }

    private IEnumerator LoadMapAt(float lat, float lon)
    {
        CurrentMapCenterLat = lat;
        CurrentMapCenterLon = lon;
        string styleId = GetCurrentStyleId();
        int z = Mathf.RoundToInt(zoom);
        double n = Math.Pow(2, z);

        // Convert user's GPS position to fractional tile coordinates
        double latRad = lat * Math.PI / 180.0;
        double tileXf = (lon + 180.0) / 360.0 * n;
        double tileYf = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
        int    tileX  = (int)Math.Floor(tileXf);
        int    tileY  = (int)Math.Floor(tileYf);
        double fracX  = tileXf - tileX;   // 0 = left edge,  1 = right edge
        double fracY  = tileYf - tileY;   // 0 = top (north), 1 = bottom (south)

        // 3×3 grid of pre-rendered seamless tiles centred on the user's tile.
        // 512-px tiles @2x = 1024 physical pixels each → 3072×3072 composite.
        // We then crop a 1280×1280 window exactly centred on the user.
        // The user's tile is always at grid position (col=1, row=1).
        const int tilePixels    = 1024;          // 512px @2x
        const int gridSize      = 3;
        const int compositeSize = tilePixels * gridSize;  // 3072
        const int outputSize    = 1280;

        // Fire all 9 requests in parallel
        var requests = new UnityWebRequest[gridSize * gridSize];
        for (int row = 0; row < gridSize; row++)
        for (int col = 0; col < gridSize; col++)
        {
            int tx  = tileX + (col - 1);
            int ty  = tileY + (row - 1);
            int idx = row * gridSize + col;
            string url = $"https://api.mapbox.com/styles/v1/{styleId}/tiles/512/{z}/{tx}/{ty}@2x?access_token={mapboxToken}";
            requests[idx] = UnityWebRequestTexture.GetTexture(url);
            requests[idx].SendWebRequest();
        }

        // Wait for all to finish in parallel
        bool allDone;
        do {
            allDone = true;
            foreach (var r in requests)
                if (!r.isDone) { allDone = false; break; }
            if (!allDone) yield return null;
        } while (!allDone);

        // Build composite pixel buffer row-by-row, disposing tiles as we go
        var composite = new Color32[compositeSize * compositeSize];
        bool anyFailed = false;

        for (int row = 0; row < gridSize && !anyFailed; row++)
        for (int col = 0; col < gridSize && !anyFailed; col++)
        {
            int idx = row * gridSize + col;
            if (requests[idx].result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MapLoader] Tile ({col},{row}) failed: {requests[idx].error}");
                anyFailed = true;
                break;
            }

            var tile      = ((DownloadHandlerTexture)requests[idx].downloadHandler).texture;
            var tileData  = tile.GetPixels32();
            int destX     = col * tilePixels;
            // grid row 0 = northernmost → highest Unity y; flip: destRow = (gridSize-1-row)
            int destYBase = (gridSize - 1 - row) * tilePixels;

            for (int r = 0; r < tilePixels; r++)
                Array.Copy(tileData, r * tilePixels,
                           composite, (destYBase + r) * compositeSize + destX,
                           tilePixels);

            requests[idx].Dispose();
        }

        if (!anyFailed)
        {
            // User's pixel in the composite (Unity tex coords: y=0 at south/bottom)
            // User is in center tile (col=1, row=1) → destYBase = 1024
            int userPxX = tilePixels + (int)(fracX * tilePixels);
            int userPxY = tilePixels + (int)((1.0 - fracY) * tilePixels);  // flip image-y

            // Crop 1280×1280 centred on user (margins guaranteed ≥ 384 px each side)
            int cropX = userPxX - outputSize / 2;
            int cropY = userPxY - outputSize / 2;

            var cropped = new Color32[outputSize * outputSize];
            for (int r = 0; r < outputSize; r++)
                Array.Copy(composite, (cropY + r) * compositeSize + cropX,
                           cropped, r * outputSize, outputSize);

            var output = new Texture2D(outputSize, outputSize, TextureFormat.RGBA32, false);
            output.filterMode = FilterMode.Bilinear;
            output.SetPixels32(cropped);
            output.Apply();

            // GPU bilinear upscale to 2× for a smoother finish
            const int upscaleSize = outputSize * 2; // 2560
            var rt = new RenderTexture(upscaleSize, upscaleSize, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(output, rt);
            mapImage.texture = rt;
            yield return null; // give GPU one frame to complete the blit before signalling ready
        }

        mapLoaded = true;
        SetMainMapReloading(false);

        StartCoroutine(GetLocationName(lat, lon));
    }

    private string ExtractNeighbourhoodFromMapbox(MapboxGeocodeResponse response)
    {
        if (response?.features == null) return null;
        foreach (var feat in response.features)
        {
            if (feat?.place_type == null) continue;
            if (System.Array.IndexOf(feat.place_type, "neighborhood") >= 0
             || System.Array.IndexOf(feat.place_type, "locality") >= 0)
                return feat.text;
        }
        return null;
    }

    private string ExtractCityFromMapbox(MapboxGeocodeResponse response)
    {
        if (response?.features == null) return null;
        // Walk context of first feature
        if (response.features.Length > 0 && response.features[0].context != null)
            foreach (var ctx in response.features[0].context)
                if (ctx?.id != null && (ctx.id.StartsWith("place.") || ctx.id.StartsWith("locality.")))
                    return ctx.text;
        // Fallback: find place-type feature
        foreach (var feat in response.features)
            if (feat?.place_type != null && System.Array.IndexOf(feat.place_type, "place") >= 0)
                return feat.text;
        return null;
    }

    private string ExtractDistrictFromMapbox(MapboxGeocodeResponse response)
    {
        if (response?.features == null) return null;
        if (response.features.Length > 0 && response.features[0].context != null)
            foreach (var ctx in response.features[0].context)
                if (ctx?.id != null && ctx.id.StartsWith("district."))
                    return ctx.text;
        foreach (var feat in response.features)
            if (feat?.place_type != null && System.Array.IndexOf(feat.place_type, "district") >= 0)
                return feat.text;
        return null;
    }

    [Space]
    public string location;   // Stores the place name
    IEnumerator GetLocationName(float lat, float lon)
    {
        string url = $"{MapboxGeocodingBase}{lon},{lat}.json?access_token={mapboxToken}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                MapboxGeocodeResponse response = JsonUtility.FromJson<MapboxGeocodeResponse>(request.downloadHandler.text);
                if (response?.features?.Length > 0)
                {
                    string neighbourhood = ExtractNeighbourhoodFromMapbox(response);
                    string city          = ExtractCityFromMapbox(response);
                    string adminArea     = ExtractDistrictFromMapbox(response);
                    location = MapPointer.BuildLocation(neighbourhood, city, adminArea)
                             ?? response.features[0].place_name;
                    //SetLabelTextAndRebuild(showAreaName, location);
                }
            }
            else
            {
                Debug.LogError($"Error fetching location: {request.error}");
            }
        }
    }

    private void SetLabelTextAndRebuild(TextMeshProUGUI label, string value)
    {
        if (label == null)
            return;

        label.text = value ?? string.Empty;

        // Force immediate layout recalculation so ContentSizeFitter/LayoutGroups
        // resize and anchor against the new text on the same frame.
        Canvas.ForceUpdateCanvases();

        RectTransform rt = label.rectTransform;
        if (rt == null)
            return;

        // Rebuild from text up through immediate containers to stabilize anchoring.
        ForceRebuildFor(rt);
        if (rt.parent is RectTransform parentRt)
            ForceRebuildFor(parentRt);
        if (rt.parent != null && rt.parent.parent is RectTransform grandParentRt)
            ForceRebuildFor(grandParentRt);
    }

    private static void ForceRebuildFor(RectTransform rect)
    {
        if (rect == null)
            return;

        ContentSizeFitter fitter = rect.GetComponent<ContentSizeFitter>();
        if (fitter != null)
        {
            fitter.SetLayoutHorizontal();
            fitter.SetLayoutVertical();
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    public Vector2 LatLonToPixel(float latitude, float longitude, int zoomLevel)
    {
        float scale = 256f * Mathf.Pow(2f, zoomLevel);
        float x = (longitude + 180f) / 360f * scale;
        float sinLat = Mathf.Sin(latitude * Mathf.Deg2Rad);
        float y = (0.5f - Mathf.Log((1f + sinLat) / (1f - sinLat)) / (4f * Mathf.PI)) * scale;
        return new Vector2(x, y);
    }

    // ── Mapbox geocoding response classes ──────────────────────────────────

    [System.Serializable]
    public class MapboxGeocodeResponse
    {
        public MapboxFeature[] features;
    }

    [System.Serializable]
    public class MapboxFeature
    {
        public string[]        place_type;
        public string          place_name;
        public string          text;
        public float[]         bbox;    // [minLon, minLat, maxLon, maxLat] — may be null
        public float[]         center;  // [lon, lat]
        public MapboxContext[] context;
    }

    [System.Serializable]
    public class MapboxContext
    {
        public string id;
        public string text;
        public string short_code;
    }
}
