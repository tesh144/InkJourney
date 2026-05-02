using UnityEngine;
using UnityEngine.UI;

public class ScrollActivatedButton : MonoBehaviour
{
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] GameObject stateActive;
    [SerializeField] GameObject stateInactive;
    [SerializeField] Button     button;

    const float BottomThreshold = 0.01f;

    private void Awake()
    {
        scrollRect.onValueChanged.AddListener(_ => Refresh());
        Refresh();
    }

    private void Refresh()
    {
        bool atBottom = scrollRect.verticalNormalizedPosition <= BottomThreshold;
        if (stateActive   != null) stateActive.SetActive(atBottom);
        if (stateInactive != null) stateInactive.SetActive(!atBottom);
        if (button        != null) button.interactable = atBottom;
    }
}
