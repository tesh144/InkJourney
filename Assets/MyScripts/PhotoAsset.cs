using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PhotoAsset : MonoBehaviour
{
    public RawImage      photo;
    public RectTransform rotatedTransform;

    [SerializeField] float fadeInDelay    = 0.15f;
    [SerializeField] float fadeInDuration = 0.25f;

    private Animator  _animator;
    private Coroutine _fadeCoroutine;
    private float     _spawnTime;
    private static readonly int ShuffleTrigger = Animator.StringToHash("Shuffle");

    private static readonly Dictionary<string, Texture2D> _thumbCache = new();
    private static readonly Dictionary<string, Texture2D> _fullCache  = new();

    private const int ThumbSize = 256;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        // Auto-find RawImage if not manually assigned in the prefab
        if (photo == null) photo = GetComponentInChildren<RawImage>();

        _spawnTime = Time.time;

        // Start invisible so the fade always plays, even when loading from cache
        if (photo != null) photo.color = Color.clear;
    }

    // fullRes = false for cards/shuffles, true for story panel
    public void Initialise(string url, float rotMin, float rotMax, bool fullRes = false, float extraDelay = 0f)
    {
        _spawnTime += extraDelay;

        if (rotatedTransform != null)
            rotatedTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(rotMin, rotMax));

        if (!string.IsNullOrEmpty(url))
            StartCoroutine(LoadTexture(url, fullRes));
    }

    public void PlayShuffle() => _animator?.SetTrigger(ShuffleTrigger);

    private IEnumerator LoadTexture(string url, bool fullRes)
    {
        // Apply cached thumb immediately so there's no blank frame
        if (_thumbCache.TryGetValue(url, out var thumb))
        {
            Apply(thumb);
            if (!fullRes) yield break;
        }

        // Full res already cached — upgrade straight away
        if (_fullCache.TryGetValue(url, out var full))
        {
            Apply(full);
            yield break;
        }

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success || photo == null) yield break;

        var downloaded = DownloadHandlerTexture.GetContent(req);

        if (fullRes)
        {
            _fullCache[url] = downloaded;
            if (!_thumbCache.ContainsKey(url))
                _thumbCache[url] = ScaleDown(downloaded, ThumbSize);
            Apply(downloaded);
        }
        else
        {
            var t = ScaleDown(downloaded, ThumbSize);
            Destroy(downloaded);
            _thumbCache[url] = t;
            Apply(t);
        }
    }

    private void Apply(Texture2D tex)
    {
        if (photo == null) return;
        photo.texture = tex;

        var fitter = photo.GetComponent<AspectRatioFitter>();
        if (fitter != null && tex.width > 0 && tex.height > 0)
            fitter.aspectRatio = (float)tex.width / tex.height;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        photo.color = Color.clear;

        float remaining = (_spawnTime + fadeInDelay) - Time.time;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            photo.color = new Color(1f, 1f, 1f, elapsed / fadeInDuration);
            yield return null;
        }
        photo.color    = Color.white;
        _fadeCoroutine = null;
    }

    // ── Static helpers ─────────────────────────────────────────────────────

    // Pre-load a thumb into cache without spawning a card.
    public static IEnumerator Prewarm(string url)
    {
        if (string.IsNullOrEmpty(url) || _thumbCache.ContainsKey(url)) yield break;

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;

        var downloaded = DownloadHandlerTexture.GetContent(req);
        _thumbCache[url] = ScaleDown(downloaded, ThumbSize);
        Destroy(downloaded);
    }

    // For the story panel — shows cached thumb instantly, then swaps in full res.
    public static IEnumerator LoadForStory(string url, RawImage target)
    {
        if (target == null || string.IsNullOrEmpty(url)) yield break;

        // Show cached thumb immediately while full res loads
        if (_thumbCache.TryGetValue(url, out var thumb))
            ApplyToTarget(target, thumb);

        if (_fullCache.TryGetValue(url, out var full))
        {
            ApplyToTarget(target, full);
            yield break;
        }

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;

        var downloaded = DownloadHandlerTexture.GetContent(req);
        _fullCache[url] = downloaded;
        if (!_thumbCache.ContainsKey(url))
            _thumbCache[url] = ScaleDown(downloaded, ThumbSize);

        if (target != null) ApplyToTarget(target, downloaded);
    }

    private static void ApplyToTarget(RawImage target, Texture2D tex)
    {
        if (target == null || tex == null) return;
        target.texture = tex;
        target.color   = Color.white;

        var fitter = target.GetComponent<AspectRatioFitter>();
        if (fitter != null && tex.width > 0 && tex.height > 0)
            fitter.aspectRatio = (float)tex.width / tex.height;
    }

    // Creates a scaled-down copy. Does NOT destroy the source.
    private static Texture2D ScaleDown(Texture2D src, int maxSize)
    {
        if (src.width <= maxSize && src.height <= maxSize) return src;

        float aspect = (float)src.width / src.height;
        int w = src.width >= src.height ? maxSize : Mathf.RoundToInt(maxSize * aspect);
        int h = src.height >= src.width ? maxSize : Mathf.RoundToInt(maxSize / aspect);

        var rt   = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}
