using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class ThemeObject : MonoBehaviour
{
    public string themeName;

    [Space]
    public Sprite backdrop;
    public Color color_cover;

    [Space]
    public Color color_content;
    public Color color_title;
    public Color color_othertxt;
    public Color lines_color;

    [Space]
    public Color tabColor;

    [Space]
    public AudioClip music;

    [Space]
    public UI_StoryPanel storyPanel;

    [Button("Get Theme From Story Panel")]
    public void GetThemeFromStoryPanel()
    {
        backdrop = storyPanel.backdrop.sprite;
        color_cover = storyPanel.cover.color;
        color_content = storyPanel.content.color;
        color_title = storyPanel.title.color;
        color_othertxt = storyPanel.user.color;
        lines_color = storyPanel.lines_buttons[0].color;
    }
}
