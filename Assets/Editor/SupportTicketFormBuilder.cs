using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public static class SupportTicketFormBuilder
{
    [MenuItem("InkJourney/Create Support Ticket Form Prefab")]
    public static void Build()
    {
        string savePath = "Assets/Prefabs/SupportTicketForm.prefab";
        System.IO.Directory.CreateDirectory("Assets/Prefabs");

        // ── Root (full-screen overlay) ─────────────────────────────────────
        var root = new GameObject("SupportTicketForm");
        root.AddComponent<RectTransform>();
        Stretch(root.GetComponent<RectTransform>());

        var overlay = MakeImage("Overlay", root.transform, new Color(0, 0, 0, 0.85f));
        Stretch(overlay.GetComponent<RectTransform>());

        // ── Panel (full screen, dark background) ───────────────────────────
        var panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        var panelRT = panel.AddComponent<RectTransform>();
        Stretch(panelRT);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.10f, 0.10f, 0.15f, 1f);

        // ── Header (fixed top) ─────────────────────────────────────────────
        var header = new GameObject("Header");
        header.transform.SetParent(panel.transform, false);
        var headerRT = header.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot     = new Vector2(0.5f, 1);
        headerRT.offsetMin = new Vector2(0, -72);
        headerRT.offsetMax = Vector2.zero;
        var headerHL = header.AddComponent<HorizontalLayoutGroup>();
        headerHL.padding           = new RectOffset(20, 20, 0, 0);
        headerHL.spacing           = 12;
        headerHL.childAlignment    = TextAnchor.MiddleLeft;
        headerHL.childControlWidth = false; headerHL.childControlHeight = false;
        headerHL.childForceExpandWidth = false; headerHL.childForceExpandHeight = false;

        var formTitle = MakeText("FormTitle", header.transform, "Report a Problem", 22, Color.white, TextAlignmentOptions.Left);
        formTitle.fontStyle = FontStyles.Bold;
        var formTitleLE = formTitle.gameObject.AddComponent<LayoutElement>();
        formTitleLE.flexibleWidth  = 1;
        formTitleLE.preferredHeight = 72;

        var closeBtn = MakeButton("CloseButton", header.transform, "✕", 18,
            new Color(0.6f, 0.6f, 0.7f), Color.clear);
        var closeBtnLE = closeBtn.gameObject.AddComponent<LayoutElement>();
        closeBtnLE.preferredWidth  = 44;
        closeBtnLE.preferredHeight = 44;

        // ── Divider ────────────────────────────────────────────────────────
        var divider = MakeImage("Divider", panel.transform, new Color(0.22f, 0.22f, 0.32f, 1f));
        var dividerRT = divider.GetComponent<RectTransform>();
        dividerRT.anchorMin = new Vector2(0, 1);
        dividerRT.anchorMax = new Vector2(1, 1);
        dividerRT.pivot     = new Vector2(0.5f, 1);
        dividerRT.offsetMin = new Vector2(0, -73);
        dividerRT.offsetMax = new Vector2(0, -72);

        // ── Scroll view (fields area) ──────────────────────────────────────
        var scrollGO = new GameObject("ScrollView");
        scrollGO.transform.SetParent(panel.transform, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(0,  72);   // above footer
        scrollRT.offsetMax = new Vector2(0, -73);   // below header+divider
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGO.transform, false);
        Stretch(viewport.AddComponent<RectTransform>());
        viewport.AddComponent<Image>().color = Color.clear;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot     = new Vector2(0.5f, 1);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var contentVL = content.AddComponent<VerticalLayoutGroup>();
        contentVL.padding           = new RectOffset(20, 20, 20, 20);
        contentVL.spacing           = 18;
        contentVL.childControlWidth = true; contentVL.childControlHeight = false;
        contentVL.childForceExpandWidth = true; contentVL.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRT;

        // ── Form fields ────────────────────────────────────────────────────
        var titleGroup = MakeFieldGroup("TitleGroup", content.transform);
        MakeLabelText("Label", titleGroup.transform, "Title  *");
        var titleInput = MakeTMPInputField("TitleInput", titleGroup.transform, "e.g. App crashes on startup", false);

        var emailGroup = MakeFieldGroup("EmailGroup", content.transform);
        MakeLabelText("Label", emailGroup.transform, "Email Address  *");
        var emailInput = MakeTMPInputField("EmailInput", emailGroup.transform, "your@email.com", false);
        emailInput.contentType = TMP_InputField.ContentType.EmailAddress;

        var nameGroup = MakeFieldGroup("NameGroup", content.transform);
        MakeLabelText("Label", nameGroup.transform, "Name");
        var nameInput = MakeTMPInputField("NameInput", nameGroup.transform, "Optional", false);

        var bodyGroup = MakeFieldGroup("BodyGroup", content.transform);
        MakeLabelText("Label", bodyGroup.transform, "Description  *");
        var bodyInput = MakeTMPInputField("BodyInput", bodyGroup.transform, "Tap to describe the issue…", true);
        bodyInput.interactable = false;

        // Transparent tap button over body
        var bodyTap = new GameObject("BodyTapButton");
        bodyTap.transform.SetParent(bodyGroup.transform, false);
        Stretch(bodyTap.AddComponent<RectTransform>());
        bodyTap.AddComponent<Image>().color = Color.clear;
        bodyTap.AddComponent<Button>();

        var validationText = MakeText("ValidationText", content.transform, "", 12,
            new Color(1f, 0.45f, 0.45f), TextAlignmentOptions.Center);
        validationText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18;

        // ── Footer (fixed bottom, full-width buttons) ──────────────────────
        var footer = new GameObject("Footer");
        footer.transform.SetParent(panel.transform, false);
        var footerRT = footer.AddComponent<RectTransform>();
        footerRT.anchorMin = new Vector2(0, 0);
        footerRT.anchorMax = new Vector2(1, 0);
        footerRT.pivot     = new Vector2(0.5f, 0);
        footerRT.offsetMin = new Vector2(0,  0);
        footerRT.offsetMax = new Vector2(0, 72);
        var footerHL = footer.AddComponent<HorizontalLayoutGroup>();
        footerHL.padding        = new RectOffset(16, 16, 12, 12);
        footerHL.spacing        = 12;
        footerHL.childControlWidth  = true;  footerHL.childControlHeight = true;
        footerHL.childForceExpandWidth  = true; footerHL.childForceExpandHeight = true;

        var cancelBtn = MakeButton("CancelButton", footer.transform, "Cancel", 16,
            new Color(0.75f, 0.75f, 0.85f), new Color(0.15f, 0.15f, 0.22f));

        var sendBtn = new GameObject("SendButton");
        sendBtn.transform.SetParent(footer.transform, false);
        sendBtn.AddComponent<RectTransform>();
        var sendBtnImg = sendBtn.AddComponent<Image>();
        sendBtnImg.color = new Color(0.76f, 0.48f, 1f, 1f);
        var sendBtnComp = sendBtn.AddComponent<Button>();
        sendBtnComp.targetGraphic = sendBtnImg;
        var sendLabel = MakeText("Label", sendBtn.transform, "Send", 16, Color.white, TextAlignmentOptions.Center);
        sendLabel.fontStyle = FontStyles.Bold;
        Stretch(sendLabel.GetComponent<RectTransform>());

        // ── Wire SupportTicketForm ─────────────────────────────────────────
        var form = root.AddComponent<SupportTicketForm>();
        form.panelRoot      = root;
        form.formTitle      = formTitle;
        form.titleField     = titleInput;
        form.emailField     = emailInput;
        form.nameField      = nameInput;
        form.bodyField      = bodyInput;
        form.sendButton     = sendBtn;
        form.validationText = validationText;

        // Wire Cancel buttons
        closeBtn.GetComponent<Button>().onClick.AddListener(form.OnCancelPressed);
        cancelBtn.GetComponent<Button>().onClick.AddListener(form.OnCancelPressed);

        // Wire Send button
        sendBtnComp.onClick.AddListener(form.OnSendPressed);

        // Wire body tap
        bodyTap.GetComponent<Button>().onClick.AddListener(form.OpenBodyEditor);

        // ── Input field change listeners ───────────────────────────────────
        titleInput.onValueChanged.AddListener(_ => form.RefreshValidation());
        emailInput.onValueChanged.AddListener(_ => form.RefreshValidation());
        bodyInput.onValueChanged.AddListener(_ => form.RefreshValidation());

        // ── Save prefab ────────────────────────────────────────────────────
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, savePath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();

        Debug.Log($"[SupportTicketFormBuilder] Prefab saved to {savePath}");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = prefab;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Image MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static TextMeshProUGUI MakeText(string name, Transform parent, string text, float size,
        Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.alignment = align;
        return tmp;
    }

    static TextMeshProUGUI MakeLabelText(string name, Transform parent, string text)
    {
        var t = MakeText(name, parent, text, 11, new Color(0.55f, 0.55f, 0.7f), TextAlignmentOptions.Left);
        t.fontStyle = FontStyles.Bold;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 16;
        return t;
    }

    static Button MakeButton(string name, Transform parent, string label, float fontSize,
        Color textColor, Color bgColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var outline = go.AddComponent<Outline>();
        outline.effectColor    = new Color(0.3f, 0.3f, 0.45f, 1f);
        outline.effectDistance = new Vector2(1, -1);
        MakeText("Label", go.transform, label, fontSize, textColor, TextAlignmentOptions.Center);
        return btn;
    }

    static GameObject MakeFieldGroup(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var vl = go.AddComponent<VerticalLayoutGroup>();
        vl.spacing            = 6;
        vl.childControlWidth  = true;
        vl.childControlHeight = false;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    static GameObject MakeHorizontal(string name, Transform parent, float spacing, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing            = spacing;
        hl.childAlignment     = TextAnchor.MiddleLeft;
        hl.childControlWidth  = false;
        hl.childControlHeight = false;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;
        return go;
    }

    static TMP_InputField MakeTMPInputField(string name, Transform parent, string placeholder, bool multiline)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, multiline ? 100 : 44);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.13f, 1f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor    = new Color(0.18f, 0.18f, 0.26f, 1f);
        outline.effectDistance = new Vector2(1, -1);

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = multiline ? 100 : 44;

        // Text Area
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(go.transform, false);
        var taRT = textArea.AddComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(10, 6);
        taRT.offsetMax = new Vector2(-10, -6);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(textArea.transform, false);
        Stretch(phGO.AddComponent<RectTransform>());
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        ph.text      = placeholder;
        ph.fontSize  = 14;
        ph.color     = new Color(0.4f, 0.4f, 0.55f, 1f);
        ph.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(textArea.transform, false);
        Stretch(textGO.AddComponent<RectTransform>());
        var txt = textGO.AddComponent<TextMeshProUGUI>();
        txt.fontSize  = 14;
        txt.color     = Color.white;
        txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;

        // TMP_InputField
        var field = go.AddComponent<TMP_InputField>();
        field.textComponent  = txt;
        field.placeholder    = ph;
        field.lineType = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;

        return field;
    }
}
