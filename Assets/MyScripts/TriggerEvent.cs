using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

public class TriggerEvent : MonoBehaviour {

	[FoldoutGroup("event on awake")] public UnityEvent EventOnAwake;
    [FoldoutGroup("event on start")] public UnityEvent EventOnStart;
    [FoldoutGroup("event on enable")] public UnityEvent EventOnEnable;
    [FoldoutGroup("event on disable")] public UnityEvent EventOnDisable;
    [FoldoutGroup("event A")] public UnityEvent EventA;
    [FoldoutGroup("event B")] public UnityEvent EventB;
    [FoldoutGroup("event on enter")] public UnityEvent EventOnEnter;
    [FoldoutGroup("event on enter")] public string enter_tag = "Player";
    [FoldoutGroup("event on exit")] public UnityEvent EventOnExit;
    [FoldoutGroup("event on exit")] public string exit_tag = "Player";
    [Space]
    [FoldoutGroup("event after time")] public float TimeValue;
    [FoldoutGroup("event after time")] public UnityEvent EventAfterTime;

    // Use this for initialization
    private void Awake()
    {
        EventOnAwake.Invoke();
    }

    public void Search_Link(string link)
    {
        Application.OpenURL(link);
    }
    void Start () {
		EventOnStart.Invoke ();
	}

    void OnEnable(){
        EventOnEnable.Invoke();
    }

	void OnDisable () {
		EventOnDisable.Invoke ();
		//Debug.Log ("ON DISABLE");
	}

	public void TriggerEventA(){
		EventA.Invoke ();
	}

    public void TriggerEventB()
    {
        EventB.Invoke();
    }

    public void TriggerEventAfterTime(){
		StartCoroutine (DelayTime());
	}

    public void DestroyObject(){
        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(enter_tag))
        {
            EventOnEnter.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(exit_tag))
        {
            EventOnExit.Invoke();
        }
    }

    IEnumerator DelayTime(){
		yield return new WaitForSeconds (TimeValue);
		EventAfterTime.Invoke ();
        //Debug.Log("DELAYED EVENT: " + this.gameObject.name);
	}

    public void StopTimer()
    {
        StopCoroutine(DelayTime());
    }

    public GameObject MissionPrefab;
    public RectTransform prefabParent;

    public void Instantiate()
    {

        GameObject obj = Instantiate(MissionPrefab);
        obj.transform.parent = prefabParent;

        RectTransform RT = obj.GetComponent<RectTransform>();
        RT.offsetMin = new Vector2(0, 0);
        RT.offsetMax = new Vector2(0, 0);
    }
}
