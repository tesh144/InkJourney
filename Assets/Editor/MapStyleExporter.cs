using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// InkJourney → Export Map Styles for Admin HTML
///
/// Reads MapLoader.mapStyles from the open scene and patches the MAP_STYLES array
/// inside inkjourney-admin.html so the journey map-style dropdown stays in sync.
/// </summary>
public static class MapStyleExporter
{
    private const string HtmlPath    = "/Users/tesh/InkJourney/admin/inkjourney-admin.html";
    private const string StartMarker = "// --- MAP_STYLES_START ---";
    private const string EndMarker   = "// --- MAP_STYLES_END ---";

    [MenuItem("InkJourney/Export Map Styles for Admin HTML")]
    public static void Export()
    {
        MapLoader ml = Object.FindFirstObjectByType<MapLoader>();
        if (ml == null)
        {
            EditorUtility.DisplayDialog("Error",
                "MapLoader not found in the current scene. Open the main scene first.", "OK");
            return;
        }

        if (ml.mapStyles == null || ml.mapStyles.Count == 0)
        {
            EditorUtility.DisplayDialog("Error",
                "MapLoader has no map styles configured.", "OK");
            return;
        }

        if (!File.Exists(HtmlPath))
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not find admin HTML at:\n{HtmlPath}", "OK");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("  const a = [];");

        for (int i = 0; i < ml.mapStyles.Count; i++)
        {
            var style = ml.mapStyles[i];
            if (style == null) continue;
            string name = (string.IsNullOrWhiteSpace(style.name) ? $"Style {i}" : style.name)
                          .Replace("\\", "\\\\").Replace("'", "\\'");
            sb.AppendLine($"  a.push({{ name: '{name}' }});");
        }

        sb.Append("  return a;");

        string html     = File.ReadAllText(HtmlPath);
        string pattern  = Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker);
        string newBlock = StartMarker + "\n" + sb + "\n  " + EndMarker;
        string patched  = Regex.Replace(html, pattern, newBlock);

        if (patched == html)
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not find the marker comments in inkjourney-admin.html.\n\n" +
                $"Expected:\n{StartMarker}\n...\n{EndMarker}", "OK");
            return;
        }

        File.WriteAllText(HtmlPath, patched);

        EditorUtility.DisplayDialog("Done",
            $"Exported {ml.mapStyles.Count} map style(s) into:\n{HtmlPath}\n\nReload the admin page in your browser.", "OK");

        Debug.Log($"[MapStyleExporter] Exported {ml.mapStyles.Count} styles into {HtmlPath}");
    }
}
