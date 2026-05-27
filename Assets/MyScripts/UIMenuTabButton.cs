using UnityEngine;

[RequireComponent(typeof(UnityEngine.UI.Button))]
public class UIMenuTabButton : MonoBehaviour
{
    public GameObject selectedIndicator;

    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
    }
}
