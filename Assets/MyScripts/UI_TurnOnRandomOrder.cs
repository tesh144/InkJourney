using System.Collections;
using System.Collections.Generic; 
using UnityEngine;

public class UI_TurnOnRandomOrder : MonoBehaviour {

	public List<GameObject> Children;

	[Space]
	[Header("MODIFIERS")]
	public float AnimationTime;
	private float Delay;
	public bool turnOffInstead;
	public bool DoOnEnable;

	// Use this for initialization
	void OnEnable(){
		//starts on enable
		if (DoOnEnable) {
			TurnOnPixel ();
		}
	}

	void Start () {
	}

	void TurnOnPixel()
	{
		//Adds Children to the list
		collectChildren();

		//Sets delay per object based on overally time of animation
		Delay = (AnimationTime / Children.Count);

		if (!turnOffInstead) {
			HideEverything ();
			ShowUpEverythingWithDelay ();
		} else {
			HideEverythingWithDelay ();
		}

	}

	//collects children into list
	void collectChildren(){
			foreach (Transform child in transform) {
			Children.Add (child.gameObject);
		}
	}

	//Hides all objects
	void HideEverything() {
		foreach (GameObject _go in Children) {
			_go.SetActive (false);
		}
	}

	//Public Function - To be accessed externally: Show Everything with delay
	public void ShowUpEverythingWithDelay() {

		if (Children.Count <= 0) {
			collectChildren ();
			HideEverything ();
		}

		int index = 1;
		foreach (GameObject go in Children) {
			index++;
			StartCoroutine(ShowObjects (go, index));
		}
	}

	//Public Function - To be accessed externally: Hide Everything with delay
	public void HideEverythingWithDelay() {

		if (Children.Count <= 0) {
			collectChildren ();
		}

		int index = 1;
		foreach (GameObject go in Children) {
			index++;
			StartCoroutine(HideObjects (go, index));
		}
	}

	//Coroutine: Turns on objects with delay
	IEnumerator ShowObjects(GameObject _go, int _index){
		yield return new WaitForSeconds(Delay*_index);

		int MaxIndex = Children.Count;
		int RandomIndex = Random.Range(0,MaxIndex);
		GameObject RandomChild = Children[RandomIndex];

		RandomChild.SetActive (true);
		Children.RemoveAt(RandomIndex);
	}

	//Coroutine: Turns off objects with delay
	IEnumerator HideObjects(GameObject _go, int _index){
		yield return new WaitForSeconds(Delay*_index);

		int MaxIndex = Children.Count;
		int RandomIndex = Random.Range(0,MaxIndex);
		GameObject RandomChild = Children[RandomIndex];

		RandomChild.SetActive (false);
		Children.RemoveAt(RandomIndex);
	}
}
