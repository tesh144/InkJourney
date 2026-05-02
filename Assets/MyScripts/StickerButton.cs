using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StickerButton : MonoBehaviour
{
    public int stickerID;
    public Button button;
    public Image previewImage;
    public TMP_Text label;

    public Animator anim;

    public Color selectedColor   = Color.white;
    public Color deselectedColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (previewImage == null)
            previewImage = GetComponentInChildren<Image>(true);

        if (button != null)
            button.onClick.AddListener(OnClick);

        RefreshPreview();
    }

    private void OnEnable()
    {
        StickerManager.OnPreviewStickerChanged += OnStickerChanged;
        RefreshPreview();
        RefreshSelectionState(StickerManager.CurrentPreviewStickerID);
    }

    private void OnDisable()
    {
        StickerManager.OnPreviewStickerChanged -= OnStickerChanged;
    }

    public void OnClick()
    {
        StickerManager.SetPreviewSticker(stickerID);
        anim.SetTrigger("pop");
    }

    private void OnStickerChanged(int selectedID)
    {
        RefreshSelectionState(selectedID);
    }

    private void RefreshPreview()
    {
        if (previewImage == null) return;
        Sprite sprite = StickerManager.instance != null ? StickerManager.instance.GetSticker(stickerID) : null;
        previewImage.sprite  = sprite;
        previewImage.enabled = sprite != null;
    }

    private void RefreshSelectionState(int selectedID)
    {
        bool selected = selectedID == stickerID;
        Color c = selected ? selectedColor : deselectedColor;
        if (previewImage != null) previewImage.color = c;
        if (label != null)        label.color        = c;
    }
}
