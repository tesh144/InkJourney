using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LikeButton : MonoBehaviour
{
    public Button button;
    public Image likedStateImage;
    public Color likedColor = Color.red;
    public Color unlikedColor = Color.white;
    public TextMeshProUGUI likesCountText;
    public GameObject savedAnimationObject;
    public float savedAnimationAutoHideSeconds = 0f;

    private GoogleSheetsFetcher.Entry boundEntry;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(OnTapped);
    }

    public void BindEntry(GoogleSheetsFetcher.Entry entry)
    {
        boundEntry = entry;
        RefreshVisuals();
    }

    public void OnTapped()
    {
        if (boundEntry == null || GoogleSheetsFetcher.instance == null)
            return;

        bool liked = GoogleSheetsFetcher.instance.ToggleLike(boundEntry);
        if (liked)
            ShowSavedAnimation();
        RefreshVisuals();
    }

    private void ShowSavedAnimation()
    {
        if (savedAnimationObject == null)
            return;

        // Restart animation by toggling active state.
        savedAnimationObject.SetActive(false);
        savedAnimationObject.SetActive(true);

        if (savedAnimationAutoHideSeconds > 0f)
            StartCoroutine(HideSavedAnimationAfterDelay(savedAnimationAutoHideSeconds));
    }

    private IEnumerator HideSavedAnimationAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (savedAnimationObject != null)
            savedAnimationObject.SetActive(false);
    }

    public void RefreshVisuals()
    {
        if (boundEntry == null)
            return;

        if (likesCountText != null)
            likesCountText.text = CompactCountFormatter.FormatLikes(boundEntry.Likes);

        if (likedStateImage != null && GoogleSheetsFetcher.instance != null)
        {
            bool isLiked = GoogleSheetsFetcher.instance.IsLikedByCurrentUser(boundEntry);
            likedStateImage.color = isLiked ? likedColor : unlikedColor;
        }
    }
}
