using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UIMenuTabSystem : MonoBehaviour
{
    public List<UIMenuTabButton> tabs;
    public List<GameObject> screens;
    public UnityEvent<int> onTabSelected;

    private void Start()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            tabs[i].GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => SelectTab(index));
        }

        if (tabs.Count > 0)
            SelectTab(0);
    }

    public void SelectTab(int index)
    {
        for (int i = 0; i < tabs.Count; i++)
            tabs[i].SetSelected(i == index);

        for (int i = 0; i < screens.Count; i++)
            if (screens[i] != null)
                screens[i].SetActive(i == index);

        onTabSelected?.Invoke(index);
    }
}
