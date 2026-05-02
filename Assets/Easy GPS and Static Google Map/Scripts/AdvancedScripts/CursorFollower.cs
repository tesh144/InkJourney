using UnityEngine;
using System.Collections;

public class CursorFollower : MonoBehaviour {

	// Use this for initialization
	public Transform cursor;

	void Start () 
	{
	
	}
	
	// Update is called once per frame
	void FixedUpdate () {

		Vector3 objective = new Vector3 (cursor.position [0], cursor.position [1], -350);
		transform.position = Vector3.Lerp (transform.position, objective, 0.1f);
	}
}
