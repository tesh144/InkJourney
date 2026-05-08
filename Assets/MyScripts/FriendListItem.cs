using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FriendListItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TextMeshProUGUI usernameText;
    [SerializeField] Image avatarImage;
    [SerializeField] TextMeshProUGUI statusText;       // "Online" / "Offline"
    [SerializeField] GameObject onlineIndicator;       // green dot
    [SerializeField] Button unfriendButton;
    [SerializeField] GameObject requestContainer;      // toggled on for pending requests
    [SerializeField] Button acceptButton;
    [SerializeField] Button rejectButton;
    [SerializeField] Button bodyButton;                // clicking the row opens friend library
    [SerializeField] GameObject suggestedContainer;    // shown only for Suggested state
    [SerializeField] Button addFriendButton;           // inside suggestedContainer

    [Header("Selection")]
    [SerializeField] GameObject selectedIndicator;     // highlight shown when this row is selected

    FriendData data;
    FriendsManager manager;
    FriendLibraryPanel friendLibraryPanel;   // injected by FriendsManager after instantiation
    UnfriendConfirmPanel unfriendConfirmPanel; // injected by FriendsManager after instantiation

    void Awake()
    {
        if (unfriendButton != null) unfriendButton.onClick.AddListener(OnUnfriendClicked);
        if (acceptButton   != null) acceptButton.onClick.AddListener(OnAcceptClicked);
        if (rejectButton   != null) rejectButton.onClick.AddListener(OnRejectClicked);
        if (bodyButton     != null) bodyButton.onClick.AddListener(OnBodyClicked);
        if (addFriendButton != null) addFriendButton.onClick.AddListener(OnAddFriendClicked);
    }

    public void SetFriendLibraryPanel(FriendLibraryPanel panel) => friendLibraryPanel = panel;
    public void SetUnfriendConfirmPanel(UnfriendConfirmPanel panel) => unfriendConfirmPanel = panel;

    public void Bind(FriendData friendData, FriendsManager ownerManager)
    {
        data    = friendData;
        manager = ownerManager;

        bool isPending   = data.status == FriendData.FriendStatus.PendingIncoming;
        bool isSuggested = data.status == FriendData.FriendStatus.Suggested;

        if (usernameText != null)
            usernameText.text = data.username;

        if (requestContainer   != null) requestContainer.SetActive(isPending);
        if (suggestedContainer != null) suggestedContainer.SetActive(isSuggested);

        if (unfriendButton != null)
            unfriendButton.gameObject.SetActive(!isPending && !isSuggested);

        if (statusText != null)
        {
            statusText.gameObject.SetActive(!isPending && !isSuggested);
            if (!isPending && !isSuggested)
                statusText.text = data.isOnline ? "Online" : "Offline";
        }

        if (onlineIndicator != null)
            onlineIndicator.SetActive(!isPending && !isSuggested && data.isOnline);

        if (bodyButton != null)
            bodyButton.interactable = !isPending && !isSuggested;

        LoadAvatar();
    }

    void LoadAvatar()
    {
        if (avatarImage == null || UserProfileManager.instance == null) return;

        avatarImage.sprite  = null;
        avatarImage.enabled = false;

        string userId = data.userId;
        UserProfileManager.instance.GetProfilePicIDForAuthor(userId, picId =>
        {
            if (data == null || data.userId != userId || avatarImage == null) return;
            Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(picId);
            avatarImage.sprite  = sprite;
            avatarImage.enabled = sprite != null;
        });
    }

    public void RefreshOnlineStatus(bool isOnline)
    {
        if (data == null || IsRequest) return;
        data.isOnline = isOnline;
        if (statusText != null) statusText.text = isOnline ? "Online" : "Offline";
        if (onlineIndicator != null) onlineIndicator.SetActive(isOnline);
    }

    public bool       IsRequest => data?.status == FriendData.FriendStatus.PendingIncoming;
    public string     Username  => data?.username ?? "";
    public FriendData Data      => data;

    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
    }

    void OnBodyClicked()
    {
        if (data == null || IsRequest) return;
        manager?.SelectFriend(this);
        if (friendLibraryPanel != null)
            friendLibraryPanel.Open(data, manager);
    }

    void OnAcceptClicked()    => manager?.AcceptRequest(data);
    void OnRejectClicked()    => manager?.RejectRequest(data);
    void OnAddFriendClicked() => manager?.SendAddFriendRequest(data);
    void OnUnfriendClicked()
    {
        if (unfriendConfirmPanel != null)
            unfriendConfirmPanel.Show(data, manager);
        else
            manager?.Unfriend(data);
    }
}
