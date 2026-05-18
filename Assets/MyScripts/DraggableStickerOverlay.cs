using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class DraggableStickerOverlay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler
{
    [Min(0.1f)] public float minScale = 0.5f;
    [Min(0.1f)] public float maxScale = 3.0f;

    private RectTransform rt;
    private RectTransform parentRect;
    private Image image;

    private bool  isPointerDown;
    private bool  isPinching;
    private float pinchStartDistance;
    private float pinchStartScale;
    private float pinchStartAngle;
    private float pinchStartRotation;

    private void Awake() => EnsureInit();

    private void EnsureInit()
    {
        if (rt != null) return;
        rt         = GetComponent<RectTransform>();
        image      = GetComponent<Image>();
        parentRect = transform.parent as RectTransform;
        if (image != null) image.raycastTarget = true;
    }

    public void SetSticker(Sprite sprite, float normalizedX = 0.5f, float normalizedY = 0.5f, float scale = 1.0f, float rotation = 0f)
    {
        EnsureInit();
        image.sprite = sprite;
        gameObject.SetActive(sprite != null);
        if (sprite != null)
        {
            ApplyNormalized(normalizedX, normalizedY);
            rt.localScale       = new Vector3(scale, scale, 1f);
            rt.localEulerAngles = new Vector3(0f, 0f, rotation);
        }
    }

    public void Clear()
    {
        EnsureInit();
        image.sprite = null;
        gameObject.SetActive(false);
    }

    public float NormalizedX { get { EnsureInit(); return ToNormalized(rt.anchoredPosition.x, parentRect != null ? parentRect.rect.width  : 1f); } }
    public float NormalizedY { get { EnsureInit(); return ToNormalized(rt.anchoredPosition.y, parentRect != null ? parentRect.rect.height : 1f); } }
    public float Scale       { get { EnsureInit(); return rt != null ? rt.localScale.x : 1f; } }
    public float Rotation    { get { EnsureInit(); return rt != null ? rt.localEulerAngles.z : 0f; } }

    public void OnPointerDown(PointerEventData eventData) => isPointerDown = true;

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;
        isPinching    = false;
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (isPinching) return;
        if (parentRect == null) parentRect = transform.parent as RectTransform;
        if (parentRect == null) return;

        Camera cam = eventData.pressEventCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position,              cam, out Vector2 curr);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position - eventData.delta, cam, out Vector2 prev);

        Vector2 half = parentRect.rect.size * 0.5f;
        rt.anchoredPosition = new Vector2(
            Mathf.Clamp(rt.anchoredPosition.x + (curr.x - prev.x), -half.x, half.x),
            Mathf.Clamp(rt.anchoredPosition.y + (curr.y - prev.y), -half.y, half.y));
    }

    private void Update()
    {
        if (!isPointerDown || Input.touchCount < 2)
        {
            isPinching = false;
            return;
        }

        float currentDist = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);

        Vector2 touchDelta   = Input.GetTouch(1).position - Input.GetTouch(0).position;
        float   currentAngle = Mathf.Atan2(touchDelta.y, touchDelta.x) * Mathf.Rad2Deg;

        if (!isPinching)
        {
            isPinching         = true;
            pinchStartDistance = currentDist;
            pinchStartScale    = rt.localScale.x;
            pinchStartAngle    = currentAngle;
            pinchStartRotation = rt.localEulerAngles.z;
            return;
        }

        if (pinchStartDistance <= 0f) return;

        float scale = Mathf.Clamp(pinchStartScale * (currentDist / pinchStartDistance), minScale, maxScale);
        rt.localScale       = new Vector3(scale, scale, 1f);
        rt.localEulerAngles = new Vector3(0f, 0f, pinchStartRotation + (currentAngle - pinchStartAngle));
    }

    private void ApplyNormalized(float nx, float ny)
    {
        if (parentRect == null) return;
        rt.anchoredPosition = new Vector2(
            (nx - 0.5f) * parentRect.rect.width,
            (ny - 0.5f) * parentRect.rect.height);
    }

    private static float ToNormalized(float anchoredPos, float size) =>
        size > 0f ? (anchoredPos / size) + 0.5f : 0.5f;
}
