using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_TurnOnDelayOrder : MonoBehaviour
{
    public List<GameObject> gameObjects;

    [Space]
    [Header("MODIFIERS")]
    public float delayPerObject = 0.05f;
    public bool CollectChildrenOnAwake;
    public bool CollectChildrenOnEnabled = false;
    public bool CollectChildOfChild = false;
    public bool CollectOnlyActiveChild = false;
    public bool turnOffInstead;
    public bool reverseOrder = false;
    public bool DoOnEnable;

    [Header("ShowTargetInList")]
    public bool showFirstOnEnable = false;
    public bool resetCountOnEnable = true;
    public bool loopNext = false;

    int currentCount = 0;

    WaitForSeconds wait;

    // Use this for initialization
    void Awake()
    {
        wait = new WaitForSeconds(delayPerObject * Time.timeScale);

        if (CollectChildrenOnAwake)
        {
            foreach (Transform child in transform)
            {
                if (!CollectOnlyActiveChild)
                {
                    if (CollectChildOfChild == true)
                    {
                        gameObjects.Add(child.GetChild(0).gameObject);
                    }
                    else
                    {
                        gameObjects.Add(child.gameObject);
                    }
                }
                else
                {
                    if (child.gameObject.activeSelf)
                    {
                        if (CollectChildOfChild == true)
                        {
                            gameObjects.Add(child.GetChild(0).gameObject);
                        }
                        else
                        {
                            gameObjects.Add(child.gameObject);
                        }
                    }
                }
            }
        }
    }

    //Does every time the object is enabled
    void OnEnable()
    {
        if (resetCountOnEnable)
        {
            currentCount = 0;
        }

        if (showFirstOnEnable)
        {
            ShowNext();
        }
        if (CollectChildrenOnEnabled)
        {
            gameObjects.Clear();
            foreach (Transform child in transform)
            {
                if (!CollectOnlyActiveChild)
                {
                    if (CollectChildOfChild == true)
                    {
                        gameObjects.Add(child.GetChild(0).gameObject);
                    }
                    else
                    {
                        gameObjects.Add(child.gameObject);
                    }
                }
                else
                {
                    if (child.gameObject.activeSelf)
                    {
                        if (CollectChildOfChild == true)
                        {
                            gameObjects.Add(child.GetChild(0).gameObject);
                        }
                        else
                        {
                            gameObjects.Add(child.gameObject);
                        }
                    }
                }
            }
        }
        if (DoOnEnable)
        {
            if (!turnOffInstead)
            {
                HideEverything();
                ShowUpEverythingWithDelay();
            }
            else
            {
                HideUpEverythingWithDelay();
            }
        }
    }

    public void CollectChild()
    {
        foreach (Transform child in transform)
        {
            gameObjects.Add(child.gameObject);
        }
    }


    //Function - To be accessed iternally: Hide Everything
    public void HideEverything()
    {
        foreach (GameObject _go in gameObjects)
        {
            if (_go != null)
            {
                _go.SetActive(false);
            }
        }
    }

    //Public Function - To be accessed externally: Show Everything with delay
    public void ShowUpEverythingWithDelay()
    {
        StartCoroutine(ShowDelay());
    }

    //Public Function - To be accessed externally: Hide Everything with delay
    public void HideUpEverythingWithDelay()
    {
        StartCoroutine(HideDelay());
    }

    IEnumerator ShowDelay()
    {
        if (!reverseOrder)
        {
            for (int i = 0; i < gameObjects.Count; i++)
            {
                yield return wait;
                if (gameObjects[i] != null)
                {
                    gameObjects[i].SetActive(true);
                }
            }
        }
        else
        {
            for (int i = gameObjects.Count - 1; i >= 0; i--)
            {
                yield return wait;
                if (gameObjects[i] != null)
                {
                    gameObjects[i].SetActive(true);
                }
            }
        }
    }

    IEnumerator HideDelay()
    {
        yield return wait;
        if (!reverseOrder)
        {
            for (int i = gameObjects.Count - 1; i >= 0; i--)
            {
                if (gameObjects[i] != null)
                {
                    yield return wait;
                    gameObjects[i].SetActive(false);
                }
            }
        }
        else
        {
            for (int i = gameObjects.Count - 1; i >= 0; i--)
            {
                if (gameObjects[i] != null)
                {
                    yield return wait;
                    gameObjects[i].SetActive(false);
                }
            }
        }
    }

    public void ShowSpecific(int order)
    {
        for (int i = 0; i < gameObjects.Count; i++)
        {
            if (i == order)
            {
                if (gameObjects[i] != null) gameObjects[i].SetActive(true);
            }
            else
            {
               if(gameObjects[i] != null) gameObjects[i].SetActive(false);
            }
        }
        currentCount = order ;
    }

    public void ShowNext()
    {
        if (0 <= currentCount && currentCount < gameObjects.Count)
        {
            ShowSpecific(currentCount);
            currentCount++;
            if (currentCount >= gameObjects.Count && loopNext)
            {
                currentCount = 0;
            }
        }
        else
        {
            HideEverything();
        }
    }
    public void SkipCurrentCountBy(int num)
    {
        currentCount += num;
        if (currentCount >= gameObjects.Count && loopNext)
        {
            currentCount = 0;
        }
    }
}
