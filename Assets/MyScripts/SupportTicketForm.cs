using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Firebase.Firestore;

public class SupportTicketForm : MonoBehaviour
{
    public enum TicketType { ReportProblem, RequestSupport }

    public static SupportTicketForm instance;

    [Header("Panel")]
    public GameObject panelRoot;
    public TMP_Text   formTitle;

    [Header("Fields")]
    public TMP_InputField titleField;
    public TMP_InputField emailField;
    public TMP_InputField nameField;
    public TMP_InputField bodyField;

    [Header("Footer")]
    public GameObject    sendButton;
    public TMP_Text      validationText;

    private TicketType _type;

    private void Awake() => instance = this;

    // ── Public API ─────────────────────────────────────────────────────────

    public void OpenReportProblem()  => Open(TicketType.ReportProblem);
    public void OpenRequestSupport() => Open(TicketType.RequestSupport);

    public void Open(TicketType type)
    {
        _type = type;
        formTitle.text = type == TicketType.ReportProblem ? "Report a Problem" : "Request Support";

        // Pre-fill name if the user has a profile
        if (nameField != null && UserProfileManager.instance != null
            && UserProfileManager.instance.HasUsername)
            nameField.text = UserProfileManager.instance.Username;

        titleField.text = "";
        emailField.text = "";
        bodyField.text  = "";

        panelRoot.SetActive(true);
        RefreshValidation();
    }

    public void OnCancelPressed()
    {
        panelRoot.SetActive(false);
    }

    public void OpenBodyEditor()
    {
        NativeTextEditor.Show(
            titleField, bodyField,
            placeholder: "Describe the issue…",
            onComplete: (t, b) =>
            {
                titleField.text = t;
                bodyField.text  = b;
                RefreshValidation();
            }
        );
    }

    public void OnSendPressed()
    {
        if (!Validate(out string reason))
        {
            if (validationText != null) validationText.text = reason;
            return;
        }
        StartCoroutine(SubmitTicket());
    }

    // ── Validation ─────────────────────────────────────────────────────────

    private bool Validate(out string reason)
    {
        if (string.IsNullOrWhiteSpace(titleField.text))
            { reason = "Please add a title."; return false; }
        if (string.IsNullOrWhiteSpace(emailField.text) || !emailField.text.Contains("@"))
            { reason = "Please enter a valid email address."; return false; }
        if (string.IsNullOrWhiteSpace(bodyField.text))
            { reason = "Please describe the issue."; return false; }
        reason = ""; return true;
    }

    public void RefreshValidation()
    {
        bool ok = Validate(out string reason);
        if (sendButton != null) sendButton.SetActive(ok);
        if (validationText != null) validationText.text = ok ? "" : reason;
    }

    // ── Submit ─────────────────────────────────────────────────────────────

    private IEnumerator SubmitTicket()
    {
        if (sendButton != null) sendButton.SetActive(false);

        string city = "";
        if (GPSManager.Instance != null)
        {
            yield return StartCoroutine(FetchCity(
                GPSManager.Instance.latitude,
                GPSManager.Instance.longitude,
                result => city = result));
        }

        string userId   = UserProfileManager.instance != null ? UserProfileManager.instance.UserId   : SystemInfo.deviceUniqueIdentifier;
        string username = UserProfileManager.instance != null && UserProfileManager.instance.HasUsername
            ? UserProfileManager.instance.Username : "";

        var data = new Dictionary<string, object>
        {
            { "type",      _type == TicketType.ReportProblem ? "report_problem" : "request_support" },
            { "title",     titleField.text.Trim()  },
            { "email",     emailField.text.Trim()  },
            { "name",      nameField.text.Trim()   },
            { "body",      bodyField.text.Trim()   },
            { "username",  username                },
            { "userId",    userId                  },
            { "city",      city                    },
            { "latitude",  GPSManager.Instance != null ? GPSManager.Instance.latitude  : 0f },
            { "longitude", GPSManager.Instance != null ? GPSManager.Instance.longitude : 0f },
            { "created",   System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            { "status",    "new"  },
            { "priority",  "none" },
        };

        var task = FirebaseFirestore.DefaultInstance.Collection("support_tickets").AddAsync(data);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
        {
            Debug.LogError($"[SupportTicket] Submit failed: {task.Exception}");
            if (validationText != null) validationText.text = "Failed to send. Please try again.";
            if (sendButton != null) sendButton.SetActive(true);
            yield break;
        }

        panelRoot.SetActive(false);
    }

    // ── City lookup (Mapbox reverse geocode) ───────────────────────────────

    private IEnumerator FetchCity(float lat, float lon, System.Action<string> onResult)
    {
        string token = MapLoader.instance != null ? MapLoader.instance.mapboxToken : "";
        if (string.IsNullOrEmpty(token)) { onResult(""); yield break; }

        string url = $"https://api.mapbox.com/geocoding/v5/mapbox.places/{lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}.json?types=place&access_token={token}";

        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) { onResult(""); yield break; }

        // Parse first feature place_name — take the first comma-separated part
        string json = req.downloadHandler.text;
        int nameIdx = json.IndexOf("\"place_name\":");
        if (nameIdx < 0) { onResult(""); yield break; }
        int start = json.IndexOf('"', nameIdx + 13) + 1;
        int end   = json.IndexOf('"', start);
        if (start < 1 || end < 0) { onResult(""); yield break; }
        string placeName = json.Substring(start, end - start);
        onResult(placeName.Split(',')[0].Trim());
    }
}
