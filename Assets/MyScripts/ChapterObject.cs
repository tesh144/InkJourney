using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChapterObject : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI userNameText;
    public TextMeshProUGUI dateText;
    public TextMeshProUGUI distanceText;
    public TextMeshProUGUI likesText;
    public TextMeshProUGUI viewsText;
    public Image profilePicImage;
    public GameObject selectedIndicator;
    public Color defaultTitleColor   = Color.white;
    public Color selectedColor       = Color.cyan;
    public Color friendSelectedColor = Color.yellow;
    public Color defaultDateColor    = Color.white;
    public Color selectedDateColor   = Color.white;

    [Header("Friend Styling")]
    [Tooltip("Background image whose sprite swaps when the author is a friend")]
    public Image backgroundImage;
    public Sprite defaultBackgroundSprite;
    public Sprite friendBackgroundSprite;
    public Color defaultAuthorColor   = Color.white;
    public Color friendAuthorColor    = new Color(1f, 0.85f, 0.2f, 1f);
    public Color defaultDistanceColor = Color.white;
    public Color friendDistanceColor  = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("Landmark Styling")]
    public Sprite landmarkBackgroundSprite;
    public Sprite landmarkProfileSprite;
    public Color  landmarkSelectedColor  = Color.white;
    public Color  landmarkAuthorColor    = Color.white;
    public Color  landmarkDistanceColor  = Color.white;

    [HideInInspector] public GoogleSheetsFetcher.Entry entry;

    private LibraryManager libraryManager;

    private void OnEnable()
    {
        if (entry != null && !string.IsNullOrEmpty(entry.ID)
            && GoogleSheetsFetcher.instance != null
            && GoogleSheetsFetcher.instance.GetStoryById(entry.ID) == null)
        {
            Destroy(gameObject);
            return;
        }

        GPSManager.OnPositionSampled += RefreshDistance;
    }

    private void OnDisable()
    {
        GPSManager.OnPositionSampled -= RefreshDistance;
    }

    private void RefreshDistance(float lat, float lon)
    {
        if (distanceText == null || entry == null) return;
        float dy = (entry.Latitude  - lat) * 111320f;
        float dx = (entry.Longitude - lon) * (111320f * Mathf.Cos(entry.Latitude * Mathf.Deg2Rad));
        int metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
        distanceText.text = metres > 999 ? $"{Mathf.RoundToInt(metres / 1000f)}k" : $"{metres}m";
    }

    public void Initialise(GoogleSheetsFetcher.Entry e, LibraryManager manager)
    {
        entry = e;
        libraryManager = manager;
        if (titleText != null)
        {
            titleText.text = e.Title;
            var font = FontManager.instance?.GetFont(e.FontID);
            if (font != null) titleText.font = font;
        }
        if (userNameText != null) userNameText.text = string.IsNullOrWhiteSpace(e.UserName) ? "unknown" : e.UserName;

        if (dateText != null)
            dateText.text = GoogleSheetsFetcher.IsLandmark(e) ? "Landmark" : StoryDateFormatter.FormatAgo(e.Created);

        if (distanceText != null)
        {
            if (GPSManager.Instance != null)
                RefreshDistance(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
            else
                distanceText.text = "--";
        }

        if (likesText != null)
            likesText.text = CompactCountFormatter.FormatLikes(e.Saves);

        if (viewsText != null)
            viewsText.text = CompactCountFormatter.FormatViews(e.Views);

        bool isFriend   = FriendsManager.IsFriend(e.User);
        bool isLandmark = GoogleSheetsFetcher.IsLandmark(e);

        if (profilePicImage != null)
        {
            if (isLandmark && landmarkProfileSprite != null)
            {
                profilePicImage.sprite  = landmarkProfileSprite;
                profilePicImage.enabled = true;
            }
            else
            {
                profilePicImage.sprite  = null;
                profilePicImage.enabled = false;

                if (UserProfileManager.instance != null)
                {
                    string ownerId = e.User;
                    UserProfileManager.instance.GetProfilePicIDForAuthor(ownerId, profilePicId =>
                    {
                        if (entry != e || profilePicImage == null || UserProfileManager.instance == null)
                            return;

                        Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(profilePicId);
                        profilePicImage.sprite  = sprite;
                        profilePicImage.enabled = sprite != null;
                    });
                }
            }
        }

        if (backgroundImage != null)
        {
            if (isLandmark && landmarkBackgroundSprite != null)
                backgroundImage.sprite = landmarkBackgroundSprite;
            else if (isFriend && friendBackgroundSprite != null)
                backgroundImage.sprite = friendBackgroundSprite;
            else if (defaultBackgroundSprite != null)
                backgroundImage.sprite = defaultBackgroundSprite;
        }

        if (userNameText != null)
            userNameText.color = isLandmark ? landmarkAuthorColor : isFriend ? friendAuthorColor : defaultAuthorColor;

        if (distanceText != null)
            distanceText.color = isLandmark ? landmarkDistanceColor : isFriend ? friendDistanceColor : defaultDistanceColor;

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (titleText != null)
        {
            bool isLandmark = entry != null && GoogleSheetsFetcher.IsLandmark(entry);
            bool isFriend   = entry != null && FriendsManager.IsFriend(entry.User);
            titleText.color = selected
                ? (isLandmark ? landmarkSelectedColor : isFriend ? friendSelectedColor : selectedColor)
                : defaultTitleColor;
        }
        if (dateText != null)
            dateText.color = selected ? selectedDateColor : defaultDateColor;
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
    }

    // Assign to button OnClick
    public void Select()
    {
        libraryManager.SelectChapter(this);
    }
}
