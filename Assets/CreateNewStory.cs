using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;
using Firebase.Storage;

public class CreateNewStory : MonoBehaviour
{
    public static CreateNewStory instance;

    public GoogleSheetsFetcher.Entry entry;
    public TMP_InputField title;
    public TMP_InputField content;
    public UI_StoryPanel storyPanel;
    public GameObject postButton;
    public TextMeshProUGUI postBlockedReasonText;
    public TMP_Text shareButtonLabel;
    public TMP_Text shareButtonLabel2;

    public GoogleSheetsFetcher.Entry starEntry;

    [Header("Tag selection")]
    public List<StoryTagButton> tagButtons = new List<StoryTagButton>();

    public StoryPhotoManager photoManager;

    private HashSet<string> selectedTagIds = new HashSet<string>();
    private List<PrivacyButton> privacyButtons = new List<PrivacyButton>();
    private string _currentPrivacy = "public";
    private static readonly HashSet<string> PrivacyTags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "public", "private", "friends_only" };
    private static readonly HttpClient httpClient = new HttpClient();
    private static readonly Regex MultiSpaceRegex = new Regex("\\s+", RegexOptions.Compiled);
    private static readonly string[] ProfanityTerms = { "fuck", "shit", "cunt", "bitch", "motherfucker", "wanker", "twat" };
    private static readonly string[] SexualTerms = { "porn", "nude", "naked", "blowjob", "handjob", "cum", "semen", "vagina", "penis", "dick", "boobs", "tits", "sex" };
    private bool isEditMode = false;
    private float editOriginalLatitude;
    private float editOriginalLongitude;
    private bool hasEditOriginalLocation;

    [Header("Ink Reward")]
    public InkRewardCounter inkRewardCounter;

    [Header("Safety")]
    public bool requirePhotoModerationBeforePosting = false;

    public string CurrentPrivacy => _currentPrivacy;

    private void Awake()
    {
        instance = this;

        if (tagButtons == null || tagButtons.Count == 0)
            tagButtons = new List<StoryTagButton>(GetComponentsInChildren<StoryTagButton>(true));

        privacyButtons = new List<PrivacyButton>(GetComponentsInChildren<PrivacyButton>(true));
    }

    private void Start()
    {
        // Title: short text — standard TMP field with suggestion bar
        title.shouldHideMobileInput = true;
        title.inputType = TMPro.TMP_InputField.InputType.AutoCorrect;

        // Content: non-interactable on device — the transparent ContentTapHandler overlay
        // intercepts taps and routes them to the native UITextView. Keeping it interactable
        // only in Editor would otherwise trigger the Unity keyboard on scroll.
#if !UNITY_EDITOR
        content.interactable = false;
#endif
        RefreshTagButtons();
        RefreshTagButtonStates();

        if (title != null)
            title.onValueChanged.AddListener(_ => { RefreshPostValidationUI(); RecalculateInkReward(); });
        if (content != null)
            content.onValueChanged.AddListener(_ => { RefreshPostValidationUI(); RecalculateInkReward(); });

        StickerManager.OnPreviewStickerChanged += _ => RecalculateInkReward();
        FontManager.OnPreviewFontChanged       += _ => RecalculateInkReward();
        if (photoManager != null) photoManager.onPhotoChanged += RecalculateInkReward;

        RefreshPostValidationUI();
    }

    void OnEnable()
    {
        privacyButtons = new List<PrivacyButton>(GetComponentsInChildren<PrivacyButton>(true));

        if (!isEditMode)
        {
            ResetTagSelection();
            UpdateEntryLocation();
            StickerManager.ResetPreviewSticker();
            FontManager.ResetPreviewFont();
            SetPrivacy(PrivacyDefaultManager.DefaultPrivacy ?? "public");
        }

        RefreshPrivacyButtons();
        RefreshTagButtonStates();
        RefreshPostValidationUI();
        NativeTextEditor.Prewarm();
    }

    void OnDisable()
    {
        NativeTextEditor.StopPrewarm();

        title.text        = "ENTER TITLE";
        content.text      = string.Empty;
        entry             = new GoogleSheetsFetcher.Entry();
        selectedTagIds.Clear();
        _currentPrivacy   = PrivacyDefaultManager.DefaultPrivacy ?? "public";
        SyncSelectedTagsToEntry();
        photoManager?.Reset();

        isEditMode            = false;
        hasEditOriginalLocation = false;
        if (inkRewardCounter != null) inkRewardCounter.gameObject.SetActive(true);
    }

    // Called by LibraryManager to pre-fill the panel for editing an existing story
    public void LoadForEdit(GoogleSheetsFetcher.Entry e)
    {
        isEditMode = true;
        inkRewardCounter?.ResetWithoutApplying();
        if (inkRewardCounter != null) inkRewardCounter.gameObject.SetActive(false);
        entry = e;
        editOriginalLatitude = e != null ? e.Latitude : 0f;
        editOriginalLongitude = e != null ? e.Longitude : 0f;
        hasEditOriginalLocation = e != null;
        title.text = e.Title;
        content.text = e.Content;
        SetSelectedTags(e.Tags);
        photoManager?.LoadExistingPhoto(e.PhotoUrl);
        FontManager.SetPreviewFont(e.FontID);
        StickerManager.SetPreviewSticker(e.StickerID);
        SetShareButtonLabel("Update");
        RefreshPostValidationUI();
    }

    /// <summary>
    /// Called by a Button overlaid on the content field (or any tap event).
    /// Opens the native iOS full-screen text editor.
    /// </summary>
    public void OpenContentEditor()
    {
        NativeTextEditor.Show(
            title, content,
            placeholder: "Write your story…",
            onComplete: (t, c) =>
            {
                entry.Title = t;
                entry.Content = c;
                RefreshPostValidationUI();
                RecalculateInkReward();
            }
        );
    }

    public void UpdateEntryContent()
    {
        entry.Content = content.text;
    }

    public void UpdateEntryTitle()
    {
        entry.Title = title.text;
    }

    public void UpdateEntryLocation()
    {
        entry.Longitude = GPSManager.Instance.longitude;
        entry.Latitude = GPSManager.Instance.latitude;

        if (UserProfileManager.instance != null)
        {
            entry.User = UserProfileManager.instance.UserId;
            entry.UserName = UserProfileManager.instance.HasUsername
                ? UserProfileManager.instance.Username
                : string.Empty;
        }
        else
        {
            entry.User = SystemInfo.deviceUniqueIdentifier;
            entry.UserName = string.Empty;
        }
    }

    private void SyncSelectedTagsToEntry()
    {
        if (entry.Tags == null)
            entry.Tags = new List<string>();

        entry.Tags.Clear();
        foreach (string tag in selectedTagIds)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !entry.Tags.Contains(tag))
                entry.Tags.Add(tag);
        }

        if (!string.IsNullOrWhiteSpace(_currentPrivacy))
            entry.Tags.Add(_currentPrivacy);
    }

    public void SetPrivacy(string privacyTag)
    {
        if (string.IsNullOrWhiteSpace(privacyTag))
            return;

        _currentPrivacy = privacyTag.Trim();
        SyncSelectedTagsToEntry();
        RefreshPrivacyButtons();
        RecalculateInkReward();
    }

    public void RegisterPrivacyButton(PrivacyButton btn)
    {
        if (btn != null && !privacyButtons.Contains(btn))
            privacyButtons.Add(btn);
    }

    private void RefreshPrivacyButtons()
    {
        if (privacyButtons == null || privacyButtons.Count == 0)
            privacyButtons = new List<PrivacyButton>(GetComponentsInChildren<PrivacyButton>(true));

        foreach (PrivacyButton btn in privacyButtons)
        {
            if (btn != null)
                btn.SetSelected(btn.privacyTag == _currentPrivacy);
        }
    }

    public int SelectedTagCount => selectedTagIds.Count;
    public IReadOnlyCollection<string> SelectedTagIds => selectedTagIds;

    private string NormalizeTagId(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return string.Empty;

        if (TagManager.instance != null)
            return TagManager.instance.GetCanonicalTagId(tagId.Trim());

        return tagId.Trim();
    }

    public bool IsTagSelected(string tagId)
    {
        return selectedTagIds.Contains(NormalizeTagId(tagId));
    }

    public bool CanSelectTag(string tagId)
    {
        string normalizedId = NormalizeTagId(tagId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return false;

        if (selectedTagIds.Contains(normalizedId))
            return true;

        int maxTags = TagManager.instance != null ? TagManager.instance.maxStoryTags : 3;
        return selectedTagIds.Count < maxTags;
    }

    public void AddTag(string tagId)
    {
        string normalizedId = NormalizeTagId(tagId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return;

        int maxTags = TagManager.instance != null ? TagManager.instance.maxStoryTags : 3;
        if (selectedTagIds.Contains(normalizedId) || selectedTagIds.Count >= maxTags)
            return;

        selectedTagIds.Add(normalizedId);
        SyncSelectedTagsToEntry();
        TagManager.instance?.UpdateStoryTagCount(selectedTagIds.Count);
        RefreshTagButtonStates();
        RefreshPostValidationUI();
        RecalculateInkReward();
    }

    public void RemoveTag(string tagId)
    {
        string normalizedId = NormalizeTagId(tagId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return;

        if (!selectedTagIds.Remove(normalizedId))
            return;

        SyncSelectedTagsToEntry();
        TagManager.instance?.UpdateStoryTagCount(selectedTagIds.Count);
        RefreshTagButtonStates();
        RefreshPostValidationUI();
        RecalculateInkReward();
    }

    public void SetSelectedTags(IEnumerable<string> tags)
    {
        selectedTagIds.Clear();
        _currentPrivacy = "public";
        if (tags != null)
        {
            foreach (string tag in tags)
            {
                if (PrivacyTags.Contains(tag))
                {
                    _currentPrivacy = tag;
                    continue;
                }
                string normalized = NormalizeTagId(tag);
                if (!string.IsNullOrWhiteSpace(normalized))
                    selectedTagIds.Add(normalized);
            }
        }

        SyncSelectedTagsToEntry();
        RefreshTagButtons();
        RefreshPrivacyButtons();
        TagManager.instance?.UpdateStoryTagCount(selectedTagIds.Count);
        RefreshPostValidationUI();
        RecalculateInkReward();
    }

    private void RefreshTagButtons()
    {
        RefreshTagButtonStates();
    }

    public void RefreshTagButtonStates()
    {
        foreach (StoryTagButton button in tagButtons)
        {
            if (button == null)
                continue;

            string buttonId = NormalizeTagId(button.tagId);
            bool selected = selectedTagIds.Contains(buttonId);
            button.SetSelected(selected);
        }
    }

    public void ResetTagSelection()
    {
        selectedTagIds.Clear();
        _currentPrivacy = PrivacyDefaultManager.DefaultPrivacy ?? "public";
        SyncSelectedTagsToEntry();
        RefreshTagButtonStates();
        RefreshPrivacyButtons();
        TagManager.instance?.UpdateStoryTagCount(selectedTagIds.Count);
        RefreshPostValidationUI();
    }

    private void RecalculateInkReward()
    {
        if (inkRewardCounter == null || isEditMode) return;
        inkRewardCounter.Recalculate(
            content:       content?.text ?? "",
            title:         title?.text ?? "",
            tagCount:      selectedTagIds.Count,
            hasSticker:    StickerManager.CurrentPreviewStickerID > 0,
            hasPhoto:      photoManager != null && photoManager.CapturedPhoto != null,
            isPublic:      _currentPrivacy == "public",
            hasCustomFont: FontManager.CurrentPreviewFontID > 0
        );
    }

    private void RefreshPostValidationUI()
    {
        bool canPost = CanPost(out string reason);

        if (postButton != null)
            postButton.SetActive(canPost);

        if (postBlockedReasonText != null)
            postBlockedReasonText.text = canPost ? string.Empty : reason;
    }

    private bool CanPost(out string reason)
    {
        string titleText = title != null ? title.text : string.Empty;
        string contentText = content != null ? content.text : string.Empty;
        string titleTrimmed = string.IsNullOrWhiteSpace(titleText) ? string.Empty : titleText.Trim();
        string contentNormalized = NormalizeSpaces(contentText);
        string lowerCombined = (titleText + " " + contentText).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(titleTrimmed) || string.Equals(titleTrimmed, "ENTER TITLE", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Add a title before posting.";
            return false;
        }

        if (contentNormalized.Length < 20)
        {
            reason = "Write at least 20 characters before posting.";
            return false;
        }

        if (SelectedTagCount < 1)
        {
            reason = "Select at least one tag.";
            return false;
        }

        if (ContainsAny(lowerCombined, ProfanityTerms))
        {
            reason = "Please remove profanity from the story.";
            return false;
        }

        if (ContainsAny(lowerCombined, SexualTerms))
        {
            reason = "Please remove explicit sexual content.";
            return false;
        }

        if (LooksLowQuality(contentNormalized))
        {
            reason = "Please make the story clearer and more meaningful before posting.";
            return false;
        }

        if (requirePhotoModerationBeforePosting && photoManager != null && photoManager.CapturedPhoto != null)
        {
            reason = "Photo safety checks are not available right now. Please remove the photo or try again later.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool ContainsAny(string haystackLower, string[] blockedTerms)
    {
        if (string.IsNullOrEmpty(haystackLower) || blockedTerms == null)
            return false;

        foreach (string term in blockedTerms)
        {
            if (string.IsNullOrWhiteSpace(term))
                continue;

            if (haystackLower.Contains(term))
                return true;
        }

        return false;
    }

    private static string NormalizeSpaces(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return MultiSpaceRegex.Replace(text, " ").Trim();
    }

    private static bool LooksLowQuality(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return true;

        string noSpaces = normalizedText.Replace(" ", string.Empty);
        if (noSpaces.Length < 20)
            return true;

        int letterCount = 0;
        int digitCount = 0;
        foreach (char c in normalizedText)
        {
            if (char.IsLetter(c)) letterCount++;
            else if (char.IsDigit(c)) digitCount++;
        }

        float letterRatio = normalizedText.Length > 0 ? (float)letterCount / normalizedText.Length : 0f;
        if (letterRatio < 0.45f)
            return true;

        // Reject repetitive patterns like "aaaaaa" or "lolololol".
        int repeats = 0;
        for (int i = 1; i < noSpaces.Length; i++)
        {
            if (char.ToLowerInvariant(noSpaces[i]) == char.ToLowerInvariant(noSpaces[i - 1]))
                repeats++;
        }

        float repeatRatio = noSpaces.Length > 1 ? (float)repeats / (noSpaces.Length - 1) : 0f;
        if (repeatRatio > 0.6f)
            return true;

        // Excessive digit spam usually indicates low-quality input.
        if (digitCount > 0 && ((float)digitCount / Mathf.Max(1, normalizedText.Length)) > 0.35f)
            return true;

        return false;
    }

    public void Preview()
    {
        entry.Theme     = ThemeManager.instance.selectedTheme.themeName;
        entry.StickerID = StickerManager.CurrentPreviewStickerID;
        entry.FontID    = FontManager.CurrentPreviewFontID;

        // Editing an existing story — skip preview, update immediately and close
        if (isEditMode)
        {
            entry.Title   = title.text;
            entry.Content = content.text;
            FinishEdit(entry);
            return;
        }

        UpdateEntryLocation();
        storyPanel.SetContentText(entry.Content);
        storyPanel.title.text = entry.Title;
        storyPanel.SetAuthor(entry.User, entry.UserName);
        storyPanel.BindStoryEntry(null);
        storyPanel.SetTags(entry.Tags);
        storyPanel.likes.text = "0";
        storyPanel.views.text = CompactCountFormatter.FormatViews(0);
        storyPanel.location_expire_text.text = "Preview";

        entry.ID = $"{entry.User}_{entry.Latitude}_{entry.Longitude}";
        entry.Created = 1738925500;
        entry.Expire = StoryLifetimeManager.instance != null
            ? StoryLifetimeManager.instance.GetInitialExpire()
            : DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();

        storyPanel.SetTypeIcon(entry);
        storyPanel.SetPhoto(photoManager != null ? photoManager.CapturedPhoto : null);
        storyPanel.shareUI.gameObject.SetActive(true);
        storyPanel.gameObject.SetActive(true);
    }

    public void PreviewStarEntry()
    {
        storyPanel.SetContentText(starEntry.Content);
        storyPanel.title.text = starEntry.Title;
        storyPanel.SetAuthor(starEntry.User, starEntry.UserName);
        storyPanel.BindStoryEntry(null);
        storyPanel.SetTags(starEntry.Tags);
        storyPanel.likes.text = "0";
        storyPanel.views.text = CompactCountFormatter.FormatViews(0);
        storyPanel.location_expire_text.text = "Never";
        ThemeManager.instance.SetTheme(starEntry.Theme);
        storyPanel.SetTypeIcon(starEntry);

        entry.ID = $"{entry.User}_{entry.Latitude}_{entry.Longitude}";
        entry.Created = 1738925500;
        entry.Expire = StoryLifetimeManager.instance != null
            ? StoryLifetimeManager.instance.GetInitialExpire()
            : DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();

        storyPanel.gameObject.SetActive(true);
    }

    public void PostStory()
    {
        if (!CanPost(out string reason))
        {
            if (postBlockedReasonText != null)
                postBlockedReasonText.text = reason;
            if (postButton != null)
                postButton.SetActive(false);
            return;
        }

        Debug.Log($"[PostStory] Title='{entry.Title}' Lat={entry.Latitude} Lon={entry.Longitude}");
        if (string.IsNullOrEmpty(entry.Theme))
            entry.Theme = ThemeManager.instance.selectedTheme.themeName;

        entry.StickerID = StickerManager.CurrentPreviewStickerID;
        entry.FontID    = FontManager.CurrentPreviewFontID;

        if (isEditMode)
        {
            entry.Title = title.text;
            entry.Content = content.text;
            SyncSelectedTagsToEntry();
            Debug.Log($"[Edit] Saving ID='{entry.ID}' Title='{entry.Title}'");
            FinishEdit(entry);
            return;
        }

        UpdateEntryLocation();
        entry.UserName = UserProfileManager.instance != null && UserProfileManager.instance.HasUsername
            ? UserProfileManager.instance.Username
            : string.Empty;
        SyncSelectedTagsToEntry();
        entry.ID = System.Guid.NewGuid().ToString("N");
        entry.Created = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        entry.Expire = StoryLifetimeManager.instance != null
            ? StoryLifetimeManager.instance.GetInitialExpire()
            : DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
        entry.PhotoUrl = "";

        var postEntry = entry;

        // Encode and cache the JPEG now, before FinishPost destroys the texture.
        byte[] jpegBytes = photoManager?.CapturedPhoto != null
            ? photoManager.CapturedPhoto.EncodeToJPG(75)
            : null;

        if (jpegBytes != null)
            PhotoUploadQueue.Enqueue(postEntry.ID, jpegBytes);

        // Spawn pin immediately — visible feedback before upload finishes
        GoogleSheetsFetcher.instance.SpawnNewMapPointer(postEntry);

        // Queue reward into InkManager before FinishPost clears the form
        if (inkRewardCounter != null && !isEditMode)
            InkManager.instance?.QueueGain(inkRewardCounter.CurrentReward);
        inkRewardCounter?.ResetWithoutApplying();

        FinishPost();

        if (jpegBytes != null)
            GPSManager.Instance.StartCoroutine(UploadPhotoAndUpdate(postEntry, jpegBytes));
    }

    private IEnumerator UploadPhotoAndUpdate(GoogleSheetsFetcher.Entry e, byte[] jpegBytes)
    {
        Debug.Log($"[Photo] Upload starting for '{e.Title}'");
        string path = $"photos/{e.ID}.jpg";
        var storageRef = FirebaseStorage.DefaultInstance.GetReference(path);

        var uploadTask = storageRef.PutBytesAsync(jpegBytes);
        yield return new WaitUntil(() => uploadTask.IsCompleted);

        if (uploadTask.IsFaulted || uploadTask.IsCanceled)
        {
            Debug.LogError($"[Photo] Upload failed: {uploadTask.Exception}");
            yield break;
        }

        var urlTask = storageRef.GetDownloadUrlAsync();
        yield return new WaitUntil(() => urlTask.IsCompleted);

        if (!urlTask.IsFaulted && !urlTask.IsCanceled)
        {
            string photoUrl = urlTask.Result.ToString();
            GoogleSheetsFetcher.instance.UpdatePhotoUrl(e.ID, photoUrl);
            e.PhotoUrl = photoUrl;
            LocalStoryStore.SaveOwned(e);
            PhotoUploadQueue.Dequeue(e.ID);

            // Update the live map pin so tapping it immediately shows the photo
            if (e.pointer != null)
            {
                var sm = e.pointer.GetComponent<GoogleSheetManager>();
                if (sm != null) sm.photoUrl = photoUrl;
            }

            Debug.Log($"[Photo] Upload complete: {photoUrl}");
        }
        else
        {
            Debug.LogError($"[Photo] Failed to get download URL: {urlTask.Exception}");
        }
    }

    private void FinishPost()
    {
        Analytics.StoryPosted();
        entry = new GoogleSheetsFetcher.Entry();
        ResetTagSelection();
        content.text = "";
        title.text = "ENTER TITLE";
        SetShareButtonLabel("Preview");
        ObjectManager.instance.createStoryPanel.SetActive(false);
        RefreshPostValidationUI();

        MapInputController.instance?.SnapToMaxZoom();
        MapLoader.instance?.resetScrollRect?.ResetToCentre();
    }

    private void SetShareButtonLabel(string label)
    {
        if (shareButtonLabel != null) shareButtonLabel.text = label;
        if (shareButtonLabel2 != null) shareButtonLabel2.text = label;
    }

    // Edit mode — update the story; if a new photo was taken, replace the old one in Storage
    private void FinishEdit(GoogleSheetsFetcher.Entry e)
    {
        if (hasEditOriginalLocation)
        {
            // Editing must never move a story; keep original coordinates.
            e.Latitude = editOriginalLatitude;
            e.Longitude = editOriginalLongitude;
        }

        e.LastUpdated = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var newPhoto = photoManager?.CapturedPhoto;
        byte[] editJpeg = newPhoto != null ? newPhoto.EncodeToJPG(75) : null;
        if (editJpeg != null)
        {
            PhotoUploadQueue.Enqueue(e.ID, editJpeg);
            GPSManager.Instance.StartCoroutine(UploadPhotoAndUpdate(e, editJpeg));
        }
        else
        {
            GoogleSheetsFetcher.instance.UpdateEntryInFirestore(e);
        }

        if (e.pointer != null) e.pointer.textbox.text = e.Title;
        LibraryManager.instance?.PopulateList();
        isEditMode = false;
        hasEditOriginalLocation = false;
        photoManager?.Reset();
        FinishPost();
    }
}
