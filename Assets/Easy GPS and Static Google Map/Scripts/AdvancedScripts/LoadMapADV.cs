using UnityEngine;
using System.Collections;
using UnityEngine.UI;


public class LoadMapADV : MonoBehaviour {

	// this is the tansform of the user's marker;
	public Transform cursor;
	// this is the prefab map that is added in case that the player moves to a corner of the actual map
	public GameObject prefabMap;
	// the las collider where the cursor is placed at
	public Collider lastCollider;
	// map container for adding new maps portions
	public Transform mapContainer;
	// show the distance to the latest portion map center
	public Vector3 diff;
	// this is the zoom of all the maps
	public int zoom=19;


	void Start()
	{
		lastCollider = null;
		//createPortionMap(600,0);

	}


	void FixedUpdate()
	{

	}



	// create a new portion map
	void createPortionMap(int X, int Y)
	{
		GameObject mapNew = GameObject.Instantiate (prefabMap, new Vector3 (600, 0, 0), Quaternion.Euler (0, 0, 0)) as GameObject;
		mapNew.transform.parent = mapContainer;
		mapNew.transform.localScale = new Vector3 (1, 1, 1);
		mapNew.transform.localPosition = new Vector3 (X, Y, 0);

		PortionMap mapScript = mapNew.GetComponent<PortionMap> ();
		mapScript.zoom = zoom;
		mapScript.shiftX = X+179*X/600;
		mapScript.shiftY = Y;

	}


	//this function verifies if the position is inside another collider (prevets creating a new map on top of another)
	bool check_collision(Vector3 pos)
	{

		GameObject[] mapsP = GameObject.FindGameObjectsWithTag ("map");

		bool a = false;

		for (int jj = 0; jj < mapsP.Length; jj++) 
		{
			
			a=a|mapsP[jj].GetComponent<Collider>().bounds.Contains (pos);
		}
			 
		return a;
		
	}


	// when a parameter is changed, the rebuildHoleMap must be called, such as
	//a change in zoom
	public void rebuildHoleMap()
	{
			GameObject[] mapsP = GameObject.FindGameObjectsWithTag ("map");
			bool a = false;

			for (int jj = 0; jj < mapsP.Length; jj++) 
			{
				PortionMap scMap = mapsP [jj].GetComponent<PortionMap> ();
				scMap.zoom = zoom;
				scMap.plotMap();
			}

	}

	public void changeZoom(int a)
	{
		zoom += a;

		if (zoom < 2) {
			zoom = 2;
		} else if (zoom > 20) 
		{
			zoom = 20;
		}

		rebuildHoleMap ();
	}

	// this is the part in which we create the 3 map modules 
	public void addMapPortion()
	{

		// crate a new map in the following cases:
		// 1- there is not another map at that position

		diff=cursor.localPosition-lastCollider.transform.position;

		int NewXMap=(int)lastCollider.transform.position [0];
		int NewYMap=(int)lastCollider.transform.position [1];


		// CREATE MAP x3 TOP-MIDDLE-Bottom  IN CASE THE PLAYER GOES RIGHT OR LEFT
		if (Mathf.Abs (diff [0]) > Mathf.Abs (diff [1])) {

			// create first map TOP
			if (check_collision (new Vector3 (NewXMap + Mathf.Sign (diff [0]) * 600, NewYMap+600, 0)) == false) {
				createPortionMap ((int)NewXMap + (int)Mathf.Sign (diff [0]) * 600, NewYMap+600);

			}
			// create first map MIDDLE
			if (check_collision (new Vector3 (NewXMap + Mathf.Sign (diff [0]) * 600, NewYMap, 0)) == false) {
				createPortionMap ((int)NewXMap + (int)Mathf.Sign (diff [0]) * 600, NewYMap);

			}
			// create first map DOWN
			if (check_collision (new Vector3 (NewXMap + Mathf.Sign (diff [0]) * 600, NewYMap-600, 0)) == false) {
				createPortionMap ((int)NewXMap + (int)Mathf.Sign (diff [0]) * 600, NewYMap-600);

			}


				
		} 
		// THIS IS IN THE CASE THE PLAYER GOES UP AND DOWN
		else 
		{
			// create first map LEFT
			if (check_collision (new Vector3 ((int)NewXMap-600, NewYMap +(int) Mathf.Sign (diff [0]) *600, 0)) == false) {
				createPortionMap ((int)NewXMap-600, (int)NewYMap+ (int)Mathf.Sign (diff [0]) *600);

			}
			// create first map MIDDLE
			if (check_collision (new Vector3 (NewXMap , NewYMap + (int)Mathf.Sign (diff [0]) *600, 0)) == false) {
				createPortionMap ((int)NewXMap, (int)NewYMap+ (int)Mathf.Sign (diff [0]) *600);

			}
			// create first map RIGHT
			if (check_collision (new Vector3 (NewXMap+600, NewYMap + Mathf.Sign (diff [0]) *600, 0)) == false) {
				createPortionMap ((int)NewXMap+600, (int)NewYMap+ (int)Mathf.Sign (diff [0]) *600);

			}

		}
			



		
	}

}
