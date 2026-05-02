using System.Collections.Generic;
using UnityEngine;

public class DistanceChecker : MonoBehaviour
{
    public List<GameObject> uiObjectsToToggle;
    public float minimumRange = 50f; // metres
    [Tooltip("If true, checks all stories. If false, only checks stories owned by the current user.")]
    public bool checkAllStories = true;

    private float nextOwnedLibraryCheckTime;
    private bool cachedOwnedLibraryFull;

    private void Awake()
    {
        SetVisible(false);
    }

    private void Update()
    {
        if (GoogleSheetsFetcher.instance == null) return;

        bool ownedLibraryFull = IsOwnedLibraryFull();

        float playerLat = GPSManager.Instance.latitude;
        float playerLon = GPSManager.Instance.longitude;

        if (playerLat == 0f && playerLon == 0f)
        {
            SetVisible(!ownedLibraryFull);
            return;
        }

        bool tooClose = false;
        foreach (var entry in GoogleSheetsFetcher.instance.storiesList)
        {
            if (!checkAllStories && (UserProfileManager.instance == null || !UserProfileManager.instance.IsCurrentUser(entry.User)))
                continue;

            float latDiff    = Mathf.Abs((entry.Latitude  - playerLat) * 111320f);
            float lonDiff    = Mathf.Abs((entry.Longitude - playerLon) * (111320f * Mathf.Cos(playerLat * Mathf.Deg2Rad)));
            float distMetres = Mathf.Sqrt(latDiff * latDiff + lonDiff * lonDiff);

            if (distMetres <= minimumRange)
            {
                tooClose = true;
                break;
            }
        }

        SetVisible(!tooClose && !ownedLibraryFull);
    }

    private bool IsOwnedLibraryFull()
    {
        if (Time.unscaledTime < nextOwnedLibraryCheckTime)
            return cachedOwnedLibraryFull;

        nextOwnedLibraryCheckTime = Time.unscaledTime + 1f;

        if (LibraryManager.instance == null)
        {
            cachedOwnedLibraryFull = false;
            return cachedOwnedLibraryFull;
        }

        int maxOwned = Mathf.Max(1, LibraryManager.instance.maxOwnedStories);
        cachedOwnedLibraryFull = LocalStoryStore.LoadOwned().Count >= maxOwned;
        return cachedOwnedLibraryFull;
    }

    private void SetVisible(bool visible)
    {
        foreach (GameObject obj in uiObjectsToToggle)
            if (obj != null) obj.SetActive(visible);
    }
}
