using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

public class LibraryManager : MonoBehaviour
{
    public static LibraryManager instance;

    [Header("Chapter List")]
    public GameObject chapterObjectPrefab;
    public Transform listParent;
    public Transform likedListParent;

    [Header("Limits")]
    public int maxOwnedStories     = 10;
    public int maxCollectedStories = 20;

    [Header("Library Fill UI")]
    public TextMeshProUGUI ownedFillText;
    public TextMeshProUGUI likedFillText;
    public string ownedFillPrefix = "";
    public string likedFillPrefix = "";

    [Header("Preview — Top Panel")]
    public List<Image> previewIcons = new List<Image>();
    public List<GameObject> previewDefaultOutlines  = new List<GameObject>();
    public List<GameObject> previewLandmarkOutlines = new List<GameObject>();
    public Sprite iconDefault;
    public Sprite iconFriend;
    public Sprite iconLandmark;
    public TextMeshProUGUI previewTitle;
    public TextMeshProUGUI previewExpires;
    public TextMeshProUGUI previewDate;
    public TextMeshProUGUI previewLocation;
    public TextMeshProUGUI previewContent;
    public TextMeshProUGUI previewLikes;
    public TextMeshProUGUI previewViews;

    [Header("Library Map")]
    public RawImage libraryMapImage;

    [Header("Map Pin")]
    public GameObject mapPin; // Centred on the library map — assign Read button here
    public MapPointer mapPinPointer; // The MapPointer component on mapPin — assign for owner styling
    public GameObject landmarkMapPin; // Used instead of mapPin when the selected story has the landmark tag
    public MapPointer landmarkMapPinPointer;
    public GameObject menus;  // Shown when a chapter is selected, hidden otherwise
    public GameObject nothingSelectedObject; // Shown when nothing is selected, hidden once something is

    private ChapterObject selectedChapter;
    private readonly List<ChapterObject> spawnedChapters      = new List<ChapterObject>();
    private readonly List<ChapterObject> spawnedLikedChapters = new List<ChapterObject>();
    private bool _libraryMapLoaded;
    private GameObject _activeMapPin;
    private MapPointer _activeMapPinPointer;

    [Header("Map")]
    [SerializeField] [Range(1, 22)] int libraryMapZoom = 17;

    [Header("Events")]
    public UnityEvent<ChapterObject> onChapterSelected;

    [Header("Search & Filter — Owned")]
    public TMP_InputField ownedSearchInput;

    [Header("Search & Filter — Saved")]
    public TMP_InputField likedSearchInput;
    public Button filterLandmarkButton;
    public Button filterStoriesButton;
    public GameObject filterLandmarkOnIndicator;
    public GameObject filterStoriesOnIndicator;

    [Header("Instance")]
    [Tooltip("Only the main library panel should be primary. Uncheck on the FriendLibrary panel.")]
    [SerializeField] bool isPrimaryInstance = true;

    // Friend-library mode — set via SetFriendMode / ClearFriendMode
    string friendFilterUserId;

    // Search & filter state
    private string _ownedQuery     = "";
    private string _likedQuery     = "";
    private int    _likedTypeFilter = 0; // 0=all  1=landmarks only  2=stories only

    private void Awake()
    {
        if (isPrimaryInstance)
            instance = this;
    }

    private void OnEnable()
    {
        MapLoader.onStyleChanged += OnStyleChanged;
        if (UserProfileManager.instance != null)
        {
            UserProfileManager.instance.OnUsernameChanged  += OnOwnProfileChanged;
            UserProfileManager.instance.OnProfilePicChanged += OnOwnProfilePicChanged;
        }

        // Reset search state each time the panel opens
        _ownedQuery      = "";
        _likedQuery      = "";
        _likedTypeFilter = 0;
        if (ownedSearchInput != null) { ownedSearchInput.text = ""; ownedSearchInput.onValueChanged.AddListener(OnOwnedSearchChanged); }
        if (likedSearchInput != null) { likedSearchInput.text = ""; likedSearchInput.onValueChanged.AddListener(OnLikedSearchChanged); }
        RefreshFilterButtonVisuals();

        ClearMap();
        if (nothingSelectedObject != null) nothingSelectedObject.SetActive(true);
        if (menus != null) menus.SetActive(false);
        PopulateList();
        PopulateLikedList();
    }

    private void OnDisable()
    {
        MapLoader.onStyleChanged -= OnStyleChanged;
        if (UserProfileManager.instance != null)
        {
            UserProfileManager.instance.OnUsernameChanged   -= OnOwnProfileChanged;
            UserProfileManager.instance.OnProfilePicChanged -= OnOwnProfilePicChanged;
        }
        if (ownedSearchInput != null) ownedSearchInput.onValueChanged.RemoveListener(OnOwnedSearchChanged);
        if (likedSearchInput != null) likedSearchInput.onValueChanged.RemoveListener(OnLikedSearchChanged);
    }

    private void OnOwnProfileChanged(string newUsername)
    {
        foreach (var co in spawnedChapters)
        {
            if (co == null || !IsCurrentUser(co.entry)) continue;
            if (co.userNameText != null) co.userNameText.text = newUsername;
        }
    }

    private void OnOwnProfilePicChanged(int newPicId)
    {
        Sprite sprite = UserProfileManager.instance?.GetProfilePicSprite(newPicId);
        foreach (var co in spawnedChapters)
        {
            if (co == null || !IsCurrentUser(co.entry)) continue;
            if (co.profilePicImage != null)
            {
                co.profilePicImage.sprite  = sprite;
                co.profilePicImage.enabled = sprite != null;
            }
        }
    }

    private static bool IsCurrentUser(GoogleSheetsFetcher.Entry e)
    {
        if (e == null) return false;
        return UserProfileManager.instance != null
            ? UserProfileManager.instance.IsCurrentUser(e.User)
            : e.User == SystemInfo.deviceUniqueIdentifier;
    }

    private void OnStyleChanged()
    {
        if (selectedChapter != null)
            StartCoroutine(LoadLibraryMap(selectedChapter.entry.Latitude, selectedChapter.entry.Longitude));
        else
            ClearMap();
    }

    public void SetFriendMode(string userId)
    {
        friendFilterUserId = userId;
        PopulateList();
    }

    public void ClearFriendMode()
    {
        friendFilterUserId = null;
        PopulateList();
        PopulateLikedList();
    }

    public void PopulateList()
    {
        // Remember what was selected so we can restore it after the rebuild
        string previousId = selectedChapter?.entry?.ID;

        foreach (var c in spawnedChapters)
            if (c != null) Destroy(c.gameObject);
        spawnedChapters.Clear();
        selectedChapter = null;

        if (GoogleSheetsFetcher.instance == null)
        {
            RefreshFillTexts();
            return;
        }

        List<GoogleSheetsFetcher.Entry> entries;

        if (!string.IsNullOrEmpty(friendFilterUserId))
        {
            // Friend library mode — show this friend's stories sorted by newest first
            entries = GoogleSheetsFetcher.instance.storiesList
                .FindAll(e => e.User == friendFilterUserId);

            // Also include friend's landmarks from the landmarks list
            var seenIds = new HashSet<string>();
            foreach (var e in entries) seenIds.Add(e.ID);
            foreach (var e in GoogleSheetsFetcher.instance.landmarksList)
                if (e.User == friendFilterUserId && seenIds.Add(e.ID))
                    entries.Add(e);

            entries.Sort((a, b) => b.Created.CompareTo(a.Created));
        }
        else
        {
            // Normal mode — show the current user's own stories sorted by distance
            float playerLat = GPSManager.Instance != null ? GPSManager.Instance.latitude  : 0f;
            float playerLon = GPSManager.Instance != null ? GPSManager.Instance.longitude : 0f;

            float DistSq(GoogleSheetsFetcher.Entry e)
            {
                float dy = (e.Latitude  - playerLat) * 111320f;
                float dx = (e.Longitude - playerLon) * (111320f * Mathf.Cos(playerLat * Mathf.Deg2Rad));
                return dx * dx + dy * dy;
            }

            // Build owned entries from storiesList, upgrading each to its landmarksList
            // version when one exists (the landmark version carries the "landmark" tag;
            // the storiesList version does not, even though they represent the same place).
            entries = new List<GoogleSheetsFetcher.Entry>();
            var seenIds = new HashSet<string>();

            foreach (var e in GoogleSheetsFetcher.instance.storiesList)
            {
                if (!(UserProfileManager.instance != null
                          ? UserProfileManager.instance.IsCurrentUser(e.User)
                          : e.User == SystemInfo.deviceUniqueIdentifier)) continue;

                // If this story exists in the Landmarks collection, use that version
                var lmVersion = string.IsNullOrEmpty(e.Title) ? null :
                    GoogleSheetsFetcher.instance.landmarksList.Find(lm =>
                        lm != null && string.Equals(lm.Title?.Trim(), e.Title.Trim(),
                            System.StringComparison.OrdinalIgnoreCase));

                var entry = lmVersion ?? e;
                if (seenIds.Contains(entry.ID) || seenIds.Contains(e.ID)) continue;
                seenIds.Add(entry.ID);
                seenIds.Add(e.ID);
                entries.Add(entry);
            }

            // Add any user-owned landmarks not already covered by the storiesList pass
            var ownedStored = LocalStoryStore.LoadOwned();
            foreach (var e in GoogleSheetsFetcher.instance.landmarksList)
            {
                if (seenIds.Contains(e.ID)) continue;
                bool isOwned = (UserProfileManager.instance != null
                        ? UserProfileManager.instance.IsCurrentUser(e.User)
                        : e.User == SystemInfo.deviceUniqueIdentifier)
                    || ownedStored.Exists(s =>
                        !string.IsNullOrEmpty(s.Title) &&
                        string.Equals(s.Title.Trim(), e.Title?.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (!isOwned) continue;
                seenIds.Add(e.ID);
                var storedMatch = ownedStored.Find(s =>
                    string.Equals(s.Title?.Trim(), e.Title?.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (storedMatch != null) seenIds.Add(storedMatch.ID);
                entries.Add(e);
            }

            entries.Sort((a, b) => DistSq(a).CompareTo(DistSq(b)));

            // Merge locally-saved owned stories that have expired from the server
            foreach (var stored in ownedStored)
            {
                if (seenIds.Contains(stored.ID)) continue;
                seenIds.Add(stored.ID);
                entries.Add(LocalStoryStore.ToLiveEntry(stored));
            }
        }

        ChapterObject toReselect = null;
        foreach (var entry in entries)
        {
            GameObject go = Instantiate(chapterObjectPrefab, listParent);
            ChapterObject co = go.GetComponent<ChapterObject>();
            co.Initialise(entry, this);
            spawnedChapters.Add(co);

            if (entry.ID == previousId)
                toReselect = co;
        }

        if (toReselect != null)
            SelectChapter(toReselect);
        else
            ShowNothingSelected();

        ApplyOwnedFilters();
        RefreshFillTexts();
    }

    public void PopulateLikedList()
    {
        // Liked list has no meaning in friend-library mode
        if (!string.IsNullOrEmpty(friendFilterUserId)) return;

        foreach (var c in spawnedLikedChapters)
            if (c != null) Destroy(c.gameObject);
        spawnedLikedChapters.Clear();

        if (likedListParent == null || GoogleSheetsFetcher.instance == null)
        {
            RefreshFillTexts();
            return;
        }

        bool IsOwned(GoogleSheetsFetcher.Entry e) =>
            UserProfileManager.instance != null
                ? UserProfileManager.instance.IsCurrentUser(e.User)
                : e.User == SystemInfo.deviceUniqueIdentifier;

        var liked = GoogleSheetsFetcher.instance.storiesList
            .FindAll(e => GoogleSheetsFetcher.instance.IsSavedByCurrentUser(e) && !IsOwned(e));

        // Merge locally-saved collected stories that have expired from the server
        var liveCollectedIds = new HashSet<string>();
        foreach (var e in liked) liveCollectedIds.Add(e.ID);
        foreach (var stored in LocalStoryStore.LoadCollected())
            if (!liveCollectedIds.Contains(stored.ID))
                liked.Add(LocalStoryStore.ToLiveEntry(stored));

        foreach (var entry in liked)
        {
            GameObject go = Instantiate(chapterObjectPrefab, likedListParent);
            ChapterObject co = go.GetComponent<ChapterObject>();
            co.Initialise(entry, this);
            spawnedLikedChapters.Add(co);
        }

        ApplyLikedFilters();
        RefreshFillTexts();
    }

    public void SelectChapter(ChapterObject chapter)
    {
        if (selectedChapter != null) selectedChapter.SetSelected(false);
        selectedChapter = chapter;
        selectedChapter.SetSelected(true);
        onChapterSelected?.Invoke(chapter);

        void ApplySelection()
        {
            var e = chapter.entry;

            // Pick the right map pin for this entry
            bool isLandmark = GoogleSheetsFetcher.IsLandmark(e);
            bool useAlt = isLandmark && landmarkMapPin != null;
            _activeMapPin        = useAlt ? landmarkMapPin        : mapPin;
            _activeMapPinPointer = useAlt ? landmarkMapPinPointer : mapPinPointer;

            // Hide whichever pin we're not using
            var inactivePin = useAlt ? mapPin : landmarkMapPin;
            if (inactivePin != null) inactivePin.SetActive(false);

            if (previewTitle != null)   previewTitle.text   = e.Title;
            if (previewExpires != null) previewExpires.text = StoryDateFormatter.FormatActive(e.Expire);
            if (previewDate    != null) previewDate.text    = isLandmark ? "Landmark" : StoryDateFormatter.FormatAgo(e.Created);
            if (previewContent != null) previewContent.text = e.Content;
            if (previewLikes != null)   previewLikes.text = CompactCountFormatter.FormatLikes(e.Saves);
            if (previewViews != null)   previewViews.text = CompactCountFormatter.FormatViews(e.Views);
            if (previewLocation != null)
            {
                // Prefer entry.cachedLocation (set by geocoding), fall back to pointer.location
                string resolvedLocation = !string.IsNullOrEmpty(e.cachedLocation)
                    ? e.cachedLocation
                    : (e.pointer != null ? e.pointer.location : null);

                bool needsFetch = string.IsNullOrEmpty(resolvedLocation)
                               || !resolvedLocation.Contains(",");

                previewLocation.text = resolvedLocation ?? "Unknown";

                if (needsFetch && (e.Latitude != 0 || e.Longitude != 0))
                    StartCoroutine(FetchAndShowLocation(e, e.Latitude, e.Longitude));
            }

            if (menus != null) menus.SetActive(true);
            if (nothingSelectedObject != null) nothingSelectedObject.SetActive(false);

            bool hasLocation = e.Latitude != 0 || e.Longitude != 0;
            if (_activeMapPinPointer != null)
            {
                _activeMapPinPointer.BindEntry(e);
                if (_activeMapPinPointer.textbox != null)
                    _activeMapPinPointer.textbox.text = "Read Story";
            }

            if (previewDefaultOutlines != null)
                foreach (var go in previewDefaultOutlines)  { if (go != null) go.SetActive(!isLandmark); }
            if (previewLandmarkOutlines != null)
                foreach (var go in previewLandmarkOutlines) { if (go != null) go.SetActive(isLandmark); }

            if (previewIcons != null && previewIcons.Count > 0)
            {
                Sprite stickerSprite = e.StickerID >= 1 && StickerManager.instance != null
                    ? StickerManager.instance.GetSticker(e.StickerID) : null;

                Sprite icon;
                if (stickerSprite != null)
                    icon = stickerSprite;
                else if (isLandmark)
                    icon = iconLandmark;
                else if (_activeMapPinPointer != null && _activeMapPinPointer.IsFriendEntry)
                    icon = iconFriend;
                else
                    icon = iconDefault;

                foreach (var img in previewIcons)
                {
                    if (img == null) continue;
                    img.sprite  = icon;
                    img.enabled = true;
                }
            }

            if (hasLocation)
            {
                _libraryMapLoaded = false;
                StartCoroutine(PingMapPin());
                StartCoroutine(LoadLibraryMap(e.Latitude, e.Longitude));
            }
            else if (_activeMapPin != null)
                _activeMapPin.SetActive(false);
        }

        Debug.Log($"[LM] SelectChapter: '{chapter.entry?.Title}' isPrimary={isPrimaryInstance} hasLocation={chapter.entry?.Latitude != 0 || chapter.entry?.Longitude != 0}");
        if (SpriteSheetTransition.instance != null)
            SpriteSheetTransition.instance.DoTransition(ApplySelection, () => _libraryMapLoaded);
        else if (InkTransition.instance != null)
            InkTransition.instance.DoTransition(ApplySelection, () => _libraryMapLoaded);
        else
            ApplySelection();
    }

    // Assign to the "Read" pin button OnClick
    public void ReadSelected()
    {
        Debug.Log($"[LM] ReadSelected called. isPrimary={isPrimaryInstance} friendFilter='{friendFilterUserId}' selectedChapter={selectedChapter?.entry?.Title ?? "NULL"}");
        if (selectedChapter == null) { Debug.LogWarning("[LM] ReadSelected: selectedChapter is null, returning"); return; }
        var e = selectedChapter.entry;

        Debug.Log($"[LM] ReadSelected: ThemeManager={ThemeManager.instance != null}, ObjectManager={ObjectManager.instance != null}");
        ThemeManager.instance.SetTheme(e.Theme);
        FontManager.SetPreviewFont(e.FontID);
        StickerManager.SetPreviewSticker(e.StickerID);
        ObjectManager.instance.storyPanel.title.text = e.Title;
        ObjectManager.instance.storyPanel.SetContentText(e.Content);
        ObjectManager.instance.storyPanel.SetAuthor(e.User, e.UserName);
        ObjectManager.instance.storyPanel.BindStoryEntry(e);
        ObjectManager.instance.storyPanel.views.text = CompactCountFormatter.FormatViews(e.Views);
        ObjectManager.instance.storyPanel.likes.text = CompactCountFormatter.FormatLikes(e.Saves);
        bool isLandmarkEntry = GoogleSheetsFetcher.IsLandmark(e);
        string storyLocation = !string.IsNullOrEmpty(e.cachedLocation) ? e.cachedLocation
            : (e.pointer != null && !string.IsNullOrEmpty(e.pointer.location) ? e.pointer.location : null);
        ObjectManager.instance.storyPanel.location_expire_text.text = storyLocation ?? "Unknown";
        if (storyLocation == null && (e.Latitude != 0 || e.Longitude != 0))
            StartCoroutine(FetchAndUpdateStoryPanelLocation(e));
        ObjectManager.instance.storyPanel.SetExpireDisplay(e);
        if (ObjectManager.instance.storyPanel.dateText != null)
            ObjectManager.instance.storyPanel.dateText.text = isLandmarkEntry ? "Landmark" : StoryDateFormatter.FormatAgo(e.Created);
        ObjectManager.instance.storyPanel.SetTypeIcon(e, !string.IsNullOrEmpty(friendFilterUserId));
        ObjectManager.instance.storyPanel.SetPhoto(e.PhotoUrl, e.ID);
        ObjectManager.instance.storyPanel.gameObject.SetActive(true);
        Debug.Log("[LM] ReadSelected: completed, storyPanel activated");
    }

    // Assign to Edit button OnClick
    public void EditSelected()
    {
        if (selectedChapter == null) return;
        CreateNewStory.instance.LoadForEdit(selectedChapter.entry);
        ObjectManager.instance.createStoryPanel.SetActive(true);
    }

    // Assign to Delete button OnClick
    public void DeleteSelected()
    {
        if (selectedChapter == null) return;

        var entry = selectedChapter.entry;
        bool isOwner = entry.IsLocalDraft || (UserProfileManager.instance != null
            ? UserProfileManager.instance.IsCurrentUser(entry.User)
            : entry.User == SystemInfo.deviceUniqueIdentifier);

        if (isOwner)
        {
            // Own story — delete from server
            GoogleSheetsFetcher.instance.DeleteEntryFromFirestore(entry);
            spawnedChapters.Remove(selectedChapter);
        }
        else
        {
            // Saved story — unsave it, leave it on the server
            GoogleSheetsFetcher.instance.ToggleSave(entry);
            spawnedLikedChapters.Remove(selectedChapter);
        }

        Destroy(selectedChapter.gameObject);
        selectedChapter = null;

        if (mapPin         != null) mapPin.SetActive(false);
        if (landmarkMapPin != null) landmarkMapPin.SetActive(false);
        if (previewIcons != null)           foreach (var img in previewIcons)           { if (img != null) img.enabled = false; }
        if (previewDefaultOutlines != null)  foreach (var go in previewDefaultOutlines)  { if (go != null) go.SetActive(false); }
        if (previewLandmarkOutlines != null) foreach (var go in previewLandmarkOutlines) { if (go != null) go.SetActive(false); }
        if (previewTitle != null)   previewTitle.text = "";
        if (previewContent != null) previewContent.text = "";
        if (previewLikes != null)   previewLikes.text = "0";
        if (previewViews != null)   previewViews.text = "0";

        var remaining = isOwner ? spawnedChapters : spawnedLikedChapters;
        if (remaining.Count > 0)
            SelectChapter(remaining[0]);
        else
        {
            if (mapPin         != null) mapPin.SetActive(false);
            if (landmarkMapPin != null) landmarkMapPin.SetActive(false);
            if (menus          != null) menus.SetActive(false);
            if (nothingSelectedObject != null) nothingSelectedObject.SetActive(true);
            ClearMap();
        }

        RefreshFillTexts();
    }

    private void RefreshFillTexts()
    {
        int ownedMax = Mathf.Max(1, maxOwnedStories);
        int likedMax = Mathf.Max(1, maxCollectedStories);

        if (ownedFillText != null)
            ownedFillText.text = spawnedChapters.Count >= ownedMax
                ? $"{ownedFillPrefix}Full!"
                : $"{ownedFillPrefix}{spawnedChapters.Count}/{ownedMax}";

        if (likedFillText != null)
            likedFillText.text = spawnedLikedChapters.Count >= likedMax
                ? $"{likedFillPrefix}Full!"
                : $"{likedFillPrefix}{spawnedLikedChapters.Count}/{likedMax}";
    }

    // ── Selection helpers ──────────────────────────────────────────────────────

    // Clears the owned search bar and selects the first owned/friend story
    public void SelectFirstOwned()
    {
        ClearOwnedSearch();
        if (spawnedChapters.Count > 0)
            SelectChapter(spawnedChapters[0]);
        else
            ShowNothingSelected();
    }

    // Clears the saved search bar + type filter and selects the first saved story
    public void SelectFirstSaved()
    {
        ClearLikedSearch();
        if (spawnedLikedChapters.Count > 0)
            SelectChapter(spawnedLikedChapters[0]);
        else
            ShowNothingSelected();
    }

    private void ShowNothingSelected()
    {
        void Apply()
        {
            if (selectedChapter != null) selectedChapter.SetSelected(false);
            selectedChapter = null;
            if (mapPin         != null) mapPin.SetActive(false);
            if (landmarkMapPin != null) landmarkMapPin.SetActive(false);
            if (menus          != null) menus.SetActive(false);
            if (nothingSelectedObject != null) nothingSelectedObject.SetActive(true);
            ClearMap();
        }

        if (SpriteSheetTransition.instance != null)
            SpriteSheetTransition.instance.DoTransition(Apply, () => true);
        else if (InkTransition.instance != null)
            InkTransition.instance.DoTransition(Apply, () => true);
        else
            Apply();
    }

    private void ClearOwnedSearch()
    {
        _ownedQuery = "";
        if (ownedSearchInput != null) ownedSearchInput.text = "";
        ApplyOwnedFilters();
    }

    private void ClearLikedSearch()
    {
        _likedQuery      = "";
        _likedTypeFilter = 0;
        if (likedSearchInput != null) likedSearchInput.text = "";
        RefreshFilterButtonVisuals();
        ApplyLikedFilters();
    }

    // ── Search & Filter ────────────────────────────────────────────────────────

    private void OnOwnedSearchChanged(string query)
    {
        _ownedQuery = query;
        ApplyOwnedFilters();
    }

    private void OnLikedSearchChanged(string query)
    {
        _likedQuery = query;
        ApplyLikedFilters();
    }

    // Assign to Landmark filter button OnClick
    public void ToggleFilterLandmark()
    {
        _likedTypeFilter = _likedTypeFilter == 1 ? 0 : 1;
        RefreshFilterButtonVisuals();
        ApplyLikedFilters();
    }

    // Assign to Stories filter button OnClick
    public void ToggleFilterStories()
    {
        _likedTypeFilter = _likedTypeFilter == 2 ? 0 : 2;
        RefreshFilterButtonVisuals();
        ApplyLikedFilters();
    }

    private void RefreshFilterButtonVisuals()
    {
        if (filterLandmarkOnIndicator != null) filterLandmarkOnIndicator.SetActive(_likedTypeFilter == 1);
        if (filterStoriesOnIndicator  != null) filterStoriesOnIndicator.SetActive(_likedTypeFilter == 2);
    }

    private void ApplyOwnedFilters()
    {
        foreach (var co in spawnedChapters)
        {
            if (co == null) continue;
            co.gameObject.SetActive(MatchesSearch(co.entry, _ownedQuery));
        }
    }

    private void ApplyLikedFilters()
    {
        foreach (var co in spawnedLikedChapters)
        {
            if (co == null) continue;
            bool typeMatch = _likedTypeFilter == 0
                || (_likedTypeFilter == 1 &&  GoogleSheetsFetcher.IsLandmark(co.entry))
                || (_likedTypeFilter == 2 && !GoogleSheetsFetcher.IsLandmark(co.entry));
            co.gameObject.SetActive(typeMatch && MatchesSearch(co.entry, _likedQuery));
        }
    }

    private static bool MatchesSearch(GoogleSheetsFetcher.Entry e, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (e == null) return true;

        if (!string.IsNullOrEmpty(e.Title) &&
            e.Title.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrEmpty(e.UserName) &&
            e.UserName.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string loc = !string.IsNullOrEmpty(e.cachedLocation) ? e.cachedLocation
                   : e.pointer != null ? e.pointer.location : null;
        if (!string.IsNullOrEmpty(loc) &&
            loc.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (e.Tags != null)
            foreach (var tag in e.Tags)
            {
                string display = TagManager.instance != null
                    ? TagManager.instance.GetDisplayName(tag) : tag;
                if (!string.IsNullOrEmpty(display) &&
                    display.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

        return false;
    }

    // ── Map ────────────────────────────────────────────────────────────────────

    private IEnumerator PingMapPin()
    {
        if (_activeMapPin == null) yield break;
        _activeMapPin.SetActive(false);
        yield return null;
        _activeMapPin.SetActive(true);
    }

    private IEnumerator FetchAndShowLocation(GoogleSheetsFetcher.Entry entry, float lat, float lon)
    {
        string token = MapLoader.instance?.mapboxToken ?? "";
        string url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon},{lat}.json?access_token={token}";

        using (var request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[LibraryManager] Location fetch failed: {request.error}");
                yield break;
            }

            var response = JsonUtility.FromJson<MapPointer.MapboxGeocodeResponse>(request.downloadHandler.text);
            if (response?.features == null || response.features.Length == 0)
            {
                Debug.LogWarning("[LibraryManager] Location fetch: empty response");
                yield break;
            }

            string neighbourhood = null, city = null, adminArea = null;
            var first = response.features[0];

            if (first.place_type != null &&
                (System.Array.IndexOf(first.place_type, "neighborhood") >= 0
              || System.Array.IndexOf(first.place_type, "locality") >= 0))
                neighbourhood = first.text;

            if (first.context != null)
                foreach (var ctx in first.context)
                {
                    if (city == null && ctx.id != null &&
                        (ctx.id.StartsWith("place.") || ctx.id.StartsWith("locality.")))
                        city = ctx.text;
                    if (adminArea == null && ctx.id != null && ctx.id.StartsWith("district."))
                        adminArea = ctx.text;
                }

            if (city == null)
                foreach (var feat in response.features)
                    if (feat.place_type != null && System.Array.IndexOf(feat.place_type, "place") >= 0)
                    { city = feat.text; break; }

            string locationName = MapPointer.BuildLocation(neighbourhood, city, adminArea) ?? first.place_name;

            // Cache on the entry itself (survives pointer being null) and on the pointer if present
            entry.cachedLocation = locationName;
            if (entry.pointer != null)
                entry.pointer.location = locationName;

            // Only update the UI if this entry is still selected
            if (selectedChapter != null && selectedChapter.entry == entry && previewLocation != null)
                previewLocation.text = locationName;
        }
    }

    private IEnumerator FetchAndUpdateStoryPanelLocation(GoogleSheetsFetcher.Entry entry)
    {
        yield return StartCoroutine(FetchAndShowLocation(entry, entry.Latitude, entry.Longitude));

        var panel = ObjectManager.instance?.storyPanel;
        if (panel != null && panel.gameObject.activeSelf && panel.BoundEntry == entry
            && !string.IsNullOrEmpty(entry.cachedLocation))
            panel.location_expire_text.text = entry.cachedLocation;
    }

    private void ClearMap()
    {
        if (libraryMapImage == null) return;
        libraryMapImage.texture = null;
        libraryMapImage.color = MapLoader.instance != null
            ? MapLoader.instance.GetCurrentBackgroundColor()
            : Color.white;
    }

    private IEnumerator LoadLibraryMap(float lat, float lon)
    {
        string styleId = (MapLoader.instance != null && !string.IsNullOrEmpty(MapLoader.instance.mapStyle))
            ? MapLoader.instance.mapStyle
            : "mapbox/dark-v11";
        string token = MapLoader.instance?.mapboxToken ?? "";
        string url = $"https://api.mapbox.com/styles/v1/{styleId}/static/{lon},{lat},{libraryMapZoom},0/640x640@2x?access_token={token}";

        yield return MapboxImageCache.Fetch(url, tex =>
        {
            if (libraryMapImage != null)
            {
                libraryMapImage.color   = Color.white;
                libraryMapImage.texture = tex;
            }
            _libraryMapLoaded = true;
        });
    }

}
