using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Sirenix.OdinInspector;

public class GoogleSheetManager : MonoBehaviour
{
    public MapPointer mapPointer;

    [Header("Story Data")]
    public string id;
    public float latitude;
    public float longitude;
    public string userId;
    public string username;
    public string title;
    [TextArea(3, 10)] public string content;
    public string theme;
    public string track;
    public string font;
    public int likes;
    public int views;
    public int created;
    public int expire;
    public string photoUrl;

    [Button("Turn On Story")]
    public void TurnOnStory()
    {
        if (GetDistanceToParentCenter() > 200f)
            StartCoroutine(TurnOnStoryCoroutine());
        else
        {
            SearchLocation.instance.gameObject.SetActive(true);
            SearchLocation.instance.GetLongAndLat(mapPointer.longitude, mapPointer.latitude);
        }
    }

    public float GetDistanceToParentCenter()
    {
        RectTransform rect = transform.parent.GetComponent<RectTransform>();
        Vector3 parentCenter = rect.position + new Vector3(rect.rect.width / 2, rect.rect.height / 2, 0f);
        return Vector3.Distance(transform.position, parentCenter);
    }

    private IEnumerator TurnOnStoryCoroutine()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Debug.LogError("No Internet Connection.");
            ObjectManager.instance.warningScreen.SetActive(true);
            yield break;
        }

        ObjectManager.instance.loadingScreen.SetActive(true);

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(content))
        {
            Debug.LogError("Failed to load story data!");
            ObjectManager.instance.loadingScreen.SetActive(false);
            ObjectManager.instance.warningScreen.SetActive(true);
            yield break;
        }

        GoogleSheetsFetcher.Entry boundEntry = mapPointer != null ? mapPointer.entry : null;
        if (boundEntry == null && GoogleSheetsFetcher.instance != null)
            boundEntry = GoogleSheetsFetcher.instance.GetStoryById(id);

        string resolvedUserId   = boundEntry != null ? boundEntry.User     : userId;
        string resolvedUserName = boundEntry != null ? boundEntry.UserName : username;
        long   resolvedCreated  = boundEntry != null && boundEntry.Created > 0 ? boundEntry.Created : created;
        int    resolvedViews    = boundEntry != null ? boundEntry.Views    : views;
        int    resolvedExpire   = boundEntry != null ? (int)boundEntry.Expire : expire;

        ObjectManager.instance.storyPanel.title.text = title;
        ObjectManager.instance.storyPanel.SetContentText(content);
        ObjectManager.instance.storyPanel.SetAuthor(resolvedUserId, resolvedUserName);
        ObjectManager.instance.storyPanel.BindStoryEntry(boundEntry);
        ObjectManager.instance.storyPanel.views.text = CompactCountFormatter.FormatViews(resolvedViews);

        ObjectManager.instance.loadingScreen.SetActive(false);
        ThemeManager.instance.SetTheme(theme);
        FontManager.SetPreviewFont(boundEntry != null ? boundEntry.FontID : 0);
        StickerManager.SetPreviewSticker(boundEntry != null ? boundEntry.StickerID : 0);
        bool isFriend = mapPointer != null && mapPointer.IsFriendEntry;
        ObjectManager.instance.storyPanel.SetTypeIcon(boundEntry, isFriend);
        ObjectManager.instance.storyPanel.SetPhoto(photoUrl, id);
        ObjectManager.instance.storyPanel.gameObject.SetActive(true);

        bool isLandmark = boundEntry != null && GoogleSheetsFetcher.IsLandmark(boundEntry);
        if (ObjectManager.instance.storyPanel.dateText != null)
            ObjectManager.instance.storyPanel.dateText.text = isLandmark ? "" : StoryDateFormatter.FormatAgo(resolvedCreated);
        ObjectManager.instance.storyPanel.SetExpireDisplay(boundEntry);

        string resolvedLocation = (mapPointer != null && !string.IsNullOrEmpty(mapPointer.location))
            ? mapPointer.location
            : (boundEntry != null && !string.IsNullOrEmpty(boundEntry.cachedLocation) ? boundEntry.cachedLocation : null);

        ObjectManager.instance.storyPanel.location_expire_text.text = resolvedLocation ?? "Unknown";

        if (resolvedLocation == null && boundEntry != null && (boundEntry.Latitude != 0 || boundEntry.Longitude != 0))
            StartCoroutine(FetchAndUpdateLocation(boundEntry));
    }

    private IEnumerator FetchAndUpdateLocation(GoogleSheetsFetcher.Entry entry)
    {
        string token = MapLoader.instance?.mapboxToken ?? "";
        string url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{entry.Longitude},{entry.Latitude}.json?access_token={token}";

        using (var request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var response = JsonUtility.FromJson<MapPointer.MapboxGeocodeResponse>(request.downloadHandler.text);
            if (response?.features == null || response.features.Length == 0) yield break;

            string neighbourhood = null, city = null, adminArea = null;
            var first = response.features[0];

            if (first.place_type != null &&
                (System.Array.IndexOf(first.place_type, "neighborhood") >= 0
              || System.Array.IndexOf(first.place_type, "locality") >= 0))
                neighbourhood = first.text;

            if (first.context != null)
                foreach (var ctx in first.context)
                {
                    if (city == null && ctx.id != null &&
                        (ctx.id.StartsWith("place.") || ctx.id.StartsWith("locality.")))
                        city = ctx.text;
                    if (adminArea == null && ctx.id != null && ctx.id.StartsWith("district."))
                        adminArea = ctx.text;
                }

            if (city == null)
                foreach (var feat in response.features)
                    if (feat.place_type != null && System.Array.IndexOf(feat.place_type, "place") >= 0)
                    { city = feat.text; break; }

            string locationName = MapPointer.BuildLocation(neighbourhood, city, adminArea) ?? first.place_name;

            entry.cachedLocation = locationName;
            if (entry.pointer != null) entry.pointer.location = locationName;

            var panel = ObjectManager.instance?.storyPanel;
            if (panel != null && panel.gameObject.activeSelf && panel.BoundEntry == entry)
                panel.location_expire_text.text = locationName;
        }
    }
}
