using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LikeButton : MonoBehaviour
{
    public Button button;
    public Image likedStateImage;
    public Color likedColor   = Color.red;
    public Color unlikedColor = Color.white;
    public TextMeshProUGUI likesCountText;

    GoogleSheetsFetcher.Entry _entry;

    static string PrefsKey(string storyId) => $"LikedStory_{storyId}";

    public static bool IsLikedLocally(string storyId)
        => PlayerPrefs.GetInt(PrefsKey(storyId), 0) != 0;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OnTapped);
    }

    public void BindEntry(GoogleSheetsFetcher.Entry entry)
    {
        _entry = entry;
        RefreshVisuals();
    }

    public void OnTapped()
    {
        if (_entry == null || GoogleSheetsFetcher.instance == null) return;

        bool liked = IsLikedLocally(_entry.ID);
        if (liked)
        {
            PlayerPrefs.SetInt(PrefsKey(_entry.ID), 0);
            PlayerPrefs.Save();
            GoogleSheetsFetcher.instance.RemoveLike(_entry);
        }
        else
        {
            PlayerPrefs.SetInt(PrefsKey(_entry.ID), 1);
            PlayerPrefs.Save();
            GoogleSheetsFetcher.instance.AddLike(_entry);
        }
        RefreshVisuals();
    }

    public void RefreshVisuals()
    {
        if (_entry == null) return;

        if (likesCountText != null)
            likesCountText.text = CompactCountFormatter.FormatLikes(_entry.LikesCount);

        if (likedStateImage != null)
            likedStateImage.color = IsLikedLocally(_entry.ID) ? likedColor : unlikedColor;
    }
}
