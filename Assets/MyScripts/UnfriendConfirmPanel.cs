using TMPro;
using UnityEngine;

public class UnfriendConfirmPanel : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI messageText;
    [SerializeField] string messageFormat = "Are you sure you want to unfriend {0}?";

    FriendData pendingData;
    FriendsManager manager;

    public void Show(FriendData data, FriendsManager ownerManager)
    {
        pendingData = data;
        manager     = ownerManager;

        if (messageText != null)
            messageText.text = string.Format(messageFormat, data.username);

        gameObject.SetActive(true);
    }

    // Assign to confirm (red trash) button OnClick
    public void OnConfirm()
    {
        manager?.Unfriend(pendingData);
        gameObject.SetActive(false);
    }

    // Assign to cancel (X) button OnClick
    public void OnCancel()
    {
        gameObject.SetActive(false);
    }
}
