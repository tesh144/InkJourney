using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FontCycleButton : MonoBehaviour
{
    public Button button;
    public Image buttonImage;
    public Color selectedColor   = Color.white;
    public Color deselectedColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    private int _currentFontID;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    private void OnEnable()
    {
        FontManager.OnPreviewFontChanged += OnFontChanged;
        ApplyFont();
        RefreshButtonState();
    }

    private void OnDisable()
    {
        FontManager.OnPreviewFontChanged -= OnFontChanged;
    }

    private void OnFontChanged(int fontID)
    {
        _currentFontID = fontID;
        ApplyFont();
        RefreshButtonState();
    }

    public void SetFont(int fontID)
    {
        _currentFontID = Mathf.Clamp(fontID, 0, Mathf.Max(0, (FontManager.instance?.FontCount ?? 1) - 1));
        ApplyFont();
        RefreshButtonState();
    }

    public void OnClick()
    {
        int count = FontManager.instance != null ? FontManager.instance.FontCount : 0;
        if (count == 0) return;

        _currentFontID = (_currentFontID + 1) % count;
        ApplyFont();
        RefreshButtonState();
        FontManager.SetPreviewFont(_currentFontID);
    }

    private void ApplyFont()
    {
        if (FontManager.instance == null) return;
        TMP_FontAsset font = FontManager.instance.GetFont(_currentFontID);
        if (font == null) return;

        var createTitle = CreateNewStory.instance?.title?.textComponent;
        if (createTitle != null) createTitle.font = font;

        var storyTitle = ObjectManager.instance?.storyPanel?.title;
        if (storyTitle != null) storyTitle.font = font;
    }

    private void RefreshButtonState()
    {
        if (buttonImage == null) return;
        buttonImage.color = _currentFontID != 0 ? selectedColor : deselectedColor;
    }
}
