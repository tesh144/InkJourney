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
    private static readonly HashSet<string> PrivacyTags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "public", "private", "friends_only", "draft" };
    private static readonly HttpClient httpClient = new HttpClient();
    private static readonly Regex MultiSpaceRegex = new Regex("\\s+", RegexOptions.Compiled);
    private static readonly string[] ProfanityTerms = { "fuck", "shit", "cunt", "bitch", "motherfucker", "wanker", "twat" };
    private static readonly string[] SexualTerms = { "porn", "nude", "naked", "blowjob", "handjob", "cum", "semen", "vagina", "penis", "dick", "boobs", "tits", "sex" };
    private bool isEditMode = false;
    private bool _returnToMapOnFinish = false;
    private bool _photoWarningShown = false;
    private bool _screen2Touched = false;
    private Color _characterCountDefaultColor;
    private int _lastAppliedStickerID = -1;
    private Coroutine _reviewPhotoCoroutine;
    private float editOriginalLatitude;
    private float editOriginalLongitude;
    private bool hasEditOriginalLocation;

    [Header("Ink Reward")]
    public InkRewardCounter inkRewardCounter;

    [Header("Safety")]
    public bool requirePhotoModerationBeforePosting = false;

    [Header("Validation Messages")]
    public ValidationMessages validationMessages = new ValidationMessages();

    [System.Serializable]
    public class ValidationMessages
    {
        [Header("Limits")]
        public int minTitleLength   = 4;
        public int minContentLength = 20;
        public int maxContentLength = 500;

        [Header("Screen 2 — Caption")]
        public string tooShort         = "Write at least {0} characters to continue.";
        public string profanity        = "Please remove profanity before continuing.";
        public string explicitContent  = "Please remove explicit content before continuing.";
        public string lowQuality       = "Please write something more meaningful before continuing.";

        [Header("Post — Full validation")]
        public string noTitle          = "Add a title of at least {0} characters before posting.";
        public string contentTooShort  = "Write at least {0} characters before posting.";
        public string postProfanity    = "Please remove profanity from the story.";
        public string postExplicit     = "Please remove explicit sexual content.";
        public string postLowQuality   = "Please make the story clearer and more meaningful before posting.";
        public string noTags           = "Select at least one tag.";
        public string photoModeration  = "Photo safety checks are not available right now. Please remove the photo or try again later.";
    }

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
            title.onValueChanged.AddListener(_ => { entry.Title = title.text; RefreshPostValidationUI(); RecalculateInkReward(); });
        if (content != null)
            content.onValueChanged.AddListener(_ => { _screen2Touched = true; entry.Content = content.text; RefreshPostValidationUI(); RecalculateInkReward(); RefreshCharacterCount(); });

        StickerManager.OnPreviewStickerChanged += id => { RecalculateInkReward(); RefreshStickerOverlay(id); };
        FontManager.OnPreviewFontChanged       += _ => RecalculateInkReward();
        if (photoManager != null)
        {
            photoManager.onPhotoChanged += RecalculateInkReward;
            photoManager.onPhotoChanged += RefreshReviewPhoto;
            photoManager.onPhotoChanged += RefreshNoPhotoWarning;
            photoManager.onPhotoChanged += RefreshScreen3DeleteButton;
        }

        if (characterCountText != null)
            _characterCountDefaultColor = characterCountText.color;

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
        _photoWarningShown = false;
        if (noPhotoWarning != null) noPhotoWarning.SetActive(false);
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

        isEditMode              = false;
        _returnToMapOnFinish    = false;
        _photoWarningShown      = false;
        _screen2Touched         = false;
        hasEditOriginalLocation = false;
        _lastAppliedStickerID   = -1;
        if (inkRewardCounter != null) inkRewardCounter.gameObject.SetActive(true);
    }

    // Called by LibraryManager to pre-fill the panel for editing an existing story
    [Header("Creation Flow Screens")]
    public GameObject creationFlowParent;
    public GameObject screen1Canvas;
    public GameObject screen2Canvas;
    public GameObject screen3Canvas;

    [Header("Screen 1 — Photo")]
    public GameObject noPhotoWarning;

    [Header("Screen 2 — Caption")]
    public GameObject screen2NextButton;
    public TextMeshProUGUI screen2BlockedReasonText;
    public TMP_Text characterCountText;
    public GameObject editLaterButton;
    public TMP_Text editLaterButtonLabel;
    public TMP_Text editLaterButtonLabel2;
    public string editLaterDraftLabel = "Save and Edit Later";
    public string editLaterLiveLabel  = "Save Changes";

    [Header("Review Screen")]
    public TMP_Text reviewContentText;
    public RawImage reviewPhoto;
    public GameObject reviewPhotoPlaceholder;
    public DraggableStickerOverlay stickerOverlay;
    public GameObject screen3CompleteButton;
    public TextMeshProUGUI screen3BlockedReasonText;
    public GameObject screen3DeletePhotoButton;

    public void StartNewStory()
    {
        isEditMode = false;
        _returnToMapOnFinish = true;
        _photoWarningShown = false;
        entry = new GoogleSheetsFetcher.Entry();
        _lastAppliedStickerID = -1;
        StickerManager.ResetPreviewSticker();
        photoManager?.Reset();
        UpdateEntryLocation();
        ResetTagSelection();
        ThemeManager.instance?.ApplyCurrentMapStyleTheme();
        SetEditLaterLabel(true);
        if (creationFlowParent != null) creationFlowParent.SetActive(true);
        if (screen1Canvas != null) screen1Canvas.SetActive(true);
    }

    public void LoadForEdit(GoogleSheetsFetcher.Entry e)
    {
        isEditMode = true;
        _returnToMapOnFinish = false;
        inkRewardCounter?.ResetWithoutApplying();
        if (inkRewardCounter != null) inkRewardCounter.gameObject.SetActive(false);
        entry = e;
        editOriginalLatitude = e != null ? e.Latitude : 0f;
        editOriginalLongitude = e != null ? e.Longitude : 0f;
        hasEditOriginalLocation = e != null;
        title.text = e.Title;
        content.text = e.Content;
        SetSelectedTags(e.Tags);
        // Draft entries store "draft" as their privacy tag — reset to a publishable default
        if (string.Equals(_currentPrivacy, "draft", StringComparison.OrdinalIgnoreCase))
            SetPrivacy(PrivacyDefaultManager.DefaultPrivacy ?? "public");
        FontManager.SetPreviewFont(e.FontID);
        StickerManager.SetPreviewSticker(e.StickerID);
        ThemeManager.instance?.ApplyCurrentMapStyleTheme();
        SetEditLaterLabel(e.IsLocalDraft);
        SetShareButtonLabel("Update");
        RefreshPostValidationUI();
        RefreshCharacterCount();

        if (creationFlowParent != null) creationFlowParent.SetActive(true);
        if (screen1Canvas != null)     screen1Canvas.SetActive(false);
        if (screen2Canvas != null)     screen2Canvas.SetActive(true);
        if (screen3Canvas != null)     screen3Canvas.SetActive(false);
        RefreshCharacterCount();
        photoManager?.LoadExistingPhoto(e.PhotoUrl);
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
                RefreshReviewContent();
            }
        );
    }

    public void UpdateEntryContent()
    {
        entry.Content = content.text;
        LocalStoryStore.SaveOwned(entry);
        if (IsLiveEntry()) GoogleSheetsFetcher.instance?.UpdateEntryInFirestore(entry);
    }

    public void UpdateEntryTitle()
    {
        entry.Title = title.text;
        LocalStoryStore.SaveOwned(entry);
        if (IsLiveEntry()) GoogleSheetsFetcher.instance?.UpdateEntryInFirestore(entry);
    }

    public void UpdateEntryLocation()
    {
        entry.Longitude = GPSManager.Instance.longitude;
        entry.Latitude  = GPSManager.Instance.latitude;

        if (UserProfileManager.instance != null)
        {
            entry.User     = UserProfileManager.instance.UserId;
            entry.UserName = UserProfileManager.instance.HasUsername
                ? UserProfileManager.instance.Username
                : string.Empty;
        }
        else
        {
            entry.User     = SystemInfo.deviceUniqueIdentifier;
            entry.UserName = string.Empty;
        }
    }

    private void RefreshCharacterCount()
    {
        if (characterCountText == null) return;
        int current = GetUserContent().Length;
        int min     = validationMessages.minContentLength;
        int max     = validationMessages.maxContentLength;
        characterCountText.text = $"{current} / {max}";
        characterCountText.color = (current < min || current > max)
            ? Color.red
            : _characterCountDefaultColor;
    }

    // True only for stories already published to Firestore (have an ID and are not drafts).
    private bool IsLiveEntry() =>
        !string.IsNullOrEmpty(entry.ID) && !entry.IsLocalDraft;

    private void SetEditLaterLabel(bool isDraft)
    {
        string label = isDraft ? editLaterDraftLabel : editLaterLiveLabel;
        if (editLaterButtonLabel  != null) editLaterButtonLabel.text  = label;
        if (editLaterButtonLabel2 != null) editLaterButtonLabel2.text = label;
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
        StoryTagButton[] allButtons = FindObjectsByType<StoryTagButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (StoryTagButton button in allButtons)
        {
            if (button == null) continue;
            button.SetSelected(selectedTagIds.Contains(NormalizeTagId(button.tagId)));
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
        string rawTitle = title?.text?.Trim() ?? string.Empty;
        bool titleValid = !string.IsNullOrWhiteSpace(rawTitle)
            && !string.Equals(rawTitle, "ENTER TITLE", StringComparison.OrdinalIgnoreCase)
            && rawTitle.Length >= validationMessages.minTitleLength;
        inkRewardCounter.Recalculate(
            content:       content?.text ?? "",
            title:         titleValid ? rawTitle : string.Empty,
            tagCount:      selectedTagIds.Count,
            hasSticker:    StickerManager.CurrentPreviewStickerID > 0,
            hasPhoto:      photoManager != null && photoManager.CapturedPhoto != null,
            isPublic:      _currentPrivacy == "public",
            hasCustomFont: FontManager.CurrentPreviewFontID > 0
        );
    }

    private void RefreshPostValidationUI()
    {
        bool canPost = CanPost(out string postReason);

        if (postButton != null)
            postButton.SetActive(canPost);

        if (postBlockedReasonText != null)
            postBlockedReasonText.text = canPost ? string.Empty : postReason;

        if (screen3CompleteButton != null)
            screen3CompleteButton.SetActive(canPost);

        if (screen3BlockedReasonText != null)
            screen3BlockedReasonText.text = canPost ? string.Empty : postReason;

        bool canProceed = CanProceedFromScreen2(out string screen2Reason);

        if (screen2NextButton != null)
            screen2NextButton.SetActive(canProceed);

        if (screen2BlockedReasonText != null)
        {
            bool showReason = _screen2Touched && !canProceed;
            screen2BlockedReasonText.gameObject.SetActive(showReason);
            if (showReason) screen2BlockedReasonText.text = screen2Reason;
        }

        bool canSaveDraft = CanSaveDraft();
        if (editLaterButton != null)
            editLaterButton.SetActive(canSaveDraft);
    }

    private string GetUserContent()
    {
        string text = content != null ? content.text : string.Empty;
        if (InspirationPanel.instance != null)
        {
            int idx = InspirationPanel.instance.PromptInsertIndex;
            if (idx >= 0 && idx <= text.Length)
                text = text.Substring(0, idx);
        }
        return text;
    }

    private bool CanProceedFromScreen2(out string reason)
    {
        string contentText = GetUserContent();
        string contentNormalized = NormalizeSpaces(contentText);
        string contentLower = contentText.ToLowerInvariant();

        if (contentNormalized.Length < validationMessages.minContentLength)
        {
            reason = string.Format(validationMessages.tooShort, validationMessages.minContentLength);
            return false;
        }

        if (ContainsAny(contentLower, ProfanityTerms))
        {
            reason = validationMessages.profanity;
            return false;
        }

        if (ContainsAny(contentLower, SexualTerms))
        {
            reason = validationMessages.explicitContent;
            return false;
        }

        if (LooksLowQuality(contentNormalized))
        {
            reason = validationMessages.lowQuality;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool CanPost(out string reason)
    {
        string titleText = title != null ? title.text : string.Empty;
        string contentText = GetUserContent();
        string titleTrimmed = string.IsNullOrWhiteSpace(titleText) ? string.Empty : titleText.Trim();
        string contentNormalized = NormalizeSpaces(contentText);
        string lowerCombined = (titleText + " " + contentText).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(titleTrimmed)
            || string.Equals(titleTrimmed, "ENTER TITLE", StringComparison.OrdinalIgnoreCase)
            || titleTrimmed.Length < validationMessages.minTitleLength)
        {
            reason = string.Format(validationMessages.noTitle, validationMessages.minTitleLength);
            return false;
        }

        if (contentNormalized.Length < validationMessages.minContentLength)
        {
            reason = string.Format(validationMessages.contentTooShort, validationMessages.minContentLength);
            return false;
        }

        if (SelectedTagCount < 1)
        {
            reason = validationMessages.noTags;
            return false;
        }

        if (ContainsAny(lowerCombined, ProfanityTerms))
        {
            reason = validationMessages.postProfanity;
            return false;
        }

        if (ContainsAny(lowerCombined, SexualTerms))
        {
            reason = validationMessages.postExplicit;
            return false;
        }

        if (LooksLowQuality(contentNormalized))
        {
            reason = validationMessages.postLowQuality;
            return false;
        }

        if (requirePhotoModerationBeforePosting && photoManager != null && photoManager.CapturedPhoto != null)
        {
            reason = validationMessages.photoModeration;
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

        // Keyboard mash detection: a word is suspicious only if it is both long (18+ chars)
        // AND vowel-poor (under 20%). The conjunction avoids flagging real long words
        // (vowel-rich) or isolated foreign names (one odd word in normal text won't tip
        // the proportion). Only applied when there are enough words to make the ratio meaningful.
        string[] wordList = normalizedText.Split(' ');
        if (wordList.Length >= 3)
        {
            int suspicious = 0;
            foreach (string word in wordList)
            {
                if (word.Length < 18) continue;
                int vowels = 0;
                foreach (char c in word)
                    if ("aeiouAEIOU".IndexOf(c) >= 0) vowels++;
                if ((float)vowels / word.Length < 0.20f) suspicious++;
            }
            if ((float)suspicious / wordList.Length > 0.40f)
                return true;
        }

        return false;
    }

    private bool CanSaveDraft() => true;

    public void SaveLater()
    {
        entry.Theme     = ThemeManager.instance != null && ThemeManager.instance.selectedTheme != null
            ? ThemeManager.instance.selectedTheme.themeName : string.Empty;
        entry.StickerID = StickerManager.CurrentPreviewStickerID;
        entry.FontID    = FontManager.CurrentPreviewFontID;
        CaptureStickerPosition();
        string rawTitle = title != null ? title.text.Trim() : string.Empty;
        entry.Title     = string.IsNullOrWhiteSpace(rawTitle)
            || string.Equals(rawTitle, "ENTER TITLE", StringComparison.OrdinalIgnoreCase)
            ? "Untitled" : rawTitle;
        entry.Content   = !string.IsNullOrEmpty(content?.text) ? content.text : entry.Content ?? string.Empty;
        entry.IsLocalDraft = true;
        if (string.IsNullOrEmpty(entry.User))
            entry.User = UserProfileManager.instance?.UserId ?? SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(entry.UserName))
            entry.UserName = UserProfileManager.instance != null && UserProfileManager.instance.HasUsername
                ? UserProfileManager.instance.Username : string.Empty;

        UpdateEntryLocation();
        SyncSelectedTagsToEntry();
        if (!entry.Tags.Contains("draft"))
            entry.Tags.Add("draft");

        if (string.IsNullOrEmpty(entry.ID))
        {
            entry.ID      = System.Guid.NewGuid().ToString("N");
            entry.Created = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        entry.PhotoUrl = string.IsNullOrEmpty(entry.PhotoUrl) ? string.Empty : entry.PhotoUrl;

        var draftEntry = entry;

        byte[] jpegBytes = photoManager?.CapturedPhoto != null
            ? photoManager.CapturedPhoto.EncodeToJPG(75) : null;
        if (jpegBytes != null)
            PhotoUploadQueue.Enqueue(draftEntry.ID, jpegBytes);

        GoogleSheetsFetcher.instance.SpawnNewMapPointer(draftEntry);

        FinishPost();
    }

    public void ShowReviewScreen()
    {
        // Sync whichever source has content into the other so RefreshReviewContent reads correctly
        if (content != null && !string.IsNullOrEmpty(content.text))
            entry.Content = content.text;
        else if (!string.IsNullOrEmpty(entry.Content) && content != null)
            content.text = entry.Content;

        RefreshReviewContent();
        RefreshScreen3DeleteButton();
        if (screen2Canvas != null) screen2Canvas.SetActive(false);
        if (screen3Canvas != null) screen3Canvas.SetActive(true);
        if (title != null && (string.IsNullOrWhiteSpace(title.text)
            || string.Equals(title.text.Trim(), "ENTER TITLE", StringComparison.OrdinalIgnoreCase)))
            title.ActivateInputField();
        RefreshReviewPhoto();
        RefreshStickerOverlay(StickerManager.CurrentPreviewStickerID);
    }

    private void RefreshReviewContent()
    {
        if (reviewContentText == null) return;
        string text = !string.IsNullOrEmpty(entry.Content)
            ? entry.Content
            : (content != null ? content.text : string.Empty);
        reviewContentText.text = text;
    }

    private void CaptureStickerPosition()
    {
        if (stickerOverlay == null || !stickerOverlay.gameObject.activeInHierarchy) return;
        entry.StickerX     = stickerOverlay.NormalizedX;
        entry.StickerY     = stickerOverlay.NormalizedY;
        entry.StickerScale = stickerOverlay.Scale;
    }

    private void RefreshStickerOverlay(int stickerID)
    {
        if (stickerOverlay == null) return;
        if (screen3Canvas == null || !screen3Canvas.activeSelf) return;
        if (stickerID <= 0 || StickerManager.instance == null)
        {
            stickerOverlay.Clear();
            _lastAppliedStickerID = -1;
            return;
        }
        Sprite sprite = StickerManager.instance.GetSticker(stickerID);
        bool isNewSticker = stickerID != _lastAppliedStickerID;
        float nx = isNewSticker ? entry.StickerX : stickerOverlay.NormalizedX;
        float ny = isNewSticker ? entry.StickerY : stickerOverlay.NormalizedY;
        float sc = isNewSticker ? entry.StickerScale : stickerOverlay.Scale;
        stickerOverlay.SetSticker(sprite, nx, ny, sc);
        _lastAppliedStickerID = stickerID;
    }

    private void RefreshNoPhotoWarning()
    {
        if (noPhotoWarning == null) return;
        bool hasPhoto = photoManager != null && photoManager.ActivePhotoTexture != null;
        if (hasPhoto)
        {
            noPhotoWarning.SetActive(false);
            _photoWarningShown = false;
        }
        else
        {
            noPhotoWarning.SetActive(_photoWarningShown);
        }
    }

    // Wire to Screen 1 Next button instead of directly toggling the canvases
    public void TryAdvanceFromScreen1()
    {
        bool hasPhoto = photoManager != null && photoManager.ActivePhotoTexture != null;
        if (!hasPhoto && !_photoWarningShown)
        {
            _photoWarningShown = true;
            RefreshNoPhotoWarning();
            return;
        }
        if (screen1Canvas != null) screen1Canvas.SetActive(false);
        if (screen2Canvas != null) screen2Canvas.SetActive(true);
        RefreshCharacterCount();
    }

    // Wire to Screen 1 Save as Draft button instead of directly calling SaveLater
    public void TrySaveDraftFromScreen1()
    {
        bool hasPhoto = photoManager != null && photoManager.ActivePhotoTexture != null;
        if (!hasPhoto && !_photoWarningShown)
        {
            _photoWarningShown = true;
            RefreshNoPhotoWarning();
            return;
        }
        SaveLater();
    }

    private void RefreshScreen3DeleteButton()
    {
        if (screen3DeletePhotoButton == null) return;
        bool hasPhoto = (photoManager != null && photoManager.CapturedPhoto != null)
                     || !string.IsNullOrEmpty(entry.PhotoUrl)
                     || (!string.IsNullOrEmpty(entry.ID) && PhotoUploadQueue.HasPending(entry.ID));
        screen3DeletePhotoButton.SetActive(hasPhoto);
    }

    public void DeleteReviewPhoto()
    {
        entry.PhotoUrl = string.Empty;
        photoManager?.DeletePhoto();
    }

    private void RefreshReviewPhoto()
    {
        if (reviewPhoto == null) return;

        // Use any texture already in memory — avoids re-fetching from the network
        Texture2D inMemory = photoManager != null ? photoManager.ActivePhotoTexture : null;

        string photoUrl = entry.PhotoUrl;
        if (string.IsNullOrEmpty(photoUrl) && !string.IsNullOrEmpty(entry.ID) && PhotoUploadQueue.HasPending(entry.ID))
            photoUrl = "file://" + PhotoUploadQueue.FilePath(entry.ID);

        if (inMemory != null)
        {
            if (_reviewPhotoCoroutine != null) { StopCoroutine(_reviewPhotoCoroutine); _reviewPhotoCoroutine = null; }
            reviewPhoto.enabled = true;
            StoryPhotoManager.ApplyPhotoToRawImage(inMemory, reviewPhoto);
            if (reviewPhotoPlaceholder != null) reviewPhotoPlaceholder.SetActive(false);
        }
        else if (!string.IsNullOrEmpty(photoUrl))
        {
            reviewPhoto.enabled = true;
            if (reviewPhotoPlaceholder != null) reviewPhotoPlaceholder.SetActive(false);
            if (_reviewPhotoCoroutine != null) StopCoroutine(_reviewPhotoCoroutine);
            _reviewPhotoCoroutine = StartCoroutine(LoadReviewPhotoFromUrl(photoUrl));
        }
        else if (entry.Latitude != 0 || entry.Longitude != 0)
        {
            reviewPhoto.enabled = true;
            reviewPhoto.texture = null;
            if (reviewPhotoPlaceholder != null) reviewPhotoPlaceholder.SetActive(false);
            StartCoroutine(LoadReviewMapSnapshot());
        }
        else
        {
            reviewPhoto.enabled = false;
            if (reviewPhotoPlaceholder != null) reviewPhotoPlaceholder.SetActive(true);
        }
    }

    private IEnumerator LoadReviewPhotoFromUrl(string url)
    {
        Texture2D loaded = null;
        yield return PhotoAsset.FetchTexture(url, tex => loaded = tex);
        if (_reviewPhotoCoroutine == null) yield break; // cancelled by a newer load
        if (loaded != null && reviewPhoto != null)
            StoryPhotoManager.ApplyPhotoToRawImage(loaded, reviewPhoto);
        _reviewPhotoCoroutine = null;
    }

    private IEnumerator LoadReviewMapSnapshot()
    {
        if (reviewPhoto == null) yield break;
        string styleId = !string.IsNullOrEmpty(MapLoader.instance?.mapStyle) ? MapLoader.instance.mapStyle : "mapbox/dark-v11";
        string token   = MapLoader.instance?.mapboxToken ?? "";
        string pin     = UI_StoryPanel.MapPinColor(entry);
        string url     = $"https://api.mapbox.com/styles/v1/{styleId}/static/pin-l+{pin}({entry.Longitude},{entry.Latitude})/{entry.Longitude},{entry.Latitude},12,0/640x360@2x?access_token={token}";

        yield return MapboxImageCache.Fetch(url, tex =>
        {
            if (reviewPhoto == null || tex == null) return;
            if (photoManager != null && photoManager.ActivePhotoTexture != null) return;
            reviewPhoto.color   = Color.white;
            reviewPhoto.texture = tex;
        });
    }

    public void Preview()
    {
        entry.Theme     = ThemeManager.instance != null && ThemeManager.instance.selectedTheme != null
            ? ThemeManager.instance.selectedTheme.themeName
            : string.Empty;
        entry.StickerID = StickerManager.CurrentPreviewStickerID;
        entry.FontID    = FontManager.CurrentPreviewFontID;
        CaptureStickerPosition();

        if (isEditMode)
        {
            entry.Title   = title.text;
            entry.Content = content.text;
            SyncSelectedTagsToEntry();
        }
        else
        {
            UpdateEntryLocation();
        }

        storyPanel.SetContentText(entry.Content);
        storyPanel.title.text = entry.Title;
        storyPanel.SetAuthor(entry.User, entry.UserName);
        storyPanel.BindStoryEntry(null);
        storyPanel.PositionSticker(entry);
        storyPanel.SetTags(entry.Tags);
        storyPanel.likes.text = "0";
        storyPanel.views.text = CompactCountFormatter.FormatViews(0);
        storyPanel.location_expire_text.text = "Preview";
        storyPanel.SetTypeIcon(entry);

        storyPanel.shareUI.gameObject.SetActive(true);
        storyPanel.gameObject.SetActive(true); // activate first so container rect is valid

        Texture2D previewPhoto = photoManager?.ActivePhotoTexture;
        if (previewPhoto != null)
            storyPanel.SetPhoto(previewPhoto);
        else
            storyPanel.SetPhoto(entry.PhotoUrl, entry.ID);
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
        CaptureStickerPosition();

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

        if (_returnToMapOnFinish)
        {
            MapInputController.instance?.SnapToMaxZoom();
            MapLoader.instance?.resetScrollRect?.ResetToCentre();
        }
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

        bool wasDraft = e.IsLocalDraft;
        e.IsLocalDraft = false;
        if (wasDraft)
        {
            if (e.Created == 0) e.Created = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (e.Expire  == 0) e.Expire  = StoryLifetimeManager.instance != null
                ? StoryLifetimeManager.instance.GetInitialExpire()
                : DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
        }
        e.Tags?.Remove("draft");

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

        LocalStoryStore.SaveOwned(e);
        e.pointer?.BindEntry(e);
        if (e.pointer != null)
        {
            var sm = e.pointer.GetComponent<GoogleSheetManager>();
            if (sm != null)
            {
                sm.title   = e.Title;
                sm.content = e.Content;
                sm.theme   = e.Theme;
                sm.photoUrl = e.PhotoUrl ?? string.Empty;
            }
        }
        LibraryManager.instance?.PopulateList();
        isEditMode = false;
        hasEditOriginalLocation = false;
        photoManager?.Reset();
        FinishPost();
    }
}
