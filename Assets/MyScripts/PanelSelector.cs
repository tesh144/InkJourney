using System.Collections.Generic;
using UnityEngine;

public class PanelSelector : MonoBehaviour
{
    public List<GameObject> pages = new List<GameObject>();

    private int _current = 0;

    private void OnEnable()
    {
        for (int i = 0; i < pages.Count; i++)
            if (pages[i] != null) pages[i].SetActive(i == _current);
    }

    public void Next()
    {
        SetPage((_current + 1) % pages.Count);
    }

    public void Back()
    {
        SetPage((_current - 1 + pages.Count) % pages.Count);
    }

    public void SetPanel(int index)
    {
        SetPage(Mathf.Clamp(index, 0, pages.Count - 1));
    }

    private void SetPage(int index)
    {
        if (pages.Count == 0) return;
        if (pages[_current] != null) pages[_current].SetActive(false);
        _current = index;
        if (pages[_current] != null) pages[_current].SetActive(true);
    }
}
