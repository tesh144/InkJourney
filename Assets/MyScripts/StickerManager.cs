using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
    public RectTransform stickerButtonContainer;
    public GameObject stickerButtonPrefab;

    [Button("Spawn Sticker Buttons")]
    private void SpawnStickerButtons()
    {
        if (stickerButtonContainer == null) { Debug.LogError("[StickerManager] stickerButtonContainer is not assigned."); return; }
        if (stickerButtonPrefab == null)    { Debug.LogError("[StickerManager] stickerButtonPrefab is not assigned.");    return; }

        // Clear existing children
        for (int i = stickerButtonContainer.childCount - 1; i >= 0; i--)
            DestroyImmediate(stickerButtonContainer.GetChild(i).gameObject);

        for (int i = 0; i < stickers.Count; i++)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(stickerButtonPrefab, stickerButtonContainer);
            StickerButton btn = go.GetComponent<StickerButton>();
            if (btn == null) continue;

            btn.stickerID = i;
            go.name = stickers[i] != null ? $"{i:D3}_{stickers[i].name}" : $"{i:D3}_empty";

            if (btn.previewImage == null)
                btn.previewImage = go.GetComponentInChildren<Image>(true);

            if (btn.previewImage != null)
            {
                btn.previewImage.sprite  = stickers[i];
                btn.previewImage.enabled = stickers[i] != null;
                EditorUtility.SetDirty(btn.previewImage);
            }

            EditorUtility.SetDirty(go);
        }

        EditorUtility.SetDirty(stickerButtonContainer.gameObject);
        Debug.Log($"[StickerManager] Spawned {stickers.Count} sticker buttons in {stickerButtonContainer.name}");
    }

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
