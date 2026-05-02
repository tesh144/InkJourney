using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists JPEG bytes for stories whose photo upload hasn't completed yet.
/// Files survive app restarts; GoogleSheetsFetcher retries them on next launch.
/// </summary>
public static class PhotoUploadQueue
{
    static string Dir => Path.Combine(Application.persistentDataPath, "photo_queue");

    public static void Enqueue(string storyId, byte[] jpegBytes)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(FilePath(storyId), jpegBytes);
    }

    public static void Dequeue(string storyId)
    {
        string p = FilePath(storyId);
        if (File.Exists(p)) File.Delete(p);
    }

    public static bool HasPending(string storyId) => File.Exists(FilePath(storyId));

    public static byte[] GetBytes(string storyId)
    {
        string p = FilePath(storyId);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    public static string FilePath(string storyId) =>
        Path.Combine(Dir, storyId + ".jpg");

    public static List<string> GetPendingIds()
    {
        if (!Directory.Exists(Dir)) return new List<string>();
        var ids = new List<string>();
        foreach (string f in Directory.GetFiles(Dir, "*.jpg"))
            ids.Add(Path.GetFileNameWithoutExtension(f));
        return ids;
    }
}
