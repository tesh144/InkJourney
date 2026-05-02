using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A map pin showing a friend's last known location.
/// Positioned using the same GPS-to-Unity math as MapPointer.
/// Expires after expireAfterSeconds and destroys itself.
/// </summary>
public class FriendMapPointer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] Image avatarImage;
    [SerializeField] TextMeshProUGUI timeAgoText;


    [Header("Expiry")]
    [SerializeField] float expireAfterSeconds = 3600f; // 1 hour

    public string FriendUserId { get; private set; }

    float lat;
    float lon;
    long  timestamp;

    public void Bind(string userId, float latitude, float longitude, long ts)
    {
        FriendUserId = userId;
        lat          = latitude;
        lon          = longitude;
        timestamp    = ts;

        UpdatePosition();
        LoadAvatar(userId);

        // Set label immediately before coroutine starts
        if (timeAgoText != null)
            timeAgoText.text = FormatTimeAgo(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts);

        StartCoroutine(UpdateTimeLabel());
    }

    void Update()
    {
        UpdatePosition();
        if (transform.parent != null)
            transform.localEulerAngles = new Vector3(0f, 0f, -transform.parent.eulerAngles.z);
    }

    public void UpdatePosition()
    {
        if (GPSManager.Instance == null || MapLoader.instance == null) return;

        float centerLat = GPSManager.Instance.latitude;
        float centerLon = GPSManager.Instance.longitude;
        float scale     = 6f * Mathf.Pow(2f, MapLoader.instance.zoom - 14f);

        float latM = (lat - centerLat) * 111320f;
        float lonM = (lon - centerLon) * (111320f * Mathf.Cos(centerLat * Mathf.Deg2Rad));

        var rt = GetComponent<RectTransform>();
        if (rt != null)
            rt.anchoredPosition = new Vector2(lonM * scale, latM * scale);
    }

    void LoadAvatar(string userId)
    {
        if (avatarImage == null || UserProfileManager.instance == null) return;

        avatarImage.sprite  = null;
        avatarImage.enabled = false;

        UserProfileManager.instance.GetProfilePicIDForAuthor(userId, picId =>
        {
            if (avatarImage == null) return;
            Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(picId);
            avatarImage.sprite  = sprite;
            avatarImage.enabled = sprite != null;
        });
    }

    IEnumerator UpdateTimeLabel()
    {
        long expireAt = timestamp + (long)expireAfterSeconds;

        while (true)
        {
            long now     = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long elapsed = now - timestamp;

            if (now >= expireAt)
            {
                Destroy(gameObject);
                yield break;
            }

            if (timeAgoText != null)
                timeAgoText.text = FormatTimeAgo(elapsed);

            yield return new WaitForSeconds(30f);
        }
    }

    static string FormatTimeAgo(long elapsed)
    {
        if (elapsed < 60)    return "Just now";
        if (elapsed < 3600)  return $"{elapsed / 60}mins ago";
        return $"{elapsed / 3600}hrs ago";
    }
}
