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
    public Image profilePic;
    public Image backdrop;
    public Image cover;
    public List<Image> lines_buttons;
    public LikeButton likeButton;
    public StoryCommentsPanel commentsPanel;

    private string pendingProfilePicAuthorId;

    [HideInInspector] public string currentStoryId;
    public GoogleSheetsFetcher.Entry BoundEntry { get; private set; }

    private static readonly HashSet<string> HiddenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "public", "private", "friends_only"
    };

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

        bool hasPhoto = !string.IsNullOrEmpty(photoUrl);

        if (photoContainer != null)
            photoContainer.SetActive(hasPhoto);

        if (hasPhoto && photoImage != null && MapLoader.instance != null)
            MapLoader.instance.StartCoroutine(LoadPhotoFromUrl(photoUrl));
    }

    private IEnumerator LoadPhotoFromUrl(string url)
    {
        using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                photoImage.texture = ((UnityEngine.Networking.DownloadHandlerTexture)req.downloadHandler).texture;

                if (photoContainer != null)
                    photoContainer.SetActive(true);
            }
            else
            {
                Debug.LogWarning($"[Photo] Failed to load: {req.error}");

                if (photoContainer != null)
                    photoContainer.SetActive(false);
            }
        }
    }

    public void SetPhoto(Texture2D texture)
    {
        bool hasPhoto = texture != null;

        if (photoContainer != null)
            photoContainer.SetActive(hasPhoto);

        if (hasPhoto && photoImage != null)
            photoImage.texture = texture;
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
            JourneyManager.instance?.OnStoryRead(entry.ID);
        }

        if (likes != null)
            likes.text = entry != null ? CompactCountFormatter.FormatLikes(entry.Likes) : "0";

        if (likeButton != null)
        {
            if (likeButton.likesCountText == null)
                likeButton.likesCountText = likes;

            likeButton.BindEntry(entry);
        }

        SetTags(entry != null ? entry.Tags : null);

        if (commentsPanel != null)
        {
            commentsPanel.SetCountDisplay(commentCountText);
            commentsPanel.BindStory(entry);
        }
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
                builder.Append('\n');

            builder.Append('#').Append(display.Trim().Replace(" ", "_"));
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