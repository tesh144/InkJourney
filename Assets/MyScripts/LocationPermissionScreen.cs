using System.Collections;
using UnityEngine;
using TMPro;

public class LocationPermissionScreen : MonoBehaviour
{
    public static LocationPermissionScreen instance;
    public static bool LocationGranted { get; private set; }

    [Header("UI")]
    public GameObject screenRoot;
    public GameObject locationCardsRoot;
    public GameObject privacyCardsRoot;
    public TMP_Text   allowButtonLabel;

    // Fired when all location cards are revealed
    public System.Action onAllCardsShown;

    private int _shownCount = 1;

    private const string ReqKey         = "LocationRequested";
    private const string PrivacyDoneKey = "PrivacyOnboardingComplete";

    private void Awake()
    {
        instance = this;
        if (PlayerPrefs.GetInt(ReqKey, 0) == 1 && PlayerPrefs.GetInt(PrivacyDoneKey, 0) == 1)
            LocationGranted = true;
    }

    private void Start()
    {
#if UNITY_EDITOR
        LocationGranted = true;
        Close();
        return;
#endif
        if (LocationGranted)
        {
            Close();
            return;
        }

        // Location was previously granted but privacy selection not completed
        if (PlayerPrefs.GetInt(ReqKey, 0) == 1 && Input.location.isEnabledByUser)
            ShowPrivacyCards();
    }

    // ── Next button ───────────────────────────────────────────────────────────

    public void OnNextPressed()
    {
        if (locationCardsRoot == null) return;
        var t = locationCardsRoot.transform;
        if (_shownCount >= t.childCount) return;

        t.GetChild(_shownCount).gameObject.SetActive(true);
        _shownCount++;

        if (_shownCount >= t.childCount)
            onAllCardsShown?.Invoke();
    }

    // ── Primary button (Allow Location or Continue) ───────────────────────────

    public void OnPrimaryButtonPressed()
    {
        if (privacyCardsRoot != null && privacyCardsRoot.activeSelf)
            OnContinuePressed();
        else
            OnAllowPressed();
    }

    private void OnAllowPressed()
    {
        // Already requested but denied → send to Settings
        if (PlayerPrefs.GetInt(ReqKey, 0) == 1 && !Input.location.isEnabledByUser)
        {
            Application.OpenURL("app-settings:");
            return;
        }

        PlayerPrefs.SetInt(ReqKey, 1);
        PlayerPrefs.Save();
        StartCoroutine(RequestLocation());
    }

    public void OnContinuePressed()
    {
        PlayerPrefs.SetInt(PrivacyDoneKey, 1);
        PlayerPrefs.Save();
        Close();
        ATTManager.instance?.RequestIfNeeded();
    }

    // ── Location request ──────────────────────────────────────────────────────

    private IEnumerator RequestLocation()
    {
#if UNITY_EDITOR
        // In the editor there's no real GPS — grant immediately so the flow continues.
        ShowPrivacyCards();
        yield break;
#endif
        Input.location.Start();
        float timeout = 10f;
        while (Input.location.status == LocationServiceStatus.Initializing && timeout > 0f)
        {
            yield return new WaitForSeconds(0.5f);
            timeout -= 0.5f;
        }

        if (Input.location.isEnabledByUser)
            ShowPrivacyCards();
        // else: permission denied — screen stays, button becomes "Open Settings" on next tap
    }

    // ── Privacy cards ─────────────────────────────────────────────────────────

    private void ShowPrivacyCards()
    {
        LocationGranted = true;  // unblock GPS immediately once location is accepted
        if (locationCardsRoot != null) locationCardsRoot.SetActive(false);
        if (privacyCardsRoot  != null) privacyCardsRoot.SetActive(true);
        if (allowButtonLabel  != null) allowButtonLabel.text = "Continue";
        onAllCardsShown?.Invoke();
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    private void Close()
    {
        if (screenRoot != null)
            screenRoot.SetActive(false);
        else
            gameObject.SetActive(false);
    }
}
