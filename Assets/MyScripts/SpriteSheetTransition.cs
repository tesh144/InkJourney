using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a fullscreen ink/liquid transition using a sprite sheet.
/// The sheet should be laid out left-to-right, top-to-bottom (row-major).
///
/// Cover  = plays frames forward  (empty → covered)
/// Reveal = plays frames backward (covered → empty)
///
/// Wire up the same way as InkTransition — assign to MapLoader/InkRevealOnEnable etc.
/// </summary>
public class SpriteSheetTransition : MonoBehaviour
{
    [System.Serializable]
    public class SpriteSheetConfig
    {
        public Texture2D texture;
        [Tooltip("Number of columns in this sprite sheet")]
        public int columns = 6;
        [Tooltip("Number of rows in this sprite sheet")]
        public int rows = 4;
        [Tooltip("Total number of frames in this sprite sheet (may be less than columns × rows)")]
        public int frameCount = 24;
    }

    [System.Serializable]
    public class TransitionSet
    {
        public string name = "Ink Set";
        [Tooltip("Sprite sheets for this set — each has its own columns, rows, and frame count")]
        public SpriteSheetConfig[] spriteSheets;
        [Tooltip("Frames per second")]
        public float fps = 30f;
        [Tooltip("Brief pause at full coverage before revealing")]
        public float holdDuration = 0.15f;
    }

    public static SpriteSheetTransition instance;

    [Header("Sprite Sheets (Legacy)")]
    [Tooltip("Legacy: used when no Transition Sets are configured")]
    public Texture2D[] spriteSheets;

    [Tooltip("Number of columns in each sprite sheet (legacy)")]
    public int columns = 6;

    [Tooltip("Number of rows in each sprite sheet (legacy)")]
    public int rows = 4;

    [Tooltip("Total number of frames per sheet (legacy)")]
    public int frameCount = 24;

    [Header("Playback")]
    [Tooltip("Frames per second")]
    public float fps = 30f;

    [Tooltip("Brief pause at full coverage before revealing")]
    public float holdDuration = 0.15f;

    [Header("Transition Sets")]
    [Tooltip("Optional named presets. If provided, these override legacy fields above.")]
    public TransitionSet[] transitionSets;
    [Tooltip("Default preset index when using transitionSets")]
    public int defaultSetIndex = 0;
    [Tooltip("Choose a random preset each time a transition starts")]
    public bool randomizeSetEachTransition = false;

    [Header("References")]
    [Tooltip("Fullscreen RawImage overlays — each gets a different random sheet for a layered look")]
    public RawImage[] overlays;

    private bool isTransitioning;
    private TransitionSet _activeSet;

    // Per-overlay active sheet config, populated by PickRandomSheets
    private SpriteSheetConfig[] _activeConfigs;

    private void Awake()
    {
        instance = this;
        SetOverlaysActive(false);
        _activeSet = BuildActiveSet();
    }

    // ── Public API (mirrors InkTransition) ────────────────────────────────

    /// <summary>Cover → onCovered() → wait until readyToReveal() → reveal.</summary>
    /// <param name="readyToReveal">Optional gate — reveal is held until this returns true.</param>
    public void DoTransition(System.Action onCovered, System.Func<bool> readyToReveal = null)
    {
        if (isTransitioning || overlays == null || overlays.Length == 0) return;
        StartCoroutine(RunFull(onCovered, readyToReveal));
    }

    /// <summary>Snaps to fully covered, then plays the reveal animation.</summary>
    public void RevealOnly()
    {
        if (isTransitioning || overlays == null || overlays.Length == 0) return;
        StartCoroutine(RunReveal(null));
    }

    /// <summary>Snaps to fully covered, waits for readyToReveal(), then reveals.</summary>
    public void RevealOnly(System.Func<bool> readyToReveal)
    {
        if (isTransitioning || overlays == null || overlays.Length == 0) return;
        StartCoroutine(RunReveal(readyToReveal));
    }

    /// <summary>Inspector-bindable: reveals only after the map and labels have finished loading.</summary>
    public void RevealOnlyWhenMapLoaded()
    {
        RevealOnly(() =>
            MapLoader.instance != null && MapLoader.instance.mapLoaded &&
            (MapLabelSpawner.instance == null || MapLabelSpawner.instance.LabelsReady));
    }

    public void SetTransitionSet(int index)
    {
        if (transitionSets == null || transitionSets.Length == 0)
            return;

        defaultSetIndex = Mathf.Clamp(index, 0, transitionSets.Length - 1);
        _activeSet = BuildActiveSet();
    }

    // ── Coroutines ────────────────────────────────────────────────────────

    private IEnumerator RunFull(System.Action onCovered, System.Func<bool> readyToReveal = null)
    {
        isTransitioning = true;
        _activeSet = BuildActiveSet();
        PickRandomSheets(_activeSet);
        SetOverlaysActive(true);

        yield return PlayFrames(_activeSet, forward: true);

        ShowFrameAtT(1f);
        onCovered?.Invoke();
        if (_activeSet.holdDuration > 0f) yield return new WaitForSeconds(_activeSet.holdDuration);

        if (readyToReveal != null)
            yield return new WaitUntil(readyToReveal);

        yield return PlayFrames(_activeSet, forward: false);

        SetOverlaysActive(false);
        isTransitioning = false;
    }

    private IEnumerator RunReveal(System.Func<bool> readyToReveal = null)
    {
        isTransitioning = true;
        _activeSet = BuildActiveSet();
        PickRandomSheets(_activeSet);
        ShowFrameAtT(1f);                    // snap to fully covered
        SetOverlaysActive(true);
        yield return null;                   // render one frame fully covered

        if (readyToReveal != null)
            yield return new WaitUntil(readyToReveal);

        yield return PlayFrames(_activeSet, forward: false);

        SetOverlaysActive(false);
        isTransitioning = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private TransitionSet BuildActiveSet()
    {
        if (transitionSets != null && transitionSets.Length > 0)
        {
            int index = randomizeSetEachTransition
                ? Random.Range(0, transitionSets.Length)
                : Mathf.Clamp(defaultSetIndex, 0, transitionSets.Length - 1);
            TransitionSet picked = transitionSets[index];

            // Defensive clone with clamped values so bad inspector values can't break playback.
            var sanitizedSheets = picked.spriteSheets != null
                ? System.Array.ConvertAll(picked.spriteSheets, s => new SpriteSheetConfig
                  {
                      texture    = s?.texture,
                      columns    = Mathf.Max(1, s != null ? s.columns    : 6),
                      rows       = Mathf.Max(1, s != null ? s.rows       : 4),
                      frameCount = Mathf.Max(1, s != null ? s.frameCount : 24)
                  })
                : null;

            return new TransitionSet
            {
                name         = string.IsNullOrWhiteSpace(picked.name) ? $"Set {index}" : picked.name,
                spriteSheets = sanitizedSheets,
                fps          = Mathf.Max(1f, picked.fps),
                holdDuration = Mathf.Max(0f, picked.holdDuration)
            };
        }

        // Legacy fallback: wrap bare Texture2D[] with the shared grid settings.
        SpriteSheetConfig[] legacyConfigs = null;
        if (spriteSheets != null && spriteSheets.Length > 0)
        {
            legacyConfigs = System.Array.ConvertAll(spriteSheets, t => new SpriteSheetConfig
            {
                texture    = t,
                columns    = Mathf.Max(1, columns),
                rows       = Mathf.Max(1, rows),
                frameCount = Mathf.Max(1, frameCount)
            });
        }

        return new TransitionSet
        {
            name         = "Legacy",
            spriteSheets = legacyConfigs,
            fps          = Mathf.Max(1f, fps),
            holdDuration = Mathf.Max(0f, holdDuration)
        };
    }

    private void PickRandomSheets(TransitionSet set)
    {
        if (overlays == null) return;

        _activeConfigs = new SpriteSheetConfig[overlays.Length];

        if (set == null || set.spriteSheets == null || set.spriteSheets.Length == 0) return;

        for (int i = 0; i < overlays.Length; i++)
        {
            if (overlays[i] == null) continue;
            var cfg = set.spriteSheets[Random.Range(0, set.spriteSheets.Length)];
            _activeConfigs[i]     = cfg;
            overlays[i].texture   = cfg?.texture;
        }
    }

    private void SetOverlaysActive(bool active)
    {
        foreach (var o in overlays)
            if (o != null) o.gameObject.SetActive(active);
    }

    /// <summary>
    /// Plays the transition driven by normalized time so each overlay can use its own frameCount.
    /// </summary>
    private IEnumerator PlayFrames(TransitionSet set, bool forward)
    {
        if (set == null) yield break;

        // Drive duration from the master frame count (max across active configs).
        int masterFrames = GetMasterFrameCount();
        float duration   = masterFrames / Mathf.Max(set.fps, 1f);
        float elapsed    = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t       = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            ShowFrameAtT(forward ? smoothT : 1f - smoothT);
            yield return null;
        }

        ShowFrameAtT(forward ? 1f : 0f);
    }

    /// <summary>Returns the largest frameCount across active overlay configs.</summary>
    private int GetMasterFrameCount()
    {
        int master = 1;
        if (_activeConfigs == null) return master;
        foreach (var cfg in _activeConfigs)
            if (cfg != null) master = Mathf.Max(master, cfg.frameCount);
        return master;
    }

    /// <summary>
    /// Maps a normalized time t [0,1] to the correct UV rect on each overlay
    /// using that overlay's own SpriteSheetConfig (columns, rows, frameCount).
    /// </summary>
    private void ShowFrameAtT(float t)
    {
        if (overlays == null) return;

        float screenAspect = (float)Screen.width / Screen.height;

        for (int i = 0; i < overlays.Length; i++)
        {
            var o = overlays[i];
            if (o == null || o.texture == null) continue;

            var cfg = (_activeConfigs != null && i < _activeConfigs.Length) ? _activeConfigs[i] : null;
            if (cfg == null) continue;

            int cols       = Mathf.Max(1, cfg.columns);
            int rowCount   = Mathf.Max(1, cfg.rows);
            int frames     = Mathf.Max(1, cfg.frameCount);

            int index = Mathf.Clamp(Mathf.RoundToInt(t * (frames - 1)), 0, frames - 1);

            int   col   = index % cols;
            int   row   = index / cols;
            float cellW = 1f / cols;
            float cellH = 1f / rowCount;
            float cellX = col * cellW;
            float cellY = 1f - (row + 1) * cellH;

            // Aspect-ratio-correct "cover" crop so frames never squish
            float texCellW   = (float)o.texture.width  / cols;
            float texCellH   = (float)o.texture.height / rowCount;
            float cellAspect = texCellW / texCellH;

            float uvW, uvH, uvX, uvY;
            if (screenAspect > cellAspect)
            {
                // Screen wider than frame: fill width, crop top/bottom
                uvW = cellW;
                uvH = cellH * (cellAspect / screenAspect);
                uvX = cellX;
                uvY = cellY + (cellH - uvH) * 0.5f;
            }
            else
            {
                // Screen taller than frame (portrait): fill height, crop sides
                uvH = cellH;
                uvW = cellW * (screenAspect / cellAspect);
                uvX = cellX + (cellW - uvW) * 0.5f;
                uvY = cellY;
            }

            o.uvRect = new Rect(uvX, uvY, uvW, uvH);
        }
    }
}
