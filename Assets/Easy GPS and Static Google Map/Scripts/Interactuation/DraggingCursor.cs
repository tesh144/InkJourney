using UnityEngine;
using System.Collections;

public class DraggingCursor : MonoBehaviour {

	// Use this for initialization
	//this is used to store the presed point to study the potential drag.
	Vector3 potentialDragPos;
	// this is the movement speed of the screen
	public float speed=0.01f;
	public Transform cursor;

	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	public void potentialDrag()
	{
		potentialDragPos=Input.mousePosition;
	}

	public void updateDragPos()
	{
		//Debug.Log ("drag");
		cursor.position += (Input.mousePosition-potentialDragPos)*speed;
	}
}
