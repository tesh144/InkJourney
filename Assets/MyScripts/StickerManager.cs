using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEditor;
#endif

public class StickerManager : MonoBehaviour
{
    public static StickerManager instance;

    [Tooltip("Index 0 = sticker ID 0, index 1 = sticker ID 1, etc.")]
    public List<Sprite> stickers = new List<Sprite>();

    public static event Action<int> OnPreviewStickerChanged;

    public static int CurrentPreviewStickerID { get; private set; } = 0;

#if UNITY_EDITOR
    [Button("Refresh Sticker Buttons")]
    private void RefreshStickerButtons()
    {
        StickerButton[] buttons = FindObjectsByType<StickerButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (StickerButton btn in buttons)
        {
            if (btn.previewImage == null) continue;
            Sprite sprite = GetSticker(btn.stickerID);
            btn.previewImage.sprite  = sprite;
            btn.previewImage.enabled = sprite != null;
            EditorUtility.SetDirty(btn.previewImage);
        }
        Debug.Log($"[StickerManager] Refreshed {buttons.Length} sticker buttons");
    }
#endif

    private void Awake()
    {
        instance = this;
    }

    public Sprite GetSticker(int stickerID)
    {
        if (stickers == null || stickerID < 0 || stickerID >= stickers.Count) return null;
        return stickers[stickerID];
    }

    public static void SetPreviewSticker(int stickerID)
    {
        CurrentPreviewStickerID = stickerID;
        OnPreviewStickerChanged?.Invoke(stickerID);
    }

    public static void ResetPreviewSticker()
    {
        CurrentPreviewStickerID = 0;
    }
}
