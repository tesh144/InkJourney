using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Firebase;
using Firebase.Firestore;
using Firebase.Extensions;

public class UserProfileManager : MonoBehaviour
{
    public static UserProfileManager instance;

    [Header("Events")]
    public UnityEvent onFirstLaunchProfileOpened;

    [Header("Profile UI")]
    public TMP_InputField usernameInputField;
    public TextMeshProUGUI usernamePlaceholder;
    public TextMeshProUGUI profileTitleText;
    public GameObject profilePanel;
    public GameObject selectProfileWindow;
    public GameObject closeButton;
    public GameObject profileDoneButton;
    public List<InterestTagButton> interestButtons = new List<InterestTagButton>();
    public List<Sprite> profilePics = new List<Sprite>();

    [Header("Profile Pic Displays")]
    [Tooltip("All Image components that should always show the current user's profile pic")]
    public List<Image> ownProfilePicImages = new List<Image>();

    private const string UserIdKey = "UserProfile.UserId";
    private const string UsernameKey = "UserProfile.Username";
    private const string InterestsKey = "UserProfile.Interests";
    private const string ProfilePicIdKey = "UserProfile.ProfilePicID";
    private const string UserProfilesCollection = "UserProfiles";

    private string _userId;
    private string _username;
    private int _profilePicID = 0;
    private HashSet<string> _selectedInterests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> authorProfilePicCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private Color defaultInputTextColor;
    private Coroutine invalidFeedbackCoroutine;
    private FirebaseFirestore db;
    private bool firebaseReady;

    public event Action<string> OnUsernameChanged;
    public event Action<int> OnProfilePicChanged;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (interestButtons == null || interestButtons.Count == 0)
            interestButtons = new List<InterestTagButton>(GetComponentsInChildren<InterestTagButton>(true));

        LoadUsername();
        LoadInterests();
        LoadProfilePicId();
        EnsureUserId();
        InitializeFirebaseForProfiles();
    }

    private void OnEnable()
    {
        // Update interest count display whenever the profile panel becomes visible
        TagManager.instance?.UpdateInterestCount(_selectedInterests.Count);
    }

    private void Start()
    {
        if (usernameInputField != null)
            defaultInputTextColor = usernameInputField.textComponent.color;

        PopulateUsernameInput();
        TagManager.instance?.UpdateInterestCount(_selectedInterests.Count);
        OnProfilePicChanged?.Invoke(ProfilePicID);
        RefreshOwnProfilePicImages();

        if (!HasUsername)
        {
            OpenProfilePanel(true);
            onFirstLaunchProfileOpened?.Invoke();
        }
    }

    public string UserId
    {
        get
        {
            if (string.IsNullOrEmpty(_userId))
                _userId = LoadOrCreateUserId();
            return _userId;
        }
    }

    public string Username
    {
        get
        {
            if (_username == null)
                LoadUsername();
            return _username;
        }
    }

    public int ProfilePicID => _profilePicID;

    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);

    public string DisplayName => HasUsername ? Username : "USERNAME";

    private void EnsureUserId()
    {
        if (string.IsNullOrEmpty(_userId))
            _userId = LoadOrCreateUserId();
    }

    private string LoadOrCreateUserId()
    {
        string id = PlayerPrefs.GetString(UserIdKey, "");
        if (!string.IsNullOrEmpty(id))
            return id;

        string rawDeviceId = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(rawDeviceId))
            rawDeviceId = Guid.NewGuid().ToString("N");

        id = Sha256Hash(rawDeviceId);
        PlayerPrefs.SetString(UserIdKey, id);
        PlayerPrefs.Save();
        return id;
    }

    private static string Sha256Hash(string input)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder();
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    private void LoadUsername()
    {
        _username = PlayerPrefs.GetString(UsernameKey, "").Trim();
    }

    private void LoadProfilePicId()
    {
        _profilePicID = Mathf.Max(0, PlayerPrefs.GetInt(ProfilePicIdKey, 0));
    }

    private void SaveProfilePicId()
    {
        PlayerPrefs.SetInt(ProfilePicIdKey, _profilePicID);
        PlayerPrefs.Save();
    }

    private void InitializeFirebaseForProfiles()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
                return;

            db = FirebaseFirestore.DefaultInstance;
            firebaseReady = true;
            SyncProfileToFirebase();
            PullProfilePicIdFromFirebase();
            StartCoroutine(PeriodicPresenceSync());
        });
    }

    private void PullProfilePicIdFromFirebase()
    {
        if (!firebaseReady || db == null || string.IsNullOrWhiteSpace(UserId))
            return;

        db.Collection(UserProfilesCollection).Document(UserId).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || !task.Result.Exists)
                return;

            Dictionary<string, object> data = task.Result.ToDictionary();
            if (!data.TryGetValue("profilePicID", out object value) || value == null)
                return;

            int remoteId = 0;
            if (value is long l)
                remoteId = (int)l;
            else if (value is int i)
                remoteId = i;
            else
                int.TryParse(value.ToString(), out remoteId);

            SetProfilePicID(remoteId, false);
        });
    }

    private void SyncProfileToFirebase()
    {
        if (!firebaseReady || db == null || string.IsNullOrWhiteSpace(UserId))
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var data = new Dictionary<string, object>
        {
            { "UserId",      UserId },
            { "UserName",    Username ?? string.Empty },
            { "profilePicID", ProfilePicID },
            { "LastUpdated", now },
            { "lastSeen",    now },
        };

        // Append last known location if GPS is available
        if (GPSManager.Instance != null
            && (GPSManager.Instance.latitude != 0f || GPSManager.Instance.longitude != 0f))
        {
            data["lastLocation"] = new Dictionary<string, object>
            {
                { "lat",       GPSManager.Instance.latitude },
                { "lon",       GPSManager.Instance.longitude },
                { "timestamp", now }
            };
        }

        // MergeAll so fields we don't own (e.g. friendCode) are never overwritten
        db.Collection(UserProfilesCollection).Document(UserId)
            .SetAsync(data, SetOptions.MergeAll)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                    Debug.LogWarning($"[UserProfileManager] Failed to sync user profile: {task.Exception}");
            });
    }

    private IEnumerator PeriodicPresenceSync()
    {
        var wait = new WaitForSeconds(60f);
        while (true)
        {
            yield return wait;
            SyncProfileToFirebase();
        }
    }

    public Sprite GetProfilePicSprite(int profilePicId)
    {
        if (profilePics == null || profilePics.Count == 0)
            return null;

        int clamped = Mathf.Clamp(profilePicId, 0, profilePics.Count - 1);
        return profilePics[clamped];
    }

    public void SetProfilePicID(int profilePicId, bool syncRemote)
    {
        int normalized = Mathf.Max(0, profilePicId);
        if (profilePics != null && profilePics.Count > 0)
            normalized = Mathf.Clamp(normalized, 0, profilePics.Count - 1);

        _profilePicID = normalized;
        authorProfilePicCache[UserId] = _profilePicID;

        SaveProfilePicId();
        OnProfilePicChanged?.Invoke(_profilePicID);
        RefreshOwnProfilePicImages();
        RefreshOpenStoryPanelDisplay();

        if (selectProfileWindow != null)
            selectProfileWindow.SetActive(false);

        if (syncRemote)
            SyncProfileToFirebase();
    }

    public void GetProfilePicIDForAuthor(string authorId, Action<int> onResolved)
    {
        if (onResolved == null)
            return;

        if (string.IsNullOrWhiteSpace(authorId))
        {
            onResolved(0);
            return;
        }

        string key = authorId.Trim();
        if (IsCurrentUser(key))
        {
            onResolved(ProfilePicID);
            return;
        }

        if (authorProfilePicCache.TryGetValue(key, out int cachedId))
        {
            onResolved(cachedId);
            return;
        }

        if (!firebaseReady || db == null)
        {
            onResolved(0);
            return;
        }

        db.Collection(UserProfilesCollection).Document(key).GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            int resolved = 0;
            if (!task.IsFaulted && !task.IsCanceled && task.Result.Exists)
            {
                Dictionary<string, object> data = task.Result.ToDictionary();
                if (data.TryGetValue("profilePicID", out object value) && value != null)
                {
                    if (value is long l)
                        resolved = (int)l;
                    else if (value is int i)
                        resolved = i;
                    else
                        int.TryParse(value.ToString(), out resolved);
                }
            }

            if (profilePics != null && profilePics.Count > 0)
                resolved = Mathf.Clamp(resolved, 0, profilePics.Count - 1);
            else
                resolved = Mathf.Max(0, resolved);

            authorProfilePicCache[key] = resolved;
            onResolved(resolved);
        });
    }

    private void LoadInterests()
    {
        _selectedInterests.Clear();

        string saved = PlayerPrefs.GetString(InterestsKey, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            foreach (string tag in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = tag.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    _selectedInterests.Add(tag.Trim());
            }
        }

        TagManager.instance?.UpdateInterestCount(_selectedInterests.Count);
        RefreshInterestButtons();
        UpdateProfileDoneButton();
    }

    private void SaveInterests()
    {
        PlayerPrefs.SetString(InterestsKey, string.Join(",", _selectedInterests));
        PlayerPrefs.Save();
    }

    public List<string> SelectedInterests => new List<string>(_selectedInterests);

    public bool HasInterest(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return false;
        string canonical = TagManager.instance != null ? TagManager.instance.GetCanonicalTagId(tagId.Trim()) : tagId.Trim();
        return _selectedInterests.Contains(canonical);
    }

    public bool CanSelectInterest(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return false;

        if (HasInterest(tagId))
            return true;

        return _selectedInterests.Count < (TagManager.instance != null ? TagManager.instance.maxInterests : 5);
    }

    public bool SelectInterest(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return false;

        tagId = TagManager.instance != null ? TagManager.instance.GetCanonicalTagId(tagId.Trim()) : tagId.Trim();
        if (string.IsNullOrWhiteSpace(tagId))
            return false;

        if (_selectedInterests.Contains(tagId))
            return false;

        if (_selectedInterests.Count >= (TagManager.instance != null ? TagManager.instance.maxInterests : 5))
            return false;

        _selectedInterests.Add(tagId);
        SaveInterests();
        TagManager.instance?.UpdateInterestCount(_selectedInterests.Count);
        RefreshInterestButtons();
        UpdateProfileDoneButton();
        return true;
    }

    public bool DeselectInterest(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return false;

        tagId = TagManager.instance != null ? TagManager.instance.GetCanonicalTagId(tagId.Trim()) : tagId.Trim();
        if (!_selectedInterests.Remove(tagId))
            return false;

        SaveInterests();
        TagManager.instance?.UpdateInterestCount(_selectedInterests.Count);
        RefreshInterestButtons();
        UpdateProfileDoneButton();
        return true;
    }

    public void RefreshInterestButtons()
    {
        if (interestButtons == null)
            return;

        foreach (InterestTagButton button in interestButtons)
        {
            if (button == null)
                continue;
            button.SetSelected(HasInterest(button.tagId));
        }
    }

    private void UpdateProfileDoneButton()
    {
        if (profileDoneButton == null)
            return;

        bool active = HasUsername && _selectedInterests.Count >= 3;
        profileDoneButton.SetActive(active);
    }

    public void SetUsername(string username)
    {
        string trimmed = username?.Trim() ?? string.Empty;
        if (trimmed == Username) return;

        if (!IsUsernameValid(trimmed))
        {
            if (invalidFeedbackCoroutine != null)
                StopCoroutine(invalidFeedbackCoroutine);
            invalidFeedbackCoroutine = StartCoroutine(ShowInvalidUsernameFeedback());
            return;
        }

        _username = trimmed;
        PlayerPrefs.SetString(UsernameKey, _username);
        PlayerPrefs.Save();
        OnUsernameChanged?.Invoke(_username);
        SyncProfileToFirebase();
        PopulateUsernameInput();
        RefreshOpenStoryPanelDisplay();
        UpdateCloseButtonState();
        UpdateProfileDoneButton();
    }

    public void SaveUsername()
    {
        if (usernameInputField == null)
        {
            Debug.LogWarning("[UserProfileManager] SaveUsername failed: usernameInputField is not assigned");
            return;
        }
        SetUsername(usernameInputField.text);
    }

    private bool IsUsernameValid(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && username.Length >= 4;
    }

    private IEnumerator ShowInvalidUsernameFeedback()
    {
        if (usernameInputField == null) yield break;

        string originalText = usernameInputField.text;
        Color originalColor = usernameInputField.textComponent.color;

        usernameInputField.text = "Invalid";
        usernameInputField.textComponent.color = Color.red;
        usernameInputField.ActivateInputField();

        yield return new WaitForSeconds(2f);

        usernameInputField.textComponent.color = defaultInputTextColor;
        PopulateUsernameInput();

        invalidFeedbackCoroutine = null;
    }

    public void CloseProfilePanel()
    {
        OpenProfilePanel(false);
    }

    public void DeleteUserProfileAndRestart()
    {
        string userIdToDelete = UserId;
        string usernameToDelete = Username;

        if (GoogleSheetsFetcher.instance != null)
        {
            GoogleSheetsFetcher.instance.DeleteEntriesByOwner(userIdToDelete, usernameToDelete);
        }

        PlayerPrefs.DeleteKey(UserIdKey);
        PlayerPrefs.DeleteKey(UsernameKey);
        PlayerPrefs.DeleteKey(ProfilePicIdKey);
        PlayerPrefs.DeleteKey("UserProfile.FriendCode");
        PlayerPrefs.DeleteKey(InterestsKey);
        PlayerPrefs.DeleteKey("ReadStories");
        PlayerPrefs.Save();
        LocalStoryStore.WipeAll();

        // Best-effort Firestore cleanup: clear friend code so no new requests can be sent,
        // and delete the friends subcollection. Stale entries in other users' lists are
        // cleaned up automatically when they next load their friends list.
        if (firebaseReady && db != null && !string.IsNullOrEmpty(userIdToDelete))
        {
            db.Collection(UserProfilesCollection).Document(userIdToDelete)
                .UpdateAsync(new Dictionary<string, object> { { "friendCode", "" } });

            db.Collection(UserProfilesCollection).Document(userIdToDelete)
                .Collection("friends").GetSnapshotAsync().ContinueWithOnMainThread(task =>
                {
                    if (task.IsFaulted || task.IsCanceled) return;
                    foreach (var doc in task.Result.Documents)
                        doc.Reference.DeleteAsync();
                });
        }

        _userId = null;
        _username = null;

        StartCoroutine(RestartAppCoroutine());
    }

    private IEnumerator RestartAppCoroutine()
    {
        yield return null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public bool IsCurrentUser(string authorId)
    {
        if (string.IsNullOrWhiteSpace(authorId))
            return false;

        string normalizedAuthorId = authorId.Trim();
        string normalizedUserId = UserId?.Trim();
        string normalizedDeviceId = SystemInfo.deviceUniqueIdentifier?.Trim();

        if (!string.IsNullOrEmpty(normalizedUserId) && normalizedAuthorId.Equals(normalizedUserId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Legacy compatibility only: old stories may have stored raw device id in User.
        // New stories should always store hashed UserId, not device id.
        if (!string.IsNullOrEmpty(normalizedDeviceId) && normalizedAuthorId.Equals(normalizedDeviceId, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public string GetStoryAuthorDisplayName(string authorId, string authorName)
    {
        string normalizedAuthorName = authorName?.Trim();
        string normalizedAuthorId = authorId?.Trim();

        bool isCurrent = IsCurrentUser(normalizedAuthorId);

        if (isCurrent)
        {
            if (HasUsername)
                return Username;

            if (!string.IsNullOrWhiteSpace(normalizedAuthorName) &&
                !normalizedAuthorName.Equals("You", StringComparison.OrdinalIgnoreCase))
                return normalizedAuthorName;

            return DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(normalizedAuthorName))
        {
            if (normalizedAuthorName.Equals("You", StringComparison.OrdinalIgnoreCase) && HasUsername)
                return Username;

            return normalizedAuthorName;
        }

        return "Anonymous";
    }

    public void PopulateUsernameInput()
    {
        if (usernameInputField == null)
        {
            Debug.LogWarning("[UserProfileManager] PopulateUsernameInput failed: usernameInputField is not assigned");
            return;
        }

        usernameInputField.onEndEdit.RemoveListener(OnUsernameInputEndEdit);
        usernameInputField.textComponent.color = defaultInputTextColor;
        usernameInputField.text = HasUsername ? Username : string.Empty;

        if (usernamePlaceholder != null)
            usernamePlaceholder.text = HasUsername ? Username : "Enter Name";

        usernameInputField.onEndEdit.AddListener(OnUsernameInputEndEdit);
        UpdateProfileDoneButton();
    }

    private void OnUsernameInputEndEdit(string value)
    {
        SetUsername(value);
    }

    public void OpenProfilePanel(bool open)
    {
        if (profilePanel == null)
        {
            Debug.LogWarning("[UserProfileManager] profilePanel is not assigned");
            return;
        }

        profilePanel.SetActive(open);

        if (open)
        {
            if (profileTitleText != null)
                profileTitleText.text = HasUsername ? "User Profile" : "Start Your Journey";
            PopulateUsernameInput();
        }
        UpdateCloseButtonState();
    }

    private void UpdateCloseButtonState()
    {
        if (closeButton == null)
        {
            Debug.LogWarning("[UserProfileManager] closeButton is not assigned");
            return;
        }

        closeButton.SetActive(HasUsername);
    }

    private void RefreshOwnProfilePicImages()
    {
        if (ownProfilePicImages == null) return;
        Sprite sprite = GetProfilePicSprite(_profilePicID);
        foreach (var img in ownProfilePicImages)
        {
            if (img == null) continue;
            img.sprite  = sprite;
            img.enabled = sprite != null;
        }
    }

    private void RefreshOpenStoryPanelDisplay()
    {
        if (ObjectManager.instance == null || ObjectManager.instance.storyPanel == null)
            return;

        ObjectManager.instance.storyPanel.RefreshAuthorText();
        ObjectManager.instance.storyPanel.RefreshAuthorProfilePic();
    }

    public string GetPublicUserKey() => UserId;
}
