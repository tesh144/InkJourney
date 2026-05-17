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

    private static readonly Dictionary<string, Texture2D>             _thumbCache = new();
    private static readonly LinkedList<string>                        _lruOrder   = new();
    private static readonly Dictionary<string, LinkedListNode<string>> _lruNodes  = new();
    private const int CacheCapacity = 50;

    private static Texture2D CacheGet(string url)
    {
        if (!_thumbCache.TryGetValue(url, out var tex)) return null;
        // Move to front (most recently used)
        _lruOrder.Remove(_lruNodes[url]);
        _lruOrder.AddFirst(_lruNodes[url]);
        return tex;
    }

    private static void CacheAdd(string url, Texture2D tex)
    {
        if (_thumbCache.ContainsKey(url))
        {
            _thumbCache[url] = tex;
            _lruOrder.Remove(_lruNodes[url]);
            _lruOrder.AddFirst(_lruNodes[url]);
            return;
        }

        // Evict LRU tail if at capacity
        if (_thumbCache.Count >= CacheCapacity)
        {
            var lruNode = _lruOrder.Last;
            if (lruNode != null)
            {
                string lruUrl = lruNode.Value;
                if (_thumbCache.TryGetValue(lruUrl, out var evicted))
                    Object.Destroy(evicted);
                _thumbCache.Remove(lruUrl);
                _lruNodes.Remove(lruUrl);
                _lruOrder.RemoveLast();
            }
        }

        var node = _lruOrder.AddFirst(url);
        _lruNodes[url]   = node;
        _thumbCache[url] = tex;
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        // Auto-find RawImage if not manually assigned in the prefab
        if (photo == null) photo = GetComponentInChildren<RawImage>();

        _spawnTime = Time.time;

        // Start invisible so the fade always plays, even when loading from cache
        if (photo != null) photo.color = Color.clear;
    }

    public void Initialise(string url, float rotMin, float rotMax, float extraDelay = 0f)
    {
        _spawnTime += extraDelay;

        if (rotatedTransform != null)
            rotatedTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(rotMin, rotMax));

        if (!string.IsNullOrEmpty(url))
            StartCoroutine(LoadTexture(url));
    }

    public void PlayShuffle() => _animator?.SetTrigger(ShuffleTrigger);

    private IEnumerator LoadTexture(string url)
    {
        var cached = CacheGet(url);
        if (cached != null) { Apply(cached); yield break; }

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success || photo == null) yield break;

        var downloaded = DownloadHandlerTexture.GetContent(req);
        CacheAdd(url, downloaded);
        Apply(downloaded);
    }

    private void Apply(Texture2D tex)
    {
        if (photo == null) return;
        photo.texture = tex;
        photo.uvRect  = CenterCropSquare(tex);

        var fitter = photo.GetComponent<AspectRatioFitter>();
        if (fitter != null) fitter.aspectRatio = 1.25f;

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

    public static Texture2D GetCached(string url) =>
        !string.IsNullOrEmpty(url) ? CacheGet(url) : null;

    // Loads into cache and fires callback — does NOT auto-apply to any UI element.
    public static IEnumerator FetchTexture(string url, System.Action<Texture2D> onDone)
    {
        if (string.IsNullOrEmpty(url)) { onDone?.Invoke(null); yield break; }
        var cached = CacheGet(url);
        if (cached != null) { onDone?.Invoke(cached); yield break; }
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { onDone?.Invoke(null); yield break; }
        var tex = DownloadHandlerTexture.GetContent(req);
        CacheAdd(url, tex);
        onDone?.Invoke(tex);
    }

    // Pre-load into cache without spawning a card.
    public static IEnumerator Prewarm(string url)
    {
        if (string.IsNullOrEmpty(url) || CacheGet(url) != null) yield break;

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;

        CacheAdd(url, DownloadHandlerTexture.GetContent(req));
    }

    // For the story panel — shows cached texture instantly if available.
    public static IEnumerator LoadForStory(string url, RawImage target)
    {
        if (target == null || string.IsNullOrEmpty(url)) yield break;

        var cached = CacheGet(url);
        if (cached != null) { ApplyToTarget(target, cached); yield break; }

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;

        var downloaded = DownloadHandlerTexture.GetContent(req);
        CacheAdd(url, downloaded);

        if (target != null) ApplyToTarget(target, downloaded);
    }

    private static void ApplyToTarget(RawImage target, Texture2D tex)
    {
        if (target == null || tex == null) return;
        target.texture = tex;
        target.color   = Color.white;
        target.uvRect  = CenterCropSquare(tex);

        var fitter = target.GetComponent<AspectRatioFitter>();
        if (fitter != null) fitter.aspectRatio = 1.25f;
    }

    private static Rect CenterCropSquare(Texture2D tex)
    {
        const float targetAspect = 1.25f;
        float texAspect = (float)tex.width / tex.height;

        if (texAspect > targetAspect)
        {
            float u = targetAspect / texAspect;
            return new Rect((1f - u) * 0.5f, 0f, u, 1f);
        }
        else
        {
            float v = texAspect / targetAspect;
            return new Rect(0f, (1f - v) * 0.5f, 1f, v);
        }
    }

}
