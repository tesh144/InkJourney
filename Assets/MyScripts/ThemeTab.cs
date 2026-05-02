using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.UI;
using UnityEngine;

public class ThemeTab : MonoBehaviour
{
    public ThemeObject theme;

    public Image tab;
    public Text text;

    // Start is called before the first frame update
    void Start()
    {
        SetUpTab();
    }

    public void ChooseTheme()
    {
        ThemeManager.instance.SetPostToTheme(theme);
        ThemeManager.instance.SetSelectedTheme();
    }

    [Button("Set Up Tab")]
    void SetUpTab()
    {
        tab.color = theme.tabColor;
        text.color = theme.color_title;
        text.text = "Theme: " + theme.themeName;
    }
}
