using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveButton : MonoBehaviour
{
    public Button button;
    public Image savedStateImage;
    public Color savedColor   = Color.yellow;
    public Color unsavedColor = Color.white;
    public TextMeshProUGUI savesCountText;
    public GameObject savedAnimationObject;
    public float savedAnimationAutoHideSeconds = 0f;

    GoogleSheetsFetcher.Entry _entry;

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

        bool saved = GoogleSheetsFetcher.instance.ToggleSave(_entry);
        if (saved) ShowSavedAnimation();
        RefreshVisuals();
    }

    public void RefreshVisuals()
    {
        if (_entry == null) return;

        if (savesCountText != null)
            savesCountText.text = CompactCountFormatter.FormatLikes(_entry.Saves);

        if (savedStateImage != null)
        {
            bool isSaved = GoogleSheetsFetcher.instance != null &&
                           GoogleSheetsFetcher.instance.IsSavedByCurrentUser(_entry);
            savedStateImage.color = isSaved ? savedColor : unsavedColor;
        }
    }

    void ShowSavedAnimation()
    {
        if (savedAnimationObject == null) return;
        savedAnimationObject.SetActive(false);
        savedAnimationObject.SetActive(true);
        if (savedAnimationAutoHideSeconds > 0f)
            StartCoroutine(HideAfterDelay(savedAnimationAutoHideSeconds));
    }

    IEnumerator HideAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (savedAnimationObject != null) savedAnimationObject.SetActive(false);
    }
}
