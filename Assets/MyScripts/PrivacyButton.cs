using UnityEngine;
using UnityEngine.UI;

public class PrivacyButton : MonoBehaviour
{
    // Set in inspector: "public", "private", or "friends_only"
    public string privacyTag;
    public GameObject selectedIndicator;
    public Button button;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    private void OnEnable()
    {
        if (CreateNewStory.instance != null)
        {
            CreateNewStory.instance.RegisterPrivacyButton(this);
            SetSelected(CreateNewStory.instance.CurrentPrivacy == privacyTag);
        }
    }

    public void OnClick()
    {
        if (CreateNewStory.instance == null || string.IsNullOrWhiteSpace(privacyTag))
            return;

        if (CreateNewStory.instance.CurrentPrivacy == privacyTag)
            return;

        CreateNewStory.instance.SetPrivacy(privacyTag);
    }

    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
        if (button != null)
            button.interactable = !selected;
    }
}
