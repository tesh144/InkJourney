using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the MapBackground Image (sits behind the map RawImage).
/// Reads the active map style's base geometry colour and applies it so the
/// MapEdgeFade vignette blends seamlessly into the background.
/// </summary>
[RequireComponent(typeof(Image))]
public class MapBackground : MonoBehaviour
{
    // Fallback teal — matches the built-in default map style geometry colour
    private static readonly Color DefaultColor = new Color(0.153f, 0.569f, 0.580f);

    private Image _image;

    private void Awake()
    {
        _image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        MapLoader.onMainMapReloadStateChanged += OnReloadStateChanged;
        ApplyColor();
    }

    private void OnDisable()
    {
        MapLoader.onMainMapReloadStateChanged -= OnReloadStateChanged;
    }

    private void OnReloadStateChanged(bool reloading)
    {
        if (!reloading) ApplyColor();
    }

    private void ApplyColor()
    {
        if (MapLoader.instance != null)
            _image.color = MapLoader.instance.GetCurrentBackgroundColor();
        else
            _image.color = DefaultColor;
    }
}
