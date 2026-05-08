using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public static class MapboxImageCache
{
    private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

    public static IEnumerator Fetch(string url, System.Action<Texture2D> onDone)
    {
        if (_cache.TryGetValue(url, out var cached))
        {
            onDone?.Invoke(cached);
            yield break;
        }

        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onDone?.Invoke(null);
            yield break;
        }

        var tex = ((DownloadHandlerTexture)req.downloadHandler).texture;
        _cache[url] = tex;
        onDone?.Invoke(tex);
    }
}
