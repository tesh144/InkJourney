
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using System;


public class PortionMap : MonoBehaviour {

	// this is the url to the google maps API call;
	string url; 
	// latitude and longitude
	public float latC,lonC;
	// zoom factors
	public int zoom=16;
	float[] zoomScale;
	// height and width of map
	public int width, height;
	Image image;
	float elapsed;
	public float shiftX=600; //px
	public float shiftY=0; //px

	public float  Px2Mt ;
	float R = 6378137;

    //key
    public string key = "YOUR_API_KEY";

    void Start()
	{
		plotMap ();

	}


	// this function obtains the map static image from google Maps
	IEnumerator reCalculateMap() 
	{

		/*basic url structure:

			https://maps.googleapis.com/maps/api/staticmap?center=
			lat,lon
			&zoom=13&size=600x300&maptype=roadmap
			&markers=color:blue%7Clabel:S%7C40.702147,-74.015794

		*/


		url=callGoogMapApi(latC, lonC, zoom, width, height);

		Debug.Log(url);

		//start download and wait to finish
		WWW www = new WWW(url);

		yield return www;


		// assign texture
		image.material.mainTexture = www.texture;
		image.enabled = false;

		//this is used to re-enable the canvas again, which is necessary to update the map
		Invoke ("reEnableIm",0.0001f);
		Debug.Log ("MapUpdate");


	}


	public string callGoogMapApi(float latt, float lonn, int ZM, int wid, int height )
	{

		string urlp="https://maps.googleapis.com/maps/api/staticmap?center=";
		urlp+= latt.ToString("##0.0000000") + "," + lonn.ToString("##0.0000000");
		urlp += "&zoom=" + ZM + "&size=" + width + "x" + height + "&maptype=" + "roadmap";
        urlp += "&key=" + key;
        Debug.Log(urlp);

        return urlp;
	}




	public void plotMap()
	{
		// map ratio database  dots map : dots reality
		image = GetComponent<UnityEngine.UI.Image> ();
		image.material = new Material (Shader.Find ("Unlit/Texture"));


		Px2Mt = 156543.04f * Mathf.Cos (latC * Mathf.PI / 180) / (Mathf.Pow (2, zoom)); // meters /pixels


		// -0.0406
		lonC += (shiftX) * Px2Mt / R * 180 / (Mathf.PI);
		latC += (shiftY) * Px2Mt / R * 180 / (Mathf.PI);



		StartCoroutine (reCalculateMap ());
	}




	void reEnableIm()
	{
		image.enabled = true;
	}
	
	// Update is called once per frame
	void FixedUpdate () {

		/*elapsed += Time.fixedDeltaTime;
		if (elapsed > 2) {
			StartCoroutine (reCalculateMap ());
			elapsed = 0;
		}*/
	}



	void OnCollisionEnter(Collision col)
	{
		if (col.gameObject.tag == "cursor") {

			StartCoroutine (changeCollider (col));

		} 

	}

	IEnumerator changeCollider(Collision col)
	{
		yield return new WaitForSeconds (0.5f);
		//perform map actions

		LoadMapADV mapSc = col.gameObject.GetComponent<LoadMapADV> ();
		mapSc.lastCollider = gameObject.GetComponent<Collider> ();

	}

	void OnCollisionExit(Collision col)
	{
		if (col.gameObject.tag == "cursor") 
		{

			//perform map actions
			LoadMapADV mapSc = col.gameObject.GetComponent<LoadMapADV> ();
			mapSc.addMapPortion ();

		}
			
	}

}









