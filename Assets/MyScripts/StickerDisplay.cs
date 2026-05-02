using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class StickerDisplay : MonoBehaviour
{
    public Image imageTarget;

    [Tooltip("When checked: image is always on — never hidden. When unchecked: image starts hidden and only turns on when a valid sticker is set.")]
    public bool alwaysOn = false;

    [Tooltip("Fired when the sticker ID is 0 (no sticker selected)")]
    public UnityEvent onNoSticker;

    [Tooltip("Fired when the sticker ID is greater than 0 (a sticker is selected)")]
    public UnityEvent onStickerSelected;

    private void Awake()
    {
        if (imageTarget == null)
            imageTarget = GetComponent<Image>();
    }

    private void OnEnable()
    {
        StickerManager.OnPreviewStickerChanged += ShowSticker;
        if (imageTarget != null && !alwaysOn)
            imageTarget.enabled = false;
        ShowSticker(StickerManager.CurrentPreviewStickerID);
    }

    private void OnDisable()
    {
        StickerManager.OnPreviewStickerChanged -= ShowSticker;
    }

    public void ShowSticker(int stickerID)
    {
        if (imageTarget == null) return;
        Sprite sprite = StickerManager.instance != null ? StickerManager.instance.GetSticker(stickerID) : null;
        imageTarget.sprite = sprite;
        // alwaysOn unchecked: hide sticker 0, only show sticker ID >= 1
        // alwaysOn checked: show sticker 0 and above (any valid sprite)
        imageTarget.enabled = (stickerID >= 1 || alwaysOn) && sprite != null;

        if (stickerID == 0)
            onNoSticker?.Invoke();
        else
            onStickerSelected?.Invoke();
    }
}
