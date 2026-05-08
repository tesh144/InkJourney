using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// InkJourney → Export Tags for Admin HTML
///
/// Reads TagManager.tags from the open scene and patches the TAGS array
/// inside inkjourney-admin.html so all tag dropdowns stay in sync with the app.
/// </summary>
public static class TagExporter
{
    private const string HtmlPath    = "/Users/tesh/InkJourney/admin/inkjourney-admin.html";
    private const string StartMarker = "// --- TAGS_START ---";
    private const string EndMarker   = "// --- TAGS_END ---";

    [MenuItem("InkJourney/Export Tags for Admin HTML")]
    public static void Export()
    {
        TagManager tm = Object.FindFirstObjectByType<TagManager>();
        if (tm == null)
        {
            EditorUtility.DisplayDialog("Error",
                "TagManager not found in the current scene. Open the main scene first.", "OK");
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

        foreach (var tag in tm.tags)
        {
            if (tag == null || string.IsNullOrWhiteSpace(tag.tagId))
                continue;

            string id      = tag.tagId.Replace("\\", "\\\\").Replace("'", "\\'");
            string display = (tag.displayName ?? tag.tagId).Replace("\\", "\\\\").Replace("'", "\\'");
            sb.AppendLine($"  a.push({{ id: '{id}', display: '{display}' }});");
        }

        sb.Append("  return a;");

        string html    = File.ReadAllText(HtmlPath);
        string pattern = Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker);

        if (!Regex.IsMatch(html, pattern))
        {
            EditorUtility.DisplayDialog("Error",
                $"Could not find the marker comments in inkjourney-admin.html.\n\n" +
                $"Expected:\n{StartMarker}\n...\n{EndMarker}", "OK");
            return;
        }

        string newBlock = StartMarker + "\n" + sb + "\n  " + EndMarker;
        File.WriteAllText(HtmlPath, Regex.Replace(html, pattern, newBlock));

        EditorUtility.DisplayDialog("Done",
            $"Exported {tm.tags.Count} tag(s) into:\n{HtmlPath}\n\nReload the admin page in your browser.", "OK");

        Debug.Log($"[TagExporter] Exported {tm.tags.Count} tags into {HtmlPath}");
    }
}
