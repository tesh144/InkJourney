using UnityEngine;

public class DestinationMarker : MonoBehaviour
{
    public static DestinationMarker instance;

    [Tooltip("The marker RectTransform to show/hide and reposition — must be inside the map RectTransform")]
    public RectTransform markerObject;
    public RectTransform mapRectTransform;

    private RectTransform _target;
    private bool          _hasDestination;

    private void Awake()
    {
        instance = this;
        if (markerObject != null) markerObject.gameObject.SetActive(false);
    }

    public void SetDestination(RectTransform target)
    {
        _target         = target;
        _hasDestination = true;
        markerObject.gameObject.SetActive(true);
        markerObject.localPosition = target.localPosition;
    }

    public void ClearDestination()
    {
        _target         = null;
        _hasDestination = false;
        if (markerObject != null) markerObject.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_hasDestination || markerObject == null) return;
        ApplyCounterTransform();
    }

    private void ApplyCounterTransform()
    {
        if (mapRectTransform == null) return;
        float s = mapRectTransform.localScale.x;
        if (s > 0f) markerObject.localScale = Vector3.one / s;
        markerObject.localEulerAngles = new Vector3(0f, 0f, -mapRectTransform.eulerAngles.z);
    }
}
