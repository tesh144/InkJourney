using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.UI;
using UnityEngine;

public class ThemeManager : MonoBehaviour
{
    public List<ThemeObject> themes;
    public AudioSource themeMusic;
    public AudioSource mainMusic;
    public UI_StoryPanel storyPanel;

    public Image themeDisplayTab;
    public Image themePostWindowBD;
    public Text themeSelectText;

    public static ThemeManager instance;

    [Header("Audio Settings")]

    [Header("Music Toggle")]
    public GameObject musicOnState;
    public GameObject musicOffState;

    [Header("SFX Toggle")]
    public GameObject sfxOnState;
    public GameObject sfxOffState;

    // ── Persistent settings ────────────────────────────────────────────────
    private const string MusicPrefKey = "MusicEnabled";
    private const string SfxPrefKey   = "SfxEnabled";

    public static bool MusicEnabled { get; private set; }
    public static bool SfxEnabled   { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        MusicEnabled = PlayerPrefs.GetInt(MusicPrefKey, 1) == 1;
        SfxEnabled   = PlayerPrefs.GetInt(SfxPrefKey,   1) == 1;

        RefreshMusicButton();
        RefreshSfxButton();
    }

    private void OnEnable()  => MapLoader.onStyleChanged += ApplyMapStyleMusic;
    private void OnDisable() => MapLoader.onStyleChanged -= ApplyMapStyleMusic;

    private void Start()
    {
        ApplyMapStyleMusic();
        ApplyMusicVolume();
    }

    // ── Map style music ────────────────────────────────────────────────────

    private void ApplyMapStyleMusic()
    {
        var styles = MapLoader.instance?.mapStyles;
        if (styles == null || styles.Count == 0) return;
        int idx = Mathf.Clamp(MapLoader.instance.currentStyleIndex, 0, styles.Count - 1);
        AudioClip clip = styles[idx].music;
        if (clip == null || mainMusic.clip == clip) return;
        mainMusic.clip   = clip;
        mainMusic.volume = MusicEnabled ? 1f : 0f;
        if (MusicEnabled) mainMusic.Play();
    }

    // ── Theme (visuals only — music no longer changes on story open/close) ─

    public string testTheme;
    [Button("Set Test Theme")]
    public void SetTestTheme() => SetTheme(testTheme);

    public void SetPostToTheme(ThemeObject theme)
    {
        themePostWindowBD.sprite = theme.backdrop;
        themePostWindowBD.gameObject.SetActive(theme.backdrop != null);
        themeDisplayTab.color = theme.tabColor;
        themeSelectText.color = theme.color_title;
        themeSelectText.text = "Theme: " + theme.themeName;
        selectedTheme = theme;
    }

    [ReadOnly] public ThemeObject selectedTheme;
    public void SetSelectedTheme()
    {
        if (selectedTheme == null) { Debug.LogError("ThemeManager: selectedTheme is null."); return; }
        SetTheme(selectedTheme.themeName);
    }

    public ThemeObject GetThemeByName(string themeName)
    {
        if (string.IsNullOrEmpty(themeName) || themes == null || themes.Count == 0) return null;
        for (int i = 0; i < themes.Count; i++)
            if (themes[i] != null && themes[i].themeName == themeName) return themes[i];
        return null;
    }

    public void SetTheme(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (storyPanel == null) storyPanel = ObjectManager.instance?.storyPanel;
        ThemeObject themeObj = GetThemeByName(id);
        if (themeObj == null || storyPanel == null) return;

        if (storyPanel.backdrop != null)              storyPanel.backdrop.sprite = themeObj.backdrop;
        if (storyPanel.cover != null)                 storyPanel.cover.color = themeObj.color_cover;
        if (storyPanel.content != null)               storyPanel.content.color = themeObj.color_content;
        if (storyPanel.title != null)                 storyPanel.title.color = themeObj.color_title;
        if (storyPanel.user != null)                  storyPanel.user.color = themeObj.color_othertxt;
        if (storyPanel.location_expire_text != null)  storyPanel.location_expire_text.color = themeObj.color_title;

        if (storyPanel.lines_buttons != null)
            foreach (Image img in storyPanel.lines_buttons)
                if (img != null) img.color = themeObj.lines_color;
    }

    // ── Music toggle ───────────────────────────────────────────────────────

    public void ToggleMusic()
    {
        MusicEnabled = !MusicEnabled;
        PlayerPrefs.SetInt(MusicPrefKey, MusicEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMusicVolume();
        RefreshMusicButton();
    }

    private void ApplyMusicVolume()
    {
        if (mainMusic == null) return;
        mainMusic.volume = MusicEnabled ? 1f : 0f;
        if (MusicEnabled && !mainMusic.isPlaying) mainMusic.Play();
    }

    private void RefreshMusicButton()
    {
        if (musicOnState  != null) musicOnState.SetActive(MusicEnabled);
        if (musicOffState != null) musicOffState.SetActive(!MusicEnabled);
    }

    // ── SFX toggle ─────────────────────────────────────────────────────────

    public void ToggleSfx()
    {
        SfxEnabled = !SfxEnabled;
        PlayerPrefs.SetInt(SfxPrefKey, SfxEnabled ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSfxButton();
    }

    private void RefreshSfxButton()
    {
        if (sfxOnState  != null) sfxOnState.SetActive(SfxEnabled);
        if (sfxOffState != null) sfxOffState.SetActive(!SfxEnabled);
    }
}
