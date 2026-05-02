using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add to the loadingArt prefab root.
/// Shows when a map load starts, hides a few seconds after it completes.
///
/// Requires a CanvasGroup on this GameObject (add one — alpha controls visibility
/// so this component stays active and can always detect the next load cycle).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class MapLoadingArt : MonoBehaviour
{
    [Tooltip("Seconds to remain visible after the map finishes loading")]
    public float hideDelay = 1.2f;

    private CanvasGroup _group;
    private bool        _wasLoaded = true; // assume loaded so we don't show on unrelated scenes
    private float       _hideTimer = -1f;

    void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        SetVisible(false);
    }

    void Start()
    {
        // Don't show while the permission screen is still waiting for the user
        if (LocationPermissionScreen.instance != null && !LocationPermissionScreen.LocationGranted)
            return;  // _wasLoaded stays true; Update picks up the loading state once granted

        // Show immediately if a map load is already in progress at startup
        bool loaded = MapLoader.instance == null || MapLoader.instance.mapLoaded;
        _wasLoaded = loaded;
        if (!loaded) SetVisible(true);
    }

    void Update()
    {
        // Stay hidden while waiting for location permission
        if (LocationPermissionScreen.instance != null && !LocationPermissionScreen.LocationGranted)
            return;

        if (MapLoader.instance == null) return;

        bool loaded = MapLoader.instance.mapLoaded;

        // Loading started (mapLoaded flipped false)
        if (_wasLoaded && !loaded)
        {
            SetVisible(true);
            _hideTimer = -1f;
        }

        // Loading finished (mapLoaded flipped true) — start hide countdown
        if (!_wasLoaded && loaded)
            _hideTimer = hideDelay;

        // Countdown
        if (_hideTimer >= 0f)
        {
            _hideTimer -= Time.deltaTime;
            if (_hideTimer < 0f)
                SetVisible(false);
        }

        _wasLoaded = loaded;
    }

    void SetVisible(bool visible)
    {
        _group.alpha          = visible ? 1f : 0f;
        _group.blocksRaycasts = visible;
    }
}
