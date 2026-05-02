using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attach to the transparent overlay above the content field (instead of Button).
/// - Taps open the native text editor.
/// - Scroll and drag events pass through to the parent ScrollRect so the panel still scrolls.
/// Requires an Image component on this GameObject for raycasting.
/// </summary>
[RequireComponent(typeof(Image))]
public class ContentTapHandler : MonoBehaviour,
    IPointerClickHandler, IScrollHandler,
    IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private ScrollRect _scrollRect;
    private bool       _dragging;

    void Start()
    {
        _scrollRect = GetComponentInParent<ScrollRect>();

        // Transparent but still raycast-able
        var img = GetComponent<Image>();
        img.color = Color.clear;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!_dragging)
            CreateNewStory.instance?.OpenContentEditor();
    }

    // Forward scroll and drag events up to the ScrollRect.
    // OnInitializePotentialDrag MUST be forwarded — the ScrollRect uses it to record the
    // starting cursor position. Without it the scroll jumps on device (works in editor
    // because mouse input is fine-grained enough to hide the missing initialisation).
    public void OnInitializePotentialDrag(PointerEventData e) => _scrollRect?.OnInitializePotentialDrag(e);
    public void OnScroll(PointerEventData e)       => _scrollRect?.OnScroll(e);
    public void OnBeginDrag(PointerEventData e)    { _dragging = true;  _scrollRect?.OnBeginDrag(e); }
    public void OnDrag(PointerEventData e)         => _scrollRect?.OnDrag(e);
    public void OnEndDrag(PointerEventData e)      { _dragging = false; _scrollRect?.OnEndDrag(e);   }
}
