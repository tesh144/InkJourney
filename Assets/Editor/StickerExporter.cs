using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// InkJourney → Export Stickers for Admin HTML
///
/// Reads StickerManager.stickers from the open scene, encodes every sprite as a
/// base64 PNG data-URI, then patches the STICKERS array directly inside
/// inkjourney-admin.html so no extra files or web server are needed.
///
/// Uses Graphics.Blit + ReadPixels via RenderTexture so it works with any texture
/// format, compression setting, isReadable flag, and Unity SpriteAtlas packing.
/// </summary>
public static class StickerExporter
{
    private const string HtmlPath    = "/Users/tesh/InkJourney/admin/inkjourney-admin.html";
    private const string StartMarker = "// --- STICKERS_START ---";
    private const string EndMarker   = "// --- STICKERS_END ---";

    [MenuItem("InkJourney/Export Stickers for Admin HTML")]
    public static void Export()
    {
        StickerManager sm = UnityEngine.Object.FindFirstObjectByType<StickerManager>();
        if (sm == null)
        {
            EditorUtility.DisplayDialog("Error",
                "StickerManager not found in the current scene. Open the main scene first.", "OK");
            return;
        }

        if (!File.Exists(HtmlPath))
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not find admin HTML at:\n{HtmlPath}", "OK");
            return;
        }

        var lines = new List<string>();
        lines.Add($"  const a = [{{ id: 0, name: 'None', icon: null }}];");

        int exported = 0;
        for (int i = 1; i < sm.stickers.Count; i++)
        {
            Sprite sprite = sm.stickers[i];
            if (sprite == null)
            {
                lines.Add($"  a.push({{ id: {i}, name: 'Sticker {i}', icon: null }});");
                continue;
            }

            string b64  = SpriteToBase64(sprite);
            string icon = b64 != null ? $"'data:image/png;base64,{b64}'" : "null";
            string name = sprite.name.Replace("\\", "\\\\").Replace("'", "\\'");
            lines.Add($"  a.push({{ id: {i}, name: '{name}', icon: {icon} }});");
            if (b64 != null) exported++;
        }

        lines.Add("  return a;");

        string newBlock = string.Join("\n", lines);
        string html     = File.ReadAllText(HtmlPath);
        string pattern  = Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker);

        if (!Regex.IsMatch(html, pattern))
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not find the marker comments in inkjourney-admin.html.\n\n" +
                $"Make sure the HTML contains:\n{StartMarker}\n...\n{EndMarker}", "OK");
            return;
        }

        File.WriteAllText(HtmlPath, Regex.Replace(html, pattern, StartMarker + "\n" + newBlock + "\n  " + EndMarker));

        EditorUtility.DisplayDialog("Done",
            $"Patched {exported} sticker icons ({sm.stickers.Count - 1} total) into:\n{HtmlPath}\n\n" +
            "Reload the admin page in your browser.", "OK");

        Debug.Log($"[StickerExporter] Patched {exported}/{sm.stickers.Count - 1} stickers into {HtmlPath}");
    }

    // ── Sprite → base64 PNG ────────────────────────────────────────────────

    private static string SpriteToBase64(Sprite sprite)
    {
        Texture2D tex  = sprite.texture;
        Rect      rect = sprite.textureRect;
        int x = Mathf.RoundToInt(rect.x);
        int y = Mathf.RoundToInt(rect.y);
        int w = Mathf.RoundToInt(rect.width);
        int h = Mathf.RoundToInt(rect.height);
        if (w <= 0 || h <= 0) return null;

        try
        {
            // Blit the atlas to a RenderTexture — works regardless of isReadable,
            // texture compression format, or SpriteAtlas packing.
            var atlasRT = RenderTexture.GetTemporary(
                tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, atlasRT);

            var prevRT = RenderTexture.active;
            RenderTexture.active = atlasRT;
            var readback = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readback.Apply();
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(atlasRT);

            Color[] src = readback.GetPixels(x, y, w, h);
            UnityEngine.Object.DestroyImmediate(readback);

            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.SetPixels(src);
            tmp.Apply();
            byte[] png = tmp.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tmp);

            return Convert.ToBase64String(png);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StickerExporter] Skipped '{sprite.name}': {e.Message}");
            return null;
        }
    }
}
