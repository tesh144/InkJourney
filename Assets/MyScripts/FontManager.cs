using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class FontManager : MonoBehaviour
{
    public static FontManager instance;

    [Tooltip("Font at index 0 = font ID 0, index 1 = font ID 1, etc.")]
    public List<TMP_FontAsset> fonts = new List<TMP_FontAsset>();

    public static event Action<int> OnPreviewFontChanged;
    public static int CurrentPreviewFontID { get; private set; } = 0;

    private void Awake()
    {
        instance = this;
    }

    public TMP_FontAsset GetFont(int fontID)
    {
        if (fonts == null || fonts.Count == 0) return null;
        return fonts[Mathf.Clamp(fontID, 0, fonts.Count - 1)];
    }

    public int FontCount => fonts != null ? fonts.Count : 0;

    public static void SetPreviewFont(int fontID)
    {
        CurrentPreviewFontID = fontID;
        OnPreviewFontChanged?.Invoke(fontID);
    }

    public static void ResetPreviewFont()
    {
        CurrentPreviewFontID = 0;
    }
}
