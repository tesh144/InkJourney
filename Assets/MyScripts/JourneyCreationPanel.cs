using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JourneyCreationPanel : MonoBehaviour
{
    [Header("Inputs")]
    public TMP_InputField titleInput;
    public TMP_InputField descriptionInput;

    [Header("UI")]
    public TextMeshProUGUI storyCountText;
    public PhotoShuffle photoShuffle;
    public GameObject distanceLimitWarning;
    public Button addStoryButton;
    public Button completeButton;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void OnEnable()
    {
        JourneyManager.onStoryAddedToJourney                 += OnStoryAdded;
        JourneyManager.onJourneyCreationDistanceLimitReached += OnDistanceLimitReached;
        FontManager.OnPreviewFontChanged                     += OnFontChanged;
        StickerManager.OnPreviewStickerChanged               += OnStickerChanged;

        if (titleInput != null)
        {
            SetPlaceholder(titleInput, "Untitled");
            titleInput.onValueChanged.AddListener(OnTitleChanged);
        }
        if (descriptionInput != null)
        {
            SetPlaceholder(descriptionInput, "Add description...");
            descriptionInput.onValueChanged.AddListener(OnDescriptionChanged);
        }

        RefreshUI();
        RestoreFont();
        RestoreSticker();
        PopulatePhotoShuffle();
    }

    private void OnDisable()
    {
        JourneyManager.onStoryAddedToJourney                 -= OnStoryAdded;
        JourneyManager.onJourneyCreationDistanceLimitReached -= OnDistanceLimitReached;
        FontManager.OnPreviewFontChanged                     -= OnFontChanged;
        StickerManager.OnPreviewStickerChanged               -= OnStickerChanged;

        if (titleInput != null)
            titleInput.onValueChanged.RemoveListener(OnTitleChanged);
        if (descriptionInput != null)
            descriptionInput.onValueChanged.RemoveListener(OnDescriptionChanged);
    }

    // ── Public API ────────────────────────────────────────────────────────

    // Wire to "Create Journey" button
    public void BeginCreation()
    {
        JourneyManager.instance?.BeginCreatingJourney();

        // Spawn the JourneyObject card in the library immediately
        JourneyLibraryPanel.instance?.PopulateJourneysList();

        UIStateManager.instance?.ActivateCreateJourneySubState();

        if (titleInput != null) titleInput.SetTextWithoutNotify("");
        if (descriptionInput != null) descriptionInput.SetTextWithoutNotify("");
        if (distanceLimitWarning != null) distanceLimitWarning.SetActive(false);
        if (addStoryButton != null) addStoryButton.interactable = true;
        photoShuffle?.Clear();
        RefreshUI();
    }

    // Wire to "Complete" button
    public void FinishCreation()
    {
        if (JourneyManager.instance == null || !JourneyManager.instance.isCreatingJourney) return;
        bool wasEdit = JourneyManager.instance.isEditMode;
        JourneyManager.instance.FinishCreatingJourney(
            titleInput?.text ?? "",
            descriptionInput?.text ?? "");
        if (wasEdit)
            UIStateManager.instance?.ActivateSubState(UIStateManager.instance.journeyDefaultSubState);
    }

    // Wire to delete button
    public void DeleteJourney()
    {
        if (JourneyManager.instance?.creatingJourney == null) return;
        var journey = JourneyManager.instance.creatingJourney;
        JourneyManager.instance.DeleteJourney(journey);
        UIStateManager.instance?.ActivateSubState(UIStateManager.instance.journeyDefaultSubState);
    }

    // Wire to cancel/X button
    public void CancelCreation()
    {
        JourneyManager.instance?.CancelCreatingJourney();
        UIStateManager.instance?.ActivateSubState(UIStateManager.instance.journeyDefaultSubState);
    }

    // ── Callbacks ─────────────────────────────────────────────────────────

    private void OnTitleChanged(string value)
    {
        if (JourneyManager.instance?.creatingJourney == null) return;
        JourneyManager.instance.creatingJourney.Title = value;
        JourneyManager.instance.SaveCreatingJourney();
        RefreshCompleteButton();
    }

    private void OnDescriptionChanged(string value)
    {
        if (JourneyManager.instance?.creatingJourney == null) return;
        JourneyManager.instance.creatingJourney.Description = value;
        JourneyManager.instance.SaveCreatingJourney();
        RefreshCompleteButton();
    }

    private void OnStickerChanged(int stickerID)
    {
        if (JourneyManager.instance?.creatingJourney == null) return;
        JourneyManager.instance.creatingJourney.StickerID = stickerID;
        JourneyManager.instance.SaveCreatingJourney();
    }

    private void OnFontChanged(int fontID)
    {
        ApplyFont(fontID);
        if (JourneyManager.instance?.creatingJourney == null) return;
        JourneyManager.instance.creatingJourney.FontID = fontID;
        JourneyManager.instance.SaveCreatingJourney();
    }

    private void OnStoryAdded(GoogleSheetsFetcher.Entry story)
    {
        RefreshUI();
        string url = ResolvePhotoUrl(story);
        if (!string.IsNullOrEmpty(url))
            photoShuffle?.AddPhoto(url);
    }

    private void OnDistanceLimitReached()
    {
        if (distanceLimitWarning != null) distanceLimitWarning.SetActive(true);
        if (addStoryButton != null) addStoryButton.interactable = false;
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        var j = JourneyManager.instance?.creatingJourney;
        int count = j?.Chapters?.Count ?? 0;
        if (storyCountText != null)
            storyCountText.text = count == 1 ? "1 story" : $"{count} stories";

        if (j != null)
        {
            if (titleInput != null && titleInput.text != j.Title)
                titleInput.SetTextWithoutNotify(j.Title ?? "");
            if (descriptionInput != null && descriptionInput.text != j.Description)
                descriptionInput.SetTextWithoutNotify(j.Description ?? "");
        }

        RefreshCompleteButton();
    }

    private void RefreshCompleteButton()
    {
        if (completeButton == null) return;
        var j = JourneyManager.instance?.creatingJourney;
        int count = j?.Chapters?.Count ?? 0;
        bool valid = count > 0
            && !string.IsNullOrWhiteSpace(titleInput?.text)
            && !string.IsNullOrWhiteSpace(descriptionInput?.text);
        completeButton.gameObject.SetActive(valid);
    }

    private void PopulatePhotoShuffle()
    {
        if (photoShuffle == null) return;
        var j = JourneyManager.instance?.creatingJourney;
        if (j?.Chapters == null || j.Chapters.Count == 0) return;

        var fetcher = GoogleSheetsFetcher.instance;
        var urls = new System.Collections.Generic.List<string>();
        foreach (var chapter in j.Chapters)
        {
            if (string.IsNullOrEmpty(chapter.StoryId)) continue;
            var story = fetcher?.storiesList?.Find(e => e?.ID == chapter.StoryId)
                     ?? fetcher?.landmarksList?.Find(e => e?.ID == chapter.StoryId);
            string url = ResolvePhotoUrl(story);
            if (!string.IsNullOrEmpty(url))
                urls.Add(url);
        }
        if (urls.Count > 0)
            photoShuffle.SpawnPhotos(urls);
    }

    private static string ResolvePhotoUrl(GoogleSheetsFetcher.Entry story)
    {
        if (story == null) return null;
        if (!string.IsNullOrEmpty(story.PhotoUrl)) return story.PhotoUrl;
        if (!string.IsNullOrEmpty(story.ID) && PhotoUploadQueue.HasPending(story.ID))
            return "file://" + PhotoUploadQueue.FilePath(story.ID);
        return null;
    }

    private void RestoreSticker()
    {
        int stickerID = JourneyManager.instance?.creatingJourney?.StickerID ?? 0;
        StickerManager.SetPreviewSticker(stickerID);
    }

    private void RestoreFont()
    {
        int fontID = JourneyManager.instance?.creatingJourney?.FontID ?? 0;
        FontManager.SetPreviewFont(fontID);
        ApplyFont(fontID);
    }

    private void ApplyFont(int fontID)
    {
        if (titleInput == null || FontManager.instance == null) return;
        var font = FontManager.instance.GetFont(fontID);
        if (font != null) titleInput.textComponent.font = font;
    }

    private static void SetPlaceholder(TMP_InputField field, string text)
    {
        var ph = field.placeholder as TextMeshProUGUI;
        if (ph != null) ph.text = text;
    }
}
