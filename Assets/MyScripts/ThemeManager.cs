using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.UI;
using UnityEngine;
using TMPro;

public class ThemeManager : MonoBehaviour
{
    public List<ThemeObject> themes;
    public AudioSource themeMusic;
    public AudioSource mainMusic;

    public Image creationBackdrop;
    public Image readingBackdrop;

    [Header("Color One")]
    public List<Image>   colorOneImages;
    public List<TMP_Text> colorOneTexts;

    [Header("Color Two")]
    public List<Image>   colorTwoImages;
    public List<TMP_Text> colorTwoTexts;

    [Header("Color Three")]
    public List<Image>   colorThreeImages;
    public List<TMP_Text> colorThreeTexts;

    [Header("Color Four")]
    public List<Image>   colorFourImages;
    public List<TMP_Text> colorFourTexts;

    public static ThemeManager instance;

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

    private void OnEnable()
    {
        MapLoader.onStyleChanged += ApplyMapStyleMusic;
        MapLoader.onStyleChanged += ApplyMapStyleTheme;
    }

    private void OnDisable()
    {
        MapLoader.onStyleChanged -= ApplyMapStyleMusic;
        MapLoader.onStyleChanged -= ApplyMapStyleTheme;
    }

    private void Start()
    {
        ApplyMapStyleMusic();
        ApplyMusicVolume();
    }

    // ── Map style theme ────────────────────────────────────────────────────

    private void ApplyMapStyleTheme()
    {
        var styles = MapLoader.instance?.mapStyles;
        if (styles == null || styles.Count == 0) return;
        int idx = Mathf.Clamp(MapLoader.instance.currentStyleIndex, 0, styles.Count - 1);
        ThemeObject theme = styles[idx].defaultTheme;
        if (theme == null) return;
        selectedTheme = theme;
        SetTheme(theme.themeName);
    }

    public void ApplyCurrentMapStyleTheme() => ApplyMapStyleTheme();

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

    // ── Theme ──────────────────────────────────────────────────────────────

    public string testTheme;
    [Button("Set Test Theme")]
    public void SetTestTheme() => SetTheme(testTheme);

    [ReadOnly] public ThemeObject selectedTheme;

    public ThemeObject GetThemeByName(string themeName)
    {
        if (string.IsNullOrEmpty(themeName) || themes == null) return null;
        for (int i = 0; i < themes.Count; i++)
            if (themes[i] != null && themes[i].themeName == themeName) return themes[i];
        return null;
    }

    public void SetTheme(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        ThemeObject t = GetThemeByName(id);
        if (t == null) return;

        if (creationBackdrop != null) creationBackdrop.sprite = t.backdrop;
        if (readingBackdrop  != null) readingBackdrop.sprite  = t.backdrop;

        ApplyColor(colorOneImages,   colorOneTexts,   t.color_one);
        ApplyColor(colorTwoImages,   colorTwoTexts,   t.color_two);
        ApplyColor(colorThreeImages, colorThreeTexts, t.color_three);
        ApplyColor(colorFourImages,  colorFourTexts,  t.color_four);
    }

    private static void ApplyColor(List<Image> images, List<TMP_Text> texts, Color color)
    {
        if (images != null)
            foreach (var img in images)
                if (img != null) img.color = color;

        if (texts != null)
            foreach (var txt in texts)
                if (txt != null) txt.color = color;
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
