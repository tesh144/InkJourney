using UnityEngine;

public class PrivacyDefaultManager : MonoBehaviour
{
    public static PrivacyDefaultManager instance;

    private const string PrefKey = "DefaultPrivacy";

    [Header("Public")]
    public GameObject publicOnState;
    public GameObject publicOffState;

    [Header("Private")]
    public GameObject privateOnState;
    public GameObject privateOffState;

    [Header("Friends Only")]
    public GameObject friendsOnState;
    public GameObject friendsOffState;

    public static string DefaultPrivacy { get; private set; }

    private void Awake()
    {
        instance = this;
        DefaultPrivacy = PlayerPrefs.GetString(PrefKey, "public");
        RefreshButtons();
    }

    public void SetDefault(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        DefaultPrivacy = value.Trim();
        PlayerPrefs.SetString(PrefKey, DefaultPrivacy);
        PlayerPrefs.Save();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        SetState(publicOnState,  publicOffState,  DefaultPrivacy == "public");
        SetState(privateOnState, privateOffState, DefaultPrivacy == "private");
        SetState(friendsOnState, friendsOffState, DefaultPrivacy == "friends_only");
    }

    private static void SetState(GameObject on, GameObject off, bool active)
    {
        if (on  != null) on.SetActive(active);
        if (off != null) off.SetActive(!active);
    }
}
