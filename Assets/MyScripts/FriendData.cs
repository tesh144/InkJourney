public class FriendData
{
    public string userId;
    public string username;
    public int profilePicId;
    public bool isOnline;
    public FriendStatus status;
    public string requestId; // FriendRequests doc ID, only for PendingIncoming

    public enum FriendStatus
    {
        Accepted,
        PendingIncoming
    }
}
