using UnityEngine;
using UnityEngine.UI;

public class ProfilePicButton : MonoBehaviour
{
    public int profilePicID;
    public Button button;
    public Image previewImage;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (previewImage == null)
            previewImage = GetComponentInChildren<Image>(true);

        if (button != null)
            button.onClick.AddListener(OnClick);

        RefreshPreviewImage();
    }

    private void OnEnable()
    {
        RefreshPreviewImage();
    }

    public void OnClick()
    {
        if (UserProfileManager.instance == null)
            return;

        UserProfileManager.instance.SetProfilePicID(profilePicID, true);
    }

    private void RefreshPreviewImage()
    {
        if (previewImage == null || UserProfileManager.instance == null)
            return;

        Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(profilePicID);
        previewImage.sprite = sprite;
        previewImage.enabled = sprite != null;
    }
}
