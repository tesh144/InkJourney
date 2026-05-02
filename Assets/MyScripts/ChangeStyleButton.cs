using UnityEngine;

public class ChangeStyleButton : MonoBehaviour
{
    [Tooltip("Which style index this button represents")]
    public int styleIndex;

    [Tooltip("The child indicator shown when this style is active")]
    public GameObject onIndicator;

    private bool lastState;

    private void OnEnable()
    {
        ForceRefresh();
    }

    private void Update()
    {
        if (MapLoader.instance == null || onIndicator == null) return;

        bool active = MapLoader.instance.currentStyleIndex == styleIndex;
        if (active != lastState)
        {
            onIndicator.SetActive(active);
            lastState = active;
        }
    }

    public void OnClick()
    {
        if (MapLoader.instance != null)
            MapLoader.instance.ChooseStyle(styleIndex);
    }

    private void ForceRefresh()
    {
        if (MapLoader.instance == null || onIndicator == null) return;
        lastState = MapLoader.instance.currentStyleIndex == styleIndex;
        onIndicator.SetActive(lastState);
    }
}
