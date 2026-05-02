using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Plays a sprite sheet animation on a single RawImage.
/// Assign a sprite sheet, configure columns/rows/frameCount, then call Play() or
/// wire up PlayReveal / PlayHide to UnityEvents. The bool direction is also Inspector-settable.
/// </summary>
public class SpriteSheetPlayer : MonoBehaviour
{
    [Header("Target")]
    public RawImage target;

    [Header("Sprite Sheet")]
    public Texture2D spriteSheet;
    [Min(1)] public int columns   = 6;
    [Min(1)] public int rows      = 4;
    [Min(1)] public int frameCount = 24;

    [Header("Playback")]
    public float fps = 30f;
    [Tooltip("True = reveal (covered → clear). False = hide (clear → covered).")]
    public bool reveal = true;
    [Tooltip("Play automatically when this component is enabled.")]
    public bool playOnEnable = false;

    [Header("Events")]
    public UnityEvent onComplete;

    public bool IsPlaying { get; private set; }

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    public void Play()           => Play(reveal);
    public void PlayReveal()     => Play(true);
    public void PlayHide()       => Play(false);
    public void SetReveal(bool v) { reveal = v; }

    public void Play(bool revealDirection)
    {
        if (target == null) return;
        if (IsPlaying) StopAllCoroutines();
        StartCoroutine(Animate(revealDirection));
    }

    private IEnumerator Animate(bool revealDirection)
    {
        IsPlaying = true;

        if (spriteSheet != null)
            target.texture = spriteSheet;

        target.gameObject.SetActive(true);

        int   frames   = Mathf.Max(1, frameCount);
        float duration = frames / Mathf.Max(1f, fps);
        float elapsed  = 0f;

        // Snap to start frame immediately
        ShowFrameAtT(revealDirection ? 1f : 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t       = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            ShowFrameAtT(revealDirection ? 1f - smoothT : smoothT);
            yield return null;
        }

        ShowFrameAtT(revealDirection ? 0f : 1f);

        if (!revealDirection)
            target.gameObject.SetActive(false);

        IsPlaying = false;
        onComplete?.Invoke();
    }

    private void ShowFrameAtT(float t)
    {
        if (target == null || target.texture == null) return;

        int cols     = Mathf.Max(1, columns);
        int rowCount = Mathf.Max(1, rows);
        int frames   = Mathf.Max(1, frameCount);

        int index = Mathf.Clamp(Mathf.RoundToInt(t * (frames - 1)), 0, frames - 1);
        int col   = index % cols;
        int row   = index / cols;

        float cellW = 1f / cols;
        float cellH = 1f / rowCount;
        float cellX = col  * cellW;
        float cellY = 1f - (row + 1) * cellH;

        float screenAspect = (float)Screen.width / Screen.height;
        float cellAspect   = ((float)target.texture.width / cols) /
                             ((float)target.texture.height / rowCount);

        float uvW, uvH, uvX, uvY;
        if (screenAspect > cellAspect)
        {
            uvW = cellW;
            uvH = cellH * (cellAspect / screenAspect);
            uvX = cellX;
            uvY = cellY + (cellH - uvH) * 0.5f;
        }
        else
        {
            uvH = cellH;
            uvW = cellW * (screenAspect / cellAspect);
            uvX = cellX + (cellW - uvW) * 0.5f;
            uvY = cellY;
        }

        target.uvRect = new Rect(uvX, uvY, uvW, uvH);
    }
}
