using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Drives one or more ink-blotch Images as a screen transition.
///
/// Setup:
///   1. Create one or more fullscreen UI Images as overlays (stretch to fill Canvas).
///   2. Give each its own Material using the Custom/InkBlotch shader.
///      Vary Noise Scale / Ink Color between them for a layered look.
///   3. Attach this script to any persistent GameObject and assign all Images to Overlays.
/// </summary>
public class InkTransition : MonoBehaviour
{
    public static InkTransition instance;

    [Tooltip("All Images with InkBlotch materials — each gets its own instanced material")]
    public Image[] overlays;

    [Tooltip("Time (seconds) for ink to cover the screen")]
    public float coverDuration = 0.45f;

    [Tooltip("Time (seconds) for ink to recede after the content switch")]
    public float revealDuration = 0.55f;

    [Tooltip("Ease curve for the cover phase")]
    public AnimationCurve coverCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("Ease curve for the reveal phase")]
    public AnimationCurve revealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("Brief pause at full coverage before revealing (lets the new content begin loading)")]
    public float holdDuration = 0.15f;

    private Material[] mats;
    private static readonly int ThresholdID = Shader.PropertyToID("_Threshold");
    private bool isTransitioning;

    private void Awake()
    {
        instance = this;

        if (overlays == null || overlays.Length == 0) return;

        mats = new Material[overlays.Length];
        for (int i = 0; i < overlays.Length; i++)
        {
            if (overlays[i] == null) continue;
            mats[i] = new Material(overlays[i].material);
            overlays[i].material = mats[i];
            overlays[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Runs ink-in → onCovered() → ink-out across all overlay Images.
    /// Safe to call while a transition is already running (ignored).
    /// </summary>
    /// <param name="readyToReveal">Optional gate — reveal is held until this returns true.</param>
    public void DoTransition(System.Action onCovered, System.Func<bool> readyToReveal = null)
    {
        if (isTransitioning || mats == null || mats.Length == 0) return;
        StartCoroutine(Run(onCovered, readyToReveal));
    }

    /// <summary>
    /// Starts fully covered and plays only the reveal (clearing) phase.
    /// Use this when a screen opens and you want to wipe the ink away to show it.
    /// </summary>
    public void RevealOnly()
    {
        if (isTransitioning || mats == null || mats.Length == 0) return;
        StartCoroutine(RunRevealOnly(null));
    }

    /// <summary>Starts fully covered, waits for readyToReveal(), then reveals.</summary>
    public void RevealOnly(System.Func<bool> readyToReveal)
    {
        if (isTransitioning || mats == null || mats.Length == 0) return;
        StartCoroutine(RunRevealOnly(readyToReveal));
    }

    /// <summary>Inspector-bindable: reveals only after the map has finished loading.
    /// Passes immediately if the location permission screen is still showing (no map yet).</summary>
    public void RevealOnlyWhenMapLoaded()
    {
        RevealOnly(() =>
            (MapLoader.instance != null && MapLoader.instance.mapLoaded) ||
            (LocationPermissionScreen.instance != null && !LocationPermissionScreen.LocationGranted));
    }

    private IEnumerator RunRevealOnly(System.Func<bool> readyToReveal = null)
    {
        isTransitioning = true;
        SetThreshold(1f);
        SetOverlaysActive(true);

        // Wait one frame so the overlay renders fully covered before animating
        yield return null;

        if (readyToReveal != null)
            yield return new WaitUntil(readyToReveal);

        yield return Animate(1f, 0f, revealDuration, revealCurve);

        SetThreshold(0f);
        SetOverlaysActive(false);
        isTransitioning = false;
    }

    private IEnumerator Run(System.Action onCovered, System.Func<bool> readyToReveal = null)
    {
        isTransitioning = true;
        SetOverlaysActive(true);

        // ── Cover ──────────────────────────────────────────────────────────
        yield return Animate(0f, 1f, coverDuration, coverCurve);

        // ── Midpoint ───────────────────────────────────────────────────────
        SetThreshold(1f);
        onCovered?.Invoke();
        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

        // Wait until the caller signals the content is ready (e.g. map download done)
        if (readyToReveal != null)
            yield return new WaitUntil(readyToReveal);

        // ── Reveal ─────────────────────────────────────────────────────────
        yield return Animate(1f, 0f, revealDuration, revealCurve);

        SetThreshold(0f);
        SetOverlaysActive(false);
        isTransitioning = false;
    }

    private IEnumerator Animate(float from, float to, float duration, AnimationCurve curve)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetThreshold(Mathf.Lerp(from, to, curve.Evaluate(t / duration)));
            yield return null;
        }
    }

    private void SetThreshold(float value)
    {
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null) mats[i].SetFloat(ThresholdID, value);
    }

    private void SetOverlaysActive(bool active)
    {
        for (int i = 0; i < overlays.Length; i++)
            if (overlays[i] != null) overlays[i].gameObject.SetActive(active);
    }
}
