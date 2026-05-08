using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.Events;
using TMPro;
using System.Collections;
using System.Linq;

public class MapPointer : MonoBehaviour
{
    // ── Existing ───────────────────────────────────────────────────────────
    public float latitude;
    public float longitude;
    public GameObject visuals;

    [Space]
    public RectTransform mapTransform;
    public float mapScale = 1.0f;

    [Header("Debug Info")]
    public float xPos;
    public float yPos;

    [Space]
    public string location;
    public TextMeshProUGUI textbox;

    // ── Entry (set by GoogleSheetsFetcher on spawn) ────────────────────────
    [HideInInspector] public GoogleSheetsFetcher.Entry entry;
    public bool IsFriendEntry { get; private set; }

    // ── Expansion ──────────────────────────────────────────────────────────
    [Header("Expansion")]
    [Tooltip("The child object whose height changes when this pointer is selected")]
    public RectTransform expandObject;
    public float defaultHeight = 60f;
    public float expandedHeight = 140f;
    [Tooltip("Max distance from screen centre in pixels to trigger expansion")]
    public float centerThresholdPixels = 150f;
    [Tooltip("How quickly the card expands/collapses (units per second)")]
    public float expandSpeed = 300f;
    [Tooltip("Scale of the pointer when expanded")]
    public float expandedScale = 1.25f;
    [Tooltip("How quickly the pointer scales up/down")]
    public float scaleSpeed = 4f;

    // ── Photo / Preview ────────────────────────────────────────────────────
    [Header("Photo / Preview")]
    public RawImage photoImage;
    [Tooltip("TMP text shown in the expanded area when no photo is available")]
    public TextMeshProUGUI previewText;
    public TextMeshProUGUI dateText;
    public TextMeshProUGUI distanceText;
    public TextMeshProUGUI likesText;
    public GameObject myStoryIndicator;
    public GameObject recentIndicator;

    // ── Proximity Styling ──────────────────────────────────────────────────
    [Header("Proximity Styling")]
    [Tooltip("GPS distance within which the pointer becomes interactive")]
    public float proximityRangeMeters = 50f;
    public Image backPanel;
    public GameObject outlineObject;
    public Color defaultTextColor = Color.white;
    public Color selectedTextColor = Color.white;
    public Color defaultPanelColor  = new Color(0.12f, 0.12f, 0.12f, 0.92f);
    public Color selectedPanelColor = new Color(0.16f, 0.80f, 0.82f, 1.00f);

    // ── Route Styling ──────────────────────────────────────────────────────
    [Header("Route Styling")]
    [Tooltip("Back-panel colour when a walking route is currently drawn to this pointer")]
    public Color routePanelColor = new Color(0.2f, 0.9f, 1f, 0.92f);
    [Tooltip("Title text colour when a walking route is currently drawn to this pointer")]
    public Color routeTextColor  = new Color(0.1f, 0.7f, 0.85f, 1f);

    // ── Journey Styling ────────────────────────────────────────────────────
    [Header("Journey Styling")]
    [Tooltip("Outline shown when this story is a chapter in the active journey and player is in proximity")]
    public GameObject journeyOutlineObject;
    [Tooltip("Indicator badge always shown when this story belongs to any journey")]
    public GameObject journeyIndicator;
    [Tooltip("Only enabled when this story is the next uncompleted chapter in the active journey")]
    public GameObject nextChapterIndicator;
    [Tooltip("Displays chapter number / total (e.g. '1/3'); shown instead of swappableIcon for journey chapters")]
    public TextMeshProUGUI chapterText;
    public Color chapterTextCompleteColor   = new Color(0.5f, 0.5f, 0.5f, 1f);
    public Color chapterTextIncompleteColor = Color.white;

    // ── Friend Styling ─────────────────────────────────────────────────────
    [Header("Friend Styling")]
    [Tooltip("Shown when this story was posted by a friend")]
    public GameObject friendIndicator;
    [Tooltip("Alternative outline shown instead of the default when posted by a friend")]
    public GameObject friendOutlineObject;
    [Tooltip("Title text colour when posted by a friend")]
    public Color friendTextColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Image component whose sprite swaps based on story type / sticker")]
    public Image swappableIcon;
    [Tooltip("Sprite shown when the story author is not a friend")]
    public Sprite defaultIconSprite;
    [Tooltip("Sprite shown when the story author is a friend")]
    public Sprite friendIconSprite;
    [Tooltip("Sprite shown for landmark entries")]
    public Sprite landmarkIconSprite;


    // ── Private ────────────────────────────────────────────────────────────
    private static MapPointer s_currentlyExpanded;
    private static MapPointer s_routePointer;

    private MapInputController _mapInputController;
    private GoogleSheetManager googleSheetManager;
    private Canvas rootCanvas;
    private float distanceToCenter = float.MaxValue;
    private bool isExpanded  = false;
    private bool photoLoaded = false;
    private Coroutine expandCoroutine;
    private float _visualScale     = 1f;
    private float _targetVisualScale = 1f;

    private readonly string apiKey = "AIzaSyDenug-6RiFj3ziSxtdrYQnS-qo0WhuBfI";

    [Header("Events")]
    public UnityEvent onPosted;
    public UnityEvent onExpanded;
    public UnityEvent onCollapsed;
    public UnityEvent onSelected;

    [Header("Static Mode")]
    [Tooltip("When enabled, all automatic lifecycle behaviour (GPS, expansion, coroutines) is suppressed. Use for library map pins.")]
    public bool staticMode = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (staticMode) return;
        visuals.SetActive(false);
    }

    private void OnEnable()
    {
        if (staticMode) return;
        rootCanvas = GetComponentInParent<Canvas>();
        _mapInputController = GetComponentInParent<MapInputController>() ?? MapInputController.instance;
        GPSManager.OnPositionSampled += OnGPSSampled;
        JourneyManager.onJourneyActivated   += OnJourneyStateChanged;
        JourneyManager.onJourneyDeactivated += OnJourneyStateChanged;
        StartCoroutine(WaitToStart());
    }

    private void OnDisable()
    {
        if (staticMode) return;
        GPSManager.OnPositionSampled -= OnGPSSampled;
        JourneyManager.onJourneyActivated   -= OnJourneyStateChanged;
        JourneyManager.onJourneyDeactivated -= OnJourneyStateChanged;

        if (s_currentlyExpanded == this)
            s_currentlyExpanded = null;

        SetExpanded(false);
    }

    public static void DeselectCurrent()
    {
        if (s_currentlyExpanded != null)
        {
            s_currentlyExpanded.SetExpanded(false);
            s_currentlyExpanded = null;
        }
        s_routePointer = null;
    }

    private void OnGPSSampled(float lat, float lon)
    {
        if (!visuals.activeSelf || latitude == 0 || longitude == 0) return;
        var rt = GetComponent<RectTransform>();
        if (rt != null) rt.anchoredPosition = GPSPositionToUnity(latitude, longitude);
    }

    IEnumerator WaitToStart()
    {
        yield return new WaitForSeconds(0.1f);
        if (!gameObject.activeInHierarchy) yield break;

        googleSheetManager = GetComponent<GoogleSheetManager>();

        if (googleSheetManager != null)
            StartCoroutine(WaitForGPSData());
        else
            Debug.LogError("[MapPointer] GoogleSheetManager not found!");
    }

    IEnumerator WaitForGPSData()
    {
        while (googleSheetManager.latitude == 0 || googleSheetManager.longitude == 0)
        {
            yield return new WaitForSeconds(0.5f);
            if (!gameObject.activeInHierarchy) yield break;
        }

        UpdatePosition();
        if (gameObject.activeInHierarchy)
            StartCoroutine(GetLocationName(latitude, longitude));
    }

    // ── Position ───────────────────────────────────────────────────────────

    public void UpdatePosition()
    {
        if (latitude == 0 || longitude == 0)
        {
            Debug.LogWarning("[MapPointer] Coordinates are 0, skipping position update.");
            return;
        }

        Vector2 unityPos = GPSPositionToUnity(latitude, longitude);

        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
            rt.anchoredPosition = unityPos;
        else
            transform.localPosition = new Vector3(unityPos.x, unityPos.y, 0);

        visuals.SetActive(true);
        RefreshOwnerVisuals();
    }

    private Vector2 GPSPositionToUnity(float lat, float lon)
    {
        float centerLat = GPSManager.Instance.latitude;
        float centerLon = GPSManager.Instance.longitude;
        int   zoom      = MapLoader.instance.CurrentMapZoom;

        Vector2 p = MapLoader.instance.LatLonToPixel(lat,       lon, zoom);
        Vector2 c = MapLoader.instance.LatLonToPixel(centerLat, centerLon, zoom);

        // 1280px crop from 4× tiles = 320 virtual 256-px tile pixels span the content
        float contentWidth = mapTransform != null ? mapTransform.rect.width : 640f;
        float scale        = contentWidth / 320f;

        return new Vector2((p.x - c.x) * scale, (c.y - p.y) * scale);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (staticMode) return;
        if (!visuals.activeSelf) return;

        UpdateDistanceToCenter();
        UpdateExpansionState();
        UpdateScale();
        UpdateColors();
    }

    private void UpdateDistanceToCenter()
    {
        if (rootCanvas == null) return;
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, transform.position);
        distanceToCenter = Vector2.Distance(screenPos, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
    }

    private void UpdateExpansionState()
    {
        if (!MapInputController.IsExploreActive)
        {
            if (isExpanded) SetExpanded(false);
            return;
        }

        if (MapInputController.IsFullyZoomedOut)
        {
            if (isExpanded) SetExpanded(false);
            return;
        }

        if (!IsWithinProximity())
        {
            if (isExpanded) SetExpanded(false);
            return;
        }

        bool withinThreshold  = distanceToCenter < centerThresholdPixels;
        bool closerThanCurrent = s_currentlyExpanded == null
                              || s_currentlyExpanded == this
                              || distanceToCenter < s_currentlyExpanded.distanceToCenter;

        if (withinThreshold && closerThanCurrent)
        {
            if (!isExpanded) SetExpanded(true);
        }
        else
        {
            if (isExpanded) SetExpanded(false);
        }
    }

    private void SetExpanded(bool expand)
    {
        if (expand)
        {
            // Collapse the previous selection
            if (s_currentlyExpanded != null && s_currentlyExpanded != this)
                s_currentlyExpanded.SetExpanded(false);

            s_currentlyExpanded = this;
            isExpanded = true;
            onExpanded?.Invoke();

            transform.SetAsLastSibling();

            if (expandObject != null)
                AnimateHeight(expandedHeight);

            _targetVisualScale = expandedScale;

            // Always show preview text
            if (previewText != null)
            {
                previewText.text = entry?.Content ?? "";
                previewText.gameObject.SetActive(true);
            }

            if (dateText != null)
            {
                dateText.text = (entry != null && entry.Created > 0)
                    ? System.DateTimeOffset.FromUnixTimeSeconds(entry.Created).LocalDateTime.ToString("dd/MM/yyyy")
                    : "";
                dateText.gameObject.SetActive(true);
            }

            // Show photo below if available
            bool hasPhoto = entry != null && !string.IsNullOrEmpty(entry.PhotoUrl);
            if (hasPhoto)
            {
                if (!photoLoaded)
                    MapLoader.instance.StartCoroutine(LoadPhoto(entry.PhotoUrl));
                else if (photoImage != null)
                    photoImage.gameObject.SetActive(true);
            }
            else if (photoImage != null)
                photoImage.gameObject.SetActive(false);
        }
        else
        {
            if (s_currentlyExpanded == this)
                s_currentlyExpanded = null;

            isExpanded = false;
            onCollapsed?.Invoke();

            if (expandObject != null)
                AnimateHeight(defaultHeight);

            _targetVisualScale = 1f;

            if (photoImage != null)  photoImage.gameObject.SetActive(false);
            if (previewText != null) previewText.gameObject.SetActive(false);
            if (dateText != null)    dateText.gameObject.SetActive(false);
        }
    }

    private void AnimateHeight(float targetHeight)
    {
        if (expandCoroutine != null) StopCoroutine(expandCoroutine);
        expandCoroutine = StartCoroutine(SmoothHeight(targetHeight));
    }

    private IEnumerator SmoothHeight(float targetHeight)
    {
        while (!Mathf.Approximately(expandObject.sizeDelta.y, targetHeight))
        {
            var sz = expandObject.sizeDelta;
            sz.y = Mathf.MoveTowards(sz.y, targetHeight, expandSpeed * Time.deltaTime);
            expandObject.sizeDelta = sz;
            yield return null;
        }
        var final = expandObject.sizeDelta;
        final.y = targetHeight;
        expandObject.sizeDelta = final;
        expandCoroutine = null;
    }

    // Counter-scales against the parent map zoom so the pointer never changes
    // visual size when the map is pinch-zoomed. The expand animation still works
    // because _targetVisualScale drives the logical size independently.
    private void UpdateScale()
    {
        _visualScale = Mathf.Lerp(_visualScale, _targetVisualScale, scaleSpeed * Time.deltaTime);

        RectTransform mapContent = _mapInputController?.mapContent;
        float mapScale = mapContent != null ? mapContent.localScale.x : 1f;

        float t = MapLoader.instance != null ? MapLoader.instance.pointerCounterScale : 1f;
        float divisor = Mathf.Lerp(1f, mapScale, t);
        if (divisor > 0f)
            transform.localScale = Vector3.one * (_visualScale / divisor);

        // Counter-rotate against mapContent so the pointer always faces up
        float mapRotZ = mapContent != null ? mapContent.eulerAngles.z : 0f;
        transform.localEulerAngles = new Vector3(0f, 0f, -mapRotZ);
    }

    private bool IsJourneyChapter() =>
        entry != null &&
        JourneyManager.instance?.activeJourney?.Chapters?.Find(c => c.StoryId == entry.ID) != null;

    private void OnJourneyStateChanged() { UpdateColors(); RefreshOwnerVisuals(); }

    private void UpdateColors()
    {
        bool selected       = IsWithinProximity();
        bool hasRoute       = s_routePointer == this;
        bool isJourneyChapter = IsJourneyChapter();

        if (textbox != null)
            textbox.color = hasRoute  ? routeTextColor
                          : selected  ? selectedTextColor
                          : IsFriendEntry ? friendTextColor
                          : defaultTextColor;

        if (backPanel != null)
            backPanel.color = hasRoute ? routePanelColor
                            : selected ? selectedPanelColor
                            : defaultPanelColor;

        if (journeyOutlineObject != null)
            journeyOutlineObject.SetActive(selected && isJourneyChapter);

        if (outlineObject != null)
            outlineObject.SetActive(selected && !IsFriendEntry && !isJourneyChapter);

        if (friendOutlineObject != null)
            friendOutlineObject.SetActive(selected && IsFriendEntry && !isJourneyChapter);

        if (distanceText != null && GPSManager.Instance != null)
        {
            float dy = (latitude  - GPSManager.Instance.latitude)  * 111320f;
            float dx = (longitude - GPSManager.Instance.longitude) * (111320f * Mathf.Cos(latitude * Mathf.Deg2Rad));
            int metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
            distanceText.text = metres > 999 ? $"{Mathf.RoundToInt(metres / 1000f)}k" : $"{metres}m";
        }
    }

    /// <summary>
    /// Bind an entry and refresh all owner-based indicators (friend, mine, recent, icon swap).
    /// Safe to call on a library pin where position/GPS logic should not run.
    /// </summary>
    public void BindEntry(GoogleSheetsFetcher.Entry e)
    {
        entry = e;
        if (textbox != null)
        {
            textbox.text = e?.Title ?? "";
            var font = FontManager.instance?.GetFont(e?.FontID ?? 0);
            if (font != null) textbox.font = font;
        }
        RefreshOwnerVisuals();
    }

    /// <summary>Directly apply route-attached colors — for static-mode previews that skip Update.</summary>
    public void ApplyRouteColors()
    {
        if (textbox   != null) textbox.color   = routeTextColor;
        if (backPanel != null) backPanel.color  = routePanelColor;
    }

    private void RefreshOwnerVisuals()
    {
        if (myStoryIndicator != null)
        {
            bool isOwner = entry != null && (UserProfileManager.instance != null
                ? UserProfileManager.instance.IsCurrentUser(entry.User)
                : entry.User == SystemInfo.deviceUniqueIdentifier);
            myStoryIndicator.SetActive(isOwner);
        }

        if (recentIndicator != null)
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool isRecent = entry != null && (now - entry.Created) < 3600;
            bool isUnread = entry != null && !HasReadStory(entry.ID);
            recentIndicator.SetActive(isRecent && isUnread);
        }

        IsFriendEntry = entry != null && FriendsManager.IsFriend(entry.User);

        if (friendIndicator != null)
            friendIndicator.SetActive(IsFriendEntry);

        // Resolve journey membership (active journey first, then any journey)
        JourneyEntry foundJourney = null;
        JourneyEntry.ChapterDef foundChapter = null;
        if (entry != null && GoogleSheetsFetcher.instance?.journeysList != null)
        {
            if (JourneyManager.instance?.activeJourney != null)
            {
                var ch = JourneyManager.instance.activeJourney.Chapters?.Find(c => c.StoryId == entry.ID);
                if (ch != null) { foundJourney = JourneyManager.instance.activeJourney; foundChapter = ch; }
            }
            if (foundJourney == null)
            {
                foreach (var j in GoogleSheetsFetcher.instance.journeysList)
                {
                    if (j?.Chapters == null) continue;
                    var ch = j.Chapters.Find(c => c.StoryId == entry.ID);
                    if (ch != null) { foundJourney = j; foundChapter = ch; break; }
                }
            }
        }

        bool isAnyJourneyChapter = foundJourney != null;

        // Determine completion and next-chapter state within the active journey
        bool isCompleted    = false;
        bool isNextChapter  = false;
        if (isAnyJourneyChapter && JourneyManager.instance?.activeJourney?.ID == foundJourney.ID)
        {
            var completedIds = JourneyManager.instance.GetCompletedChapterIds(foundJourney.ID);
            isCompleted = completedIds.Contains(foundChapter.Id);

            if (!isCompleted)
            {
                bool allPriorDone = true;
                foreach (var c in foundJourney.Chapters)
                {
                    if (string.IsNullOrEmpty(c.StoryId)) continue;
                    if (c.Order >= foundChapter.Order) continue; // not a prerequisite — skip
                    if (!completedIds.Contains(c.Id)) { allPriorDone = false; break; }
                }
                isNextChapter = allPriorDone;
            }
        }

        if (journeyIndicator != null)
            journeyIndicator.SetActive(isAnyJourneyChapter);

        if (nextChapterIndicator != null)
            nextChapterIndicator.SetActive(isNextChapter);

        if (chapterText != null)
        {
            chapterText.gameObject.SetActive(isAnyJourneyChapter);
            if (isAnyJourneyChapter)
            {
                chapterText.text  = $"{foundChapter.Order + 1}/{foundJourney.Chapters.Count}";
                chapterText.color = isCompleted ? chapterTextCompleteColor : chapterTextIncompleteColor;
            }
        }

        if (swappableIcon != null)
        {
            swappableIcon.gameObject.SetActive(!isAnyJourneyChapter);

            if (!isAnyJourneyChapter)
            {
                Sprite stickerSprite = (entry != null && entry.StickerID >= 1 && StickerManager.instance != null)
                    ? StickerManager.instance.GetSticker(entry.StickerID)
                    : null;

                if (stickerSprite != null)
                    swappableIcon.sprite = stickerSprite;
                else if (entry != null && GoogleSheetsFetcher.IsLandmark(entry) && landmarkIconSprite != null)
                    swappableIcon.sprite = landmarkIconSprite;
                else if (IsFriendEntry && friendIconSprite != null)
                    swappableIcon.sprite = friendIconSprite;
                else if (defaultIconSprite != null)
                    swappableIcon.sprite = defaultIconSprite;
            }
        }

        RefreshLikeDisplay();
    }

    public void RefreshLikeDisplay()
    {
        if (likesText == null)
            return;

        int likes = entry != null ? Mathf.Max(0, entry.Saves) : 0;
        likesText.text = CompactCountFormatter.FormatLikes(likes);
    }

    // ── Photo ──────────────────────────────────────────────────────────────

    private IEnumerator LoadPhoto(string url)
    {
        if (photoImage != null) photoImage.gameObject.SetActive(false);

        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                photoLoaded = true;
                if (photoImage != null)
                {
                    photoImage.texture = ((DownloadHandlerTexture)req.downloadHandler).texture;
                    if (isExpanded) photoImage.gameObject.SetActive(true);
                }
            }
            else
            {
                Debug.LogWarning($"[MapPointer] Photo load failed: {req.error}");
            }
        }
    }

    // ── Tap ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from the pointer's Button OnClick.
    /// - If already expanded and in range: open the story.
    /// - If in range but not expanded: smooth-scroll to centre (auto-expand follows).
    /// - If out of range: smooth-scroll to centre, show the out-of-range popup, draw walking route.
    /// </summary>
    public void OnTapped()
    {
        // Block taps while the out-of-range popup is open (blocker panel handles scroll/zoom)
        if (MapPointerPopup.instance != null && MapPointerPopup.instance.gameObject.activeSelf) return;

        // Collapse any other expanded pointer immediately on tap.
        if (s_currentlyExpanded != null && s_currentlyExpanded != this)
            s_currentlyExpanded.SetExpanded(false);

        // Stop follow-mode recentering so it doesn't fight the centering coroutine.
        MainMapUserCursorController.Instance?.SetFollowMode(false);

        // Reset rotation to north so the centering formula is always axis-aligned.
        MapInputController.instance?.ResetRotation();

        // Always centre the map on this pointer, regardless of state.
        MapLoader.instance?.resetScrollRect?.LerpToCenterTarget(transform);

        if (isExpanded && IsWithinProximity())
        {
            if (entry != null) MarkStoryRead(entry.ID);

            // Auto-activate a journey when player taps the first chapter pin without a journey active.
            // Popup is deferred until the story panel closes so it isn't immediately covered.
            if (entry != null && JourneyManager.instance != null && JourneyManager.instance.activeJourney == null)
            {
                var journey = JourneyManager.instance.GetJourneyForFirstChapter(entry.ID);
                if (journey != null)
                {
                    JourneyManager.instance.ActivateJourney(journey, showPopup: false);
                    JourneyManager.instance.pendingJourneyPopup = journey;
                }
            }

            s_routePointer = this;
            onSelected?.Invoke();
            UpdateColors();
            // Journey chapters don't draw a map_pointer route — the journey route takes priority
            if (MapRouteManager.instance != null && GPSManager.Instance != null && !IsJourneyChapter())
                MapRouteManager.instance.DrawRoute(
                    MapRouteManager.MapPointerRouteType,
                    GPSManager.Instance.latitude,
                    GPSManager.Instance.longitude,
                    latitude,
                    longitude);
            MapPointerPopup.instance?.Hide();
            googleSheetManager.TurnOnStory();
            return;
        }

        // Second tap on the already-routed pointer clears the route
        if (s_routePointer == this)
        {
            s_routePointer = null;
            MapRouteManager.instance?.ClearRoutes(MapRouteManager.MapPointerRouteType);
            MapPointerPopup.instance?.Hide();
            return;
        }

        // Popup only when out of range
        if (!IsWithinProximity())
            MapPointerPopup.instance?.Show(this);

        // Draw a route to the tapped pointer — unless the journey route already covers it
        if (MapRouteManager.instance != null && GPSManager.Instance != null && !IsJourneyChapter())
        {
            s_routePointer = this;
            onSelected?.Invoke();
            UpdateColors();
            MapRouteManager.instance.DrawRoute(
                MapRouteManager.MapPointerRouteType,
                GPSManager.Instance.latitude,
                GPSManager.Instance.longitude,
                latitude,
                longitude);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    public bool IsWithinProximity()
    {
        if (GPSManager.Instance == null) return false;

        float dy = (latitude  - GPSManager.Instance.latitude)  * 111320f;
        float dx = (longitude - GPSManager.Instance.longitude) * (111320f * Mathf.Cos(latitude * Mathf.Deg2Rad));

        return Mathf.Sqrt(dx * dx + dy * dy) <= proximityRangeMeters;
    }

    // ── Location formatting ────────────────────────────────────────────────

    public static string BuildLocation(string neighbourhood, string city, string adminArea = null)
    {
        if (!string.IsNullOrEmpty(adminArea) && adminArea.StartsWith("Greater ", System.StringComparison.OrdinalIgnoreCase))
            adminArea = adminArea.Substring(8);

        // Both neighbourhood and city present → "Neighbourhood, City"
        if (!string.IsNullOrEmpty(neighbourhood) && !string.IsNullOrEmpty(city) && neighbourhood != city)
            return $"{neighbourhood}, {city}";

        // No neighbourhood but have city + broader admin area → "City, AdminArea"
        if (string.IsNullOrEmpty(neighbourhood) && !string.IsNullOrEmpty(city))
        {
            if (!string.IsNullOrEmpty(adminArea) && adminArea != city)
                return $"{city}, {adminArea}";
            return city;
        }

        return neighbourhood ?? city;
    }

    // ── Location name ──────────────────────────────────────────────────────

    IEnumerator GetLocationName(float lat, float lon)
    {
        string token = MapLoader.instance?.mapboxToken ?? "";
        string url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?access_token={token}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                MapboxGeocodeResponse response = JsonUtility.FromJson<MapboxGeocodeResponse>(request.downloadHandler.text);
                if (response?.features?.Length > 0)
                {
                    string neighbourhood = null, city = null, adminArea = null;
                    var first = response.features[0];

                    if (first.place_type != null &&
                        (System.Array.IndexOf(first.place_type, "neighborhood") >= 0
                      || System.Array.IndexOf(first.place_type, "locality") >= 0))
                        neighbourhood = first.text;

                    if (first.context != null)
                        foreach (var ctx in first.context)
                        {
                            if (city == null && ctx.id != null &&
                                (ctx.id.StartsWith("place.") || ctx.id.StartsWith("locality.")))
                                city = ctx.text;
                            if (adminArea == null && ctx.id != null && ctx.id.StartsWith("district."))
                                adminArea = ctx.text;
                        }

                    if (city == null)
                        foreach (var feat in response.features)
                            if (feat.place_type != null && System.Array.IndexOf(feat.place_type, "place") >= 0)
                            { city = feat.text; break; }

                    location = BuildLocation(neighbourhood, city, adminArea) ?? first.place_name;
                    if (entry != null) entry.cachedLocation = location;
                }
            }
            else
            {
                Debug.LogError($"[MapPointer] Location fetch error: {request.error}");
            }
        }
    }

    // ── Read tracking ──────────────────────────────────────────────────────

    private const string ReadStoriesKey = "ReadStories";

    private static bool HasReadStory(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return PlayerPrefs.GetString(ReadStoriesKey, "").Contains($"|{id}|");
    }

    private static void MarkStoryRead(string id)
    {
        if (string.IsNullOrEmpty(id) || HasReadStory(id)) return;
        string current = PlayerPrefs.GetString(ReadStoriesKey, "");
        PlayerPrefs.SetString(ReadStoriesKey, current + $"|{id}|");
        PlayerPrefs.Save();
    }

    // ── JSON ───────────────────────────────────────────────────────────────

    [System.Serializable] public class MapboxGeocodeResponse { public MapboxFeature[] features; }
    [System.Serializable] public class MapboxFeature         { public string[] place_type; public string place_name; public string text; public float[] bbox; public float[] center; public MapboxContext[] context; }
    [System.Serializable] public class MapboxContext         { public string id; public string text; public string short_code; }
}
