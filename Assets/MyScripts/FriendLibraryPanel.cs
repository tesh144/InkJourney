using UnityEngine;

/// <summary>
/// Attach to the FriendLibrary panel. Wire up:
///   - libraryManager   → the LibraryManager component on this same panel
///   - headerFriendItem → the FriendListItem at the top of the panel (the "header" friend display)
/// </summary>
public class FriendLibraryPanel : MonoBehaviour
{
    [SerializeField] LibraryManager libraryManager;
    [SerializeField] FriendListItem headerFriendItem;

    FriendsManager friendsManager;

    public void Open(FriendData friend, FriendsManager manager)
    {
        friendsManager = manager;
        gameObject.SetActive(true);

        if (headerFriendItem != null)
            headerFriendItem.Bind(friend, manager);

        if (libraryManager != null)
            libraryManager.SetFriendMode(friend.userId);
    }

    public void ReadSelected()
    {
        libraryManager?.ReadSelected();
    }

    public void Close()
    {
        if (libraryManager != null)
            libraryManager.ClearFriendMode();

        gameObject.SetActive(false);
    }
}
