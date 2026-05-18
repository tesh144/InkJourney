using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using TMPro;
using System;
using System.Text;
using System.Text.RegularExpressions;

public class UI_StoryPanel : MonoBehaviour
{
    private static readonly Regex UrlRegex = new Regex("(https?://[^\\s<>\"\\)\\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsOpen { get; private set; }

    public TextMeshProUGUI content;
    public TextMeshProUGUI title;
    public TextMeshProUGUI user;
    public TextMeshProUGUI likes;
    public TextMeshProUGUI views;
    public TextMeshProUGUI tags;
    public TextMeshProUGUI location_expire_text;
    public TextMeshProUGUI expiresText;
    public TextMeshProUGUI dateText;
    public TextMeshProUGUI commentCountText;

    [HideInInspector] public string authorId;
    [HideInInspector] public string authorName;

    public GameObject shareUI;

    [Header("Type Icon")]
    public Image typeIcon;
    public Sprite storySprite;
    public Sprite friendStorySprite;
    public Sprite landmarkSprite;

    public GameObject photoContainer;
    public RawImage photoImage;
    public RectTransform stickerRect;
    public Image profilePic;
    public Image backdrop;
    public Image cover;
    public List<Image> lines_buttons;
    public LikeButton likeButton;
    public SaveButton saveButton;
    public BoostButton boostButton;
    public StoryCommentsPanel commentsPanel;

    private string pendingProfilePicAuthorId;
    private int _photoLoadGen;

    [HideInInspector] public string currentStoryId;
    public GoogleSheetsFetcher.Entry BoundEntry { get; private set; }

    private static readonly HashSet<string> HiddenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "public", "private", "friends_only", "draft"
    };

    [Header("Expiry Display")]
    public string draftLabel    = "Draft";
    public string expiredLabel  = "Expired";

    public GameObject editButton;
    public GameObject deleteButton;

    private void Awake()
    {
        if (content != null)
        {
            content.richText = true;
            content.raycastTarget = true;

            StoryContentLinkHandler handler = content.GetComponent<StoryContentLinkHandler>();
            if (handler == null)
                handler = content.gameObject.AddComponent<StoryContentLinkHandler>();

            handler.targetText = content;
        }
    }

    private void OnEnable()
    {
        IsOpen = true;
    }

    public void SetPhoto(string photoUrl, string storyId = null)
    {
        if (string.IsNullOrEmpty(photoUrl) && storyId != null && PhotoUploadQueue.HasPending(storyId))
            photoUrl = "file://" + PhotoUploadQueue.FilePath(storyId);

        int gen = ++_photoLoadGen;
        if (photoImage != null) { photoImage.texture = null; photoImage.color = new Color(0.1f, 0.1f, 0.1f, 1f); }

        if (!string.IsNullOrEmpty(photoUrl))
        {
            if (photoContainer != null) photoContainer.SetActive(true);
            if (photoImage != null && MapLoader.instance != null)
                MapLoader.instance.StartCoroutine(LoadPhotoWithFallback(photoUrl, gen));
            return;
        }

        if (photoContainer != null) photoContainer.SetActive(true);
        TryLoadMapFallback(gen);
    }

    private IEnumerator LoadPhotoWithFallback(string url, int gen)
    {
        Texture2D loaded = null;
        yield return PhotoAsset.FetchTexture(url, tex => loaded = tex);
        if (gen != _photoLoadGen || photoImage == null) yield break;
        if (loaded == null) { TryLoadMapFallback(gen); yield break; }
        StoryPhotoManager.ApplyPhotoToRawImage(loaded, photoImage);
    }

    private void TryLoadMapFallback(int gen)
    {
        if (photoImage == null || photoImage.texture != null) return;
        var entry = BoundEntry;
        if (entry == null || (entry.Latitude == 0 && entry.Longitude == 0)) return;
        if (MapLoader.instance == null) return;
        photoImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        MapLoader.instance.StartCoroutine(LoadMapFallback(entry, gen));
    }

    public static string MapPinColor(GoogleSheetsFetcher.Entry entry)
    {
        if (entry == null) return "3bb3d0";
        if (GoogleSheetsFetcher.IsLandmark(entry)) return "9b59b6";
        if (GoogleSheetsFetcher.instance?.journeysList != null)
            foreach (var j in GoogleSheetsFetcher.instance.journeysList)
                if (j?.Chapters?.Exists(c => c.StoryId == entry.ID) == true)
                    return "b23333";
        if (FriendsManager.IsFriend(entry.User)) return "ffd833";
        return "3bb3d0";
    }

    private IEnumerator LoadMapFallback(GoogleSheetsFetcher.Entry entry, int gen)
    {
        string styleId = ResolveMapStyle(entry.Theme);
        string token   = MapLoader.instance?.mapboxToken ?? "";
        string pin     = MapPinColor(entry);
        string url     = $"https://api.mapbox.com/styles/v1/{styleId}/static/pin-l+{pin}({entry.Longitude},{entry.Latitude})/{entry.Longitude},{entry.Latitude},12,0/640x360@2x?access_token={token}";

        yield return MapboxImageCache.Fetch(url, tex =>
        {
            if (gen != _photoLoadGen || photoImage == null || tex == null) return;
            StoryPhotoManager.ApplyPhotoToRawImage(tex, photoImage);
        });
    }

    private static string ResolveMapStyle(string themeName)
    {
        if (!string.IsNullOrEmpty(themeName) && MapLoader.instance?.mapStyles != null)
        {
            foreach (var s in MapLoader.instance.mapStyles)
                if (s.defaultTheme != null && s.defaultTheme.themeName == themeName)
                    return s.styleString;
        }
        return !string.IsNullOrEmpty(MapLoader.instance?.mapStyle)
            ? MapLoader.instance.mapStyle
            : "mapbox/dark-v11";
    }

    public void SetPhoto(Texture2D texture)
    {
        ++_photoLoadGen; // cancel any in-flight URL load
        if (photoContainer != null) photoContainer.SetActive(true);
        if (texture != null && photoImage != null)
            StoryPhotoManager.ApplyPhotoToRawImage(texture, photoImage);
    }

    public void SetAuthor(string authorId, string authorName)
    {
        this.authorId = authorId;
        this.authorName = authorName;

        RefreshAuthorText();
        RefreshAuthorProfilePic();
    }

    public void BindStoryEntry(GoogleSheetsFetcher.Entry entry)
    {
        currentStoryId = entry != null ? entry.ID : null;
        BoundEntry = entry;

        if (entry != null)
        {
            Analytics.StoryRead(entry.ID);
            GoogleSheetsFetcher.instance?.RecordStoryView(entry.ID, entry.User);
            StoryLifetimeManager.instance?.RecordView(entry);
            JourneyManager.instance?.OnStoryRead(entry.ID);
        }

        if (boostButton != null)
            boostButton.BindEntry(entry);

        if (likes != null)
            likes.text = entry != null ? CompactCountFormatter.FormatLikes(entry.Saves) : "0";

        if (saveButton != null)
            saveButton.BindEntry(entry);

        if (likeButton != null)
            likeButton.BindEntry(entry);

        SetTags(entry != null ? entry.Tags : null);

        if (commentsPanel != null)
        {
            commentsPanel.SetCountDisplay(commentCountText);
            commentsPanel.BindStory(entry);
        }

        bool isOwner = entry != null
            && UserProfileManager.instance != null
            && entry.User == UserProfileManager.instance.UserId;
        bool isExpired = entry != null
            && !entry.IsLocalDraft
            && entry.Expire > 0
            && entry.Expire < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (editButton   != null) editButton.SetActive(isOwner && !isExpired);
        if (deleteButton != null) deleteButton.SetActive(isOwner);

        PositionSticker(entry);
    }

    public void PositionSticker(GoogleSheetsFetcher.Entry entry)
    {
        if (stickerRect == null) return;
        var parent = stickerRect.parent as RectTransform;
        if (parent == null) return;

        float nx       = entry != null ? entry.StickerX        : 0.5f;
        float ny       = entry != null ? entry.StickerY        : 0.5f;
        float scale    = entry != null ? entry.StickerScale    : 1.0f;
        float rotation = entry != null ? entry.StickerRotation : 0.0f;

        stickerRect.anchoredPosition = new Vector2(
            (nx - 0.5f) * parent.rect.width,
            (ny - 0.5f) * parent.rect.height);
        stickerRect.localScale       = new Vector3(scale, scale, 1f);
        stickerRect.localEulerAngles = new Vector3(0f, 0f, rotation);
    }

    public void SetExpireDisplay(GoogleSheetsFetcher.Entry entry)
    {
        if (expiresText == null) return;
        if (entry == null) { expiresText.text = string.Empty; return; }

        if (entry.IsLocalDraft || entry.Expire == 0)
        {
            expiresText.text = draftLabel;
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (entry.Expire < now)
        {
            expiresText.text = expiredLabel;
            return;
        }

        expiresText.text = StoryDateFormatter.FormatActive(entry.Expire);
    }

    public void EditBoundStory()
    {
        if (BoundEntry == null) return;
        gameObject.SetActive(false);
        CreateNewStory.instance.LoadForEdit(BoundEntry);
    }

    public void DeleteBoundStory()
    {
        if (BoundEntry == null) return;
        gameObject.SetActive(false);
        GoogleSheetsFetcher.instance.DeleteEntryFromFirestore(BoundEntry);
    }

    public void SetTags(IEnumerable<string> tagIds)
    {
        if (tags == null)
            return;

        if (tagIds == null)
        {
            tags.text = string.Empty;
            return;
        }

        var builder = new StringBuilder();

        foreach (string rawTag in tagIds)
        {
            if (string.IsNullOrWhiteSpace(rawTag))
                continue;

            string tagId = rawTag.Trim();

            if (HiddenTags.Contains(tagId))
                continue;

            string display = TagManager.instance != null
                ? TagManager.instance.GetDisplayName(tagId)
                : tagId;

            if (string.IsNullOrWhiteSpace(display))
                continue;

            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append('#').Append(display.Trim());
        }

        tags.text = builder.ToString();
    }

    public void SetContentText(string rawText)
    {
        if (content == null)
            return;

        content.text = LinkifyUrls(rawText ?? string.Empty);
    }

    private static string LinkifyUrls(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Contains("<link="))
            return text;

        return UrlRegex.Replace(text, match =>
        {
            string url = match.Value;
            string label = BuildCompactLinkLabel(url);
            return $"<link=\"{url}\"><u><color=#66B3FF>{label}</color></u></link>";
        });
    }

    private static string BuildCompactLinkLabel(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) || string.IsNullOrWhiteSpace(parsed.Host))
            return "link";

        string host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? parsed.Host.Substring(4)
            : parsed.Host;

        string hint = ExtractDestinationHint(parsed);

        if (string.IsNullOrWhiteSpace(hint))
            return host;

        return host + " (" + hint + ")";
    }

    private static string ExtractDestinationHint(Uri parsed)
    {
        if (parsed == null)
            return string.Empty;

        if (parsed.Host.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string videoId = GetQueryValue(parsed.Query, "v");

            if (!string.IsNullOrWhiteSpace(videoId))
                return "video " + Truncate(videoId, 12);
        }

        string[] segments = parsed.AbsolutePath.Trim('/').Split('/');

        for (int i = segments.Length - 1; i >= 0; i--)
        {
            string cleaned = CleanSegment(segments[i]);

            if (!string.IsNullOrWhiteSpace(cleaned))
                return Truncate(cleaned, 32);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Query))
        {
            string query = parsed.Query.TrimStart('?');
            int sep = query.IndexOf('&');

            if (sep >= 0)
                query = query.Substring(0, sep);

            return Truncate(query.Replace('=', ':'), 32);
        }

        return string.Empty;
    }

    private static string CleanSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        string decoded = Uri.UnescapeDataString(segment).Trim();
        int dot = decoded.LastIndexOf('.');

        if (dot > 0)
            decoded = decoded.Substring(0, dot);

        decoded = decoded.Replace('-', ' ').Replace('_', ' ');
        return decoded.Trim();
    }

    private static string GetQueryValue(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(key))
            return string.Empty;

        string[] pairs = query.TrimStart('?').Split('&');

        foreach (string pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
                continue;

            string[] kv = pair.Split(new[] { '=' }, 2);

            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }

        return string.Empty;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength) + "...";
    }

    public void RefreshAuthorProfilePic()
    {
        if (profilePic == null)
            return;

        if (UserProfileManager.instance == null)
        {
            profilePic.sprite = null;
            profilePic.enabled = false;
            return;
        }

        string requestAuthorId = authorId;
        pendingProfilePicAuthorId = requestAuthorId;

        UserProfileManager.instance.GetProfilePicIDForAuthor(requestAuthorId, resolvedId =>
        {
            if (pendingProfilePicAuthorId != requestAuthorId)
                return;

            Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(resolvedId);
            profilePic.sprite = sprite;
            profilePic.enabled = sprite != null;
        });
    }

    public void RefreshAuthorText()
    {
        if (user == null)
            return;

        string display = authorName;

        if (UserProfileManager.instance != null)
            display = UserProfileManager.instance.GetStoryAuthorDisplayName(authorId, authorName);
        else if (string.IsNullOrWhiteSpace(display))
            display = "Anonymous";

        user.text = "By: " + display;
    }

    public void SetTypeIcon(GoogleSheetsFetcher.Entry entry, bool isFriend = false)
    {
        if (typeIcon == null)
            return;

        Sprite sprite;

        if (GoogleSheetsFetcher.IsLandmark(entry))
            sprite = landmarkSprite;
        else if (isFriend || FriendsManager.IsFriend(entry?.User))
            sprite = friendStorySprite;
        else
            sprite = storySprite;

        typeIcon.sprite = sprite;
        typeIcon.enabled = sprite != null;
    }

    public void OpenInMaps()
    {
        float lat;
        float lon;

        if (BoundEntry != null && (BoundEntry.Latitude != 0 || BoundEntry.Longitude != 0))
        {
            lat = BoundEntry.Latitude;
            lon = BoundEntry.Longitude;
        }
        else if (CreateNewStory.instance != null &&
                 (CreateNewStory.instance.entry.Latitude != 0 || CreateNewStory.instance.entry.Longitude != 0))
        {
            lat = CreateNewStory.instance.entry.Latitude;
            lon = CreateNewStory.instance.entry.Longitude;
        }
        else
        {
            return;
        }

        MapPointerPopup.OpenInMaps(lat, lon);
    }

    public void OnDisable()
    {
        IsOpen = false;

        if (shareUI != null)
            shareUI.SetActive(false);

        MapPointer.DeselectCurrent();

        var journeyManager = JourneyManager.instance;
        if (journeyManager == null)
            return;

        var pendingStart = journeyManager.pendingJourneyPopup;
        if (pendingStart != null)
        {
            journeyManager.pendingJourneyPopup = null;
            JourneyStartedPopup.instance?.Show(pendingStart);
            pendingStart = null;
        }
    }
}