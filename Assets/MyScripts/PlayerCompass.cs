using UnityEngine;

public class PlayerCompass : MonoBehaviour
{
    [Tooltip("The arrow child RectTransform to rotate — should be a child of the player dot")]
    public RectTransform arrowTransform;

    [Tooltip("Smooth rotation speed. Set to 0 for instant snapping.")]
    public float smoothSpeed = 8f;

    private float _visualScale = 1f;
    private MapInputController _mapInputController;

    private void Start()
    {
        Input.compass.enabled = true;

        _visualScale = transform.localScale.x;
        if (_visualScale <= 0f) _visualScale = 1f;
    }

    private void Update()
    {
        if (arrowTransform == null) return;

        if (_mapInputController == null)
            _mapInputController = GetComponentInParent<MapInputController>() ?? MapInputController.instance;

        float heading = Input.compass.trueHeading;
        Quaternion target = Quaternion.Euler(0f, 0f, -heading);

        if (smoothSpeed > 0f)
            arrowTransform.rotation = Quaternion.Slerp(arrowTransform.rotation, target, Time.deltaTime * smoothSpeed);
        else
            arrowTransform.rotation = target;

        RectTransform mapContent = _mapInputController?.mapContent;
        if (mapContent != null)
        {
            float mapScale = mapContent.localScale.x;
            float t        = MapLoader.instance != null ? MapLoader.instance.compassCounterScale : 1f;
            float divisor  = Mathf.Lerp(1f, mapScale, t);
            if (divisor > 0f)
                transform.localScale = Vector3.one * (_visualScale / divisor);
        }
    }
}
