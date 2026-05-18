using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists owned and collected stories to local JSON files so they survive
/// server expiry. Owned stories can be re-read or re-deployed; collected
/// stories remain in the library even after they expire on the server.
/// </summary>
public static class LocalStoryStore
{
    [Serializable]
    public class StoredEntry
    {
        [Serializable]
        public class StoredComment
        {
            public string CommentId;
            public string UserId;
            public string UserName;
            public string Text;
            public long Created;
        }

        public string ID, User, UserName, Title, Content, Theme, Track, Font, PhotoUrl;
        public float Latitude, Longitude;
        public int Saves, LikesCount, Views, StickerID, FontID;
        public float StickerX = 0.5f, StickerY = 0.5f, StickerScale = 1.0f, StickerRotation = 0.0f;
        public long Created, LastUpdated, Expire;
        public bool IsLocalDraft;
        public List<string> Tags           = new List<string>();
        public List<string> SavedByUserIds = new List<string>();
        public List<StoredComment> Comments = new List<StoredComment>();
    }

    [Serializable]
    private class StoredEntryList
    {
        public List<StoredEntry> entries = new List<StoredEntry>();
    }

    // Properties (not fields) so Application.persistentDataPath is evaluated at call time.
    private static string OwnedPath     => Path.Combine(Application.persistentDataPath, "owned_stories.json");
    private static string CollectedPath => Path.Combine(Application.persistentDataPath, "collected_stories.json");

    // ── Public API ─────────────────────────────────────────────────────────────

    public static List<StoredEntry> LoadOwned()     => Load(OwnedPath);
    public static List<StoredEntry> LoadCollected() => Load(CollectedPath);

    public static void SaveOwned(GoogleSheetsFetcher.Entry e)
    {
        if (!IsValid(e)) return;
        Save(OwnedPath, e);
    }

    public static void SaveCollected(GoogleSheetsFetcher.Entry e)
    {
        if (!IsValid(e)) return;
        Save(CollectedPath, e);
    }

    private static bool IsValid(GoogleSheetsFetcher.Entry e) =>
        e != null
        && !string.IsNullOrWhiteSpace(e.ID)
        && !string.IsNullOrWhiteSpace(e.User)
        && !string.IsNullOrWhiteSpace(e.Title)
        && !string.Equals(e.Title.Trim(), "ENTER TITLE", System.StringComparison.OrdinalIgnoreCase);

    public static void RemoveOwned(string id)      => Remove(OwnedPath,     id);
    public static void RemoveCollected(string id)  => Remove(CollectedPath,  id);

    public static void WipeAll()
    {
        try { if (File.Exists(OwnedPath))     File.Delete(OwnedPath);     } catch { }
        try { if (File.Exists(CollectedPath)) File.Delete(CollectedPath); } catch { }
    }

    /// <summary>Converts a stored snapshot back to a lightweight Entry (pointer = null).</summary>
    public static GoogleSheetsFetcher.Entry ToLiveEntry(StoredEntry s)
    {
        return new GoogleSheetsFetcher.Entry
        {
            ID           = s.ID,
            User         = s.User,
            UserName     = s.UserName,
            Title        = s.Title,
            Content      = s.Content,
            Theme        = s.Theme,
            Track        = s.Track,
            Font         = s.Font,
            PhotoUrl     = s.PhotoUrl,
            Latitude     = s.Latitude,
            Longitude    = s.Longitude,
            Saves          = s.Saves,
            LikesCount     = s.LikesCount,
            Views          = s.Views,
            Created        = s.Created,
            LastUpdated    = s.LastUpdated,
            Expire         = s.Expire,
            StickerID      = s.StickerID,
            FontID         = s.FontID,
            StickerX        = s.StickerX,
            StickerY        = s.StickerY,
            StickerScale    = s.StickerScale,
            StickerRotation = s.StickerRotation,
            Tags           = s.Tags            ?? new List<string>(),
            SavedByUserIds = s.SavedByUserIds  ?? new List<string>(),
            Comments       = ToLiveComments(s.Comments),
            IsLocalDraft   = s.IsLocalDraft,
        };
    }

    private static List<GoogleSheetsFetcher.Entry.Comment> ToLiveComments(List<StoredEntry.StoredComment> stored)
    {
        var result = new List<GoogleSheetsFetcher.Entry.Comment>();
        if (stored == null)
            return result;

        foreach (var c in stored)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Text))
                continue;

            result.Add(new GoogleSheetsFetcher.Entry.Comment
            {
                CommentId = c.CommentId,
                UserId = c.UserId,
                UserName = c.UserName,
                Text = c.Text,
                Created = c.Created,
            });
        }

        return result;
    }

    private static List<StoredEntry.StoredComment> ToStoredComments(List<GoogleSheetsFetcher.Entry.Comment> comments)
    {
        var result = new List<StoredEntry.StoredComment>();
        if (comments == null)
            return result;

        foreach (var c in comments)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Text))
                continue;

            result.Add(new StoredEntry.StoredComment
            {
                CommentId = c.CommentId,
                UserId = c.UserId,
                UserName = c.UserName,
                Text = c.Text,
                Created = c.Created,
            });
        }

        return result;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static List<StoredEntry> Load(string path)
    {
        if (!File.Exists(path)) return new List<StoredEntry>();
        try
        {
            var wrapper = JsonUtility.FromJson<StoredEntryList>(File.ReadAllText(path));
            return wrapper?.entries ?? new List<StoredEntry>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalStoryStore] Failed to load {path}: {ex.Message}");
            return new List<StoredEntry>();
        }
    }

    private static void Save(string path, GoogleSheetsFetcher.Entry entry)
    {
        var list = Load(path);
        int idx  = list.FindIndex(s => s.ID == entry.ID);
        var stored = ToStored(entry);
        if (idx >= 0) list[idx] = stored;
        else          list.Add(stored);
        Write(path, list);
    }

    private static void Remove(string path, string id)
    {
        var list = Load(path);
        list.RemoveAll(s => s.ID == id);
        Write(path, list);
    }

    private static void Write(string path, List<StoredEntry> list)
    {
        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(new StoredEntryList { entries = list }, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalStoryStore] Failed to write {path}: {ex.Message}");
        }
    }

    private static StoredEntry ToStored(GoogleSheetsFetcher.Entry e) => new StoredEntry
    {
        ID             = e.ID,
        User           = e.User,
        UserName       = e.UserName,
        Title          = e.Title,
        Content        = e.Content,
        Theme          = e.Theme,
        Track          = e.Track,
        Font           = e.Font,
        PhotoUrl       = e.PhotoUrl,
        Latitude       = e.Latitude,
        Longitude      = e.Longitude,
        Saves          = e.Saves,
        LikesCount     = e.LikesCount,
        Views          = e.Views,
        Created        = e.Created,
        LastUpdated    = e.LastUpdated,
        Expire         = e.Expire,
        StickerID      = e.StickerID,
        FontID         = e.FontID,
        StickerX        = e.StickerX,
        StickerY        = e.StickerY,
        StickerScale    = e.StickerScale,
        StickerRotation = e.StickerRotation,
        Tags           = e.Tags            ?? new List<string>(),
        SavedByUserIds = e.SavedByUserIds  ?? new List<string>(),
        Comments       = ToStoredComments(e.Comments),
        IsLocalDraft   = e.IsLocalDraft,
    };
}
