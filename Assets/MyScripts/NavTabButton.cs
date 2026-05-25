using UnityEngine;

/// Attach to each nav bar tab button.
/// OnClick() should be wired to this component's OnClick method.
public class NavTabButton : MonoBehaviour
{
    [Tooltip("Index into UIStateManager.coreStates")]
    public int coreStateIndex;

    [Tooltip("Shown when this tab is the active CoreState")]
    public GameObject selectedIndicator;

    [Tooltip("Shown when this tab is NOT the active CoreState")]
    public GameObject unselectedIndicator;

    private void OnEnable()
    {
        UIStateManager.instance?.RegisterNavButton(this);
        UpdateSelected(UIStateManager.instance?.CurrentCoreIndex ?? -1);
    }

    private void OnDisable()
    {
        UIStateManager.instance?.UnregisterNavButton(this);
    }

    public void OnClick()
    {
        UIStateManager.instance?.ActivateCoreState(coreStateIndex);
    }

    public void UpdateSelected(int activeCoreIndex)
    {
        bool selected = activeCoreIndex == coreStateIndex;
        if (selectedIndicator   != null) selectedIndicator.SetActive(selected);
        if (unselectedIndicator != null) unselectedIndicator.SetActive(!selected);
    }
}
