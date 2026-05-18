using Sirenix.OdinInspector;
using UnityEngine.UI;
using UnityEngine;

public class ThemeTab : MonoBehaviour
{
    public ThemeObject theme;

    public Image tab;
    public Text  text;

    void Start() => SetUpTab();

    public void ChooseTheme()
    {
        if (ThemeManager.instance == null || theme == null) return;
        ThemeManager.instance.SetTheme(theme.themeName);
    }

    [Button("Set Up Tab")]
    void SetUpTab()
    {
        if (theme == null) return;
        if (text != null) text.text = "Theme: " + theme.themeName;
    }
}
