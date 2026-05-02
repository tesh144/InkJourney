using UnityEngine;
using UnityEngine.UI;

public class ProfilePicDisplay : MonoBehaviour
{
    public Image imageTarget;

    private bool _subscribed;

    private void Awake()
    {
        if (imageTarget == null)
            imageTarget = GetComponent<Image>();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // Keep trying until UserProfileManager is ready (handles startup race).
        if (!_subscribed)
            TrySubscribe();
    }

    private void OnDisable()
    {
        if (_subscribed && UserProfileManager.instance != null)
            UserProfileManager.instance.OnProfilePicChanged -= HandleProfilePicChanged;

        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (UserProfileManager.instance == null) return;

        UserProfileManager.instance.OnProfilePicChanged += HandleProfilePicChanged;
        _subscribed = true;
        Apply(UserProfileManager.instance.ProfilePicID);
    }

    private void HandleProfilePicChanged(int profilePicId)
    {
        Apply(profilePicId);
    }

    private void Apply(int profilePicId)
    {
        if (imageTarget == null || UserProfileManager.instance == null)
            return;

        Sprite sprite = UserProfileManager.instance.GetProfilePicSprite(profilePicId);
        imageTarget.sprite = sprite;
        imageTarget.enabled = sprite != null;
    }
}
