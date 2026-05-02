using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the vignette Image. Keeps the shader's aspect ratio in sync with
/// the RectTransform so the vignette stays circular on any screen size.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapVignette : MonoBehaviour
{
    private static readonly int AspectRatioId = Shader.PropertyToID("_AspectRatio");

    private RawImage   _image;
    private RectTransform _rt;
    private Vector2    _lastSize;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _rt    = GetComponent<RectTransform>();
    }

    private void Update()
    {
        Vector2 size = _rt.rect.size;
        if (size == _lastSize) return;
        _lastSize = size;

        if (_image.material != null && size.y > 0f)
            _image.material.SetFloat(AspectRatioId, size.x / size.y);
    }
}
