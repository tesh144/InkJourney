using UnityEngine;
using System.Collections;
using UnityEngine.UI;


public class LoadMap : MonoBehaviour {

	// this is the url to the google maps API call;
	string url; 
	Image image;
	// these are the world's absolute latitude and longitude of the position of the user.
	public float lat=0;
	public float lon=0; 

	//these are the world's absolute latitude and longitude of the center of the map.
	public float latC=0;
	public float lonC=0; 

	// distance thershold to recenter map
	public float thDistanceUpdate;

	//time that goes by before recalling the update API google maps
	public float updateFloatRate;
	//mapZoom
	public int zoom;
	float mapScale;
	//evolving time value
	float elapsed;
	
    // size of the map wxh
	public int width, height;

    //API key
    public string key = "YOUR_API_KEY";


    // debug DPI
    public Text dpiText;

	// debug distance
	public Text distanceText;


	// this is an arraw that contains the x y and total distance between the actual position and the map center
	float[] distances;

	// image of the cursor
	public RectTransform cursorImage;

	// show variables
	public float Xdist,Ydist;


	void Start()
	{
		dpiText.text = ""+Screen.dpi;

		distances = new float[3];


		// map ratio database  dots map : dots reality

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

		//  final latitude update:
		latC = lat;
		lonC = lon;


		url=callGoogMapApi( latC,  lonC,  zoom,  width,  height, key );


		//start download and wait to finish
		WWW www = new WWW(url);

		yield return www;


		// assign texture
		image=GetComponent<UnityEngine.UI.Image>();
		image.material.mainTexture = www.texture;
		image.enabled = false;

		//this is used to re-enable the canvas again, which is necessary to update the map
		Invoke ("reEnableIm",0.0001f);
		Debug.Log ("MapUpdate");


	}



	public string callGoogMapApi(float latt, float lonn, int ZM, int wid, int height, string key )
	{

		string urlp="https://maps.googleapis.com/maps/api/staticmap?center=";
		urlp+= latt.ToString("##0.0000000") + "," + lonn.ToString("##0.0000000");
		urlp += "&zoom=" + ZM + "&size=" + width + "x" + height + "&maptype=" + "roadmap";
        urlp += "&key="+key;
        Debug.Log(urlp);

		return urlp;
	}



	void FixedUpdate()
	{
		elapsed += Time.fixedDeltaTime;
		distances = calculateDistance (lat, lon, latC, lonC);

		// use if you want a time condition to update map
		if(elapsed>updateFloatRate)
		{
			StartCoroutine (reCalculateMap ());
			elapsed = 0;

		}
		//else move the cursor if it is in bounds
		else 
		{
			// parameter to conver from pixels to meters... it may change with zoom
			mapScale= 156543.04f* Mathf.Cos(lat) / (2 ^ zoom);//  meters/pixel 

			Xdist = distances [0]/mapScale;
			Ydist = distances [1]/mapScale;

			//move cursor the given pixels
			cursorImage.localPosition=new Vector3(-(int)Xdist,-(int)Ydist,0);
			Debug.Log ("update cursor");

		}

		/* use if you want a distance condition to update map, using a specific bound
		if (distances [2] > thDistanceUpdate) 
		{
		}*/




	}


	void reEnableIm()
	{
		image.enabled = true;
	}



	float[] calculateDistance(float Lat1, float Lon1, float Lat2, float Lon2)
	{

		//earth radi
		float R = 6371.0e3f; // metres
		//angles in radians
		float φ1 = Lat1*Mathf.Deg2Rad;
		float φ2 = Lat2*Mathf.Deg2Rad;
		float Δφ = (Lat2-Lat1)*Mathf.Deg2Rad;
		float Δλ = (Lon2-Lon1)*Mathf.Deg2Rad;

		// equations to get distance 
		float a = Mathf.Sin(Δφ/2) * Mathf.Sin(Δφ/2) +
			Mathf.Cos(φ1) * Mathf.Cos(φ2) *
			Mathf.Sin(Δλ/2) * Mathf.Sin(Δλ/2);
		float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1-a));

		//final distance
		float[] d = new float[3];

		d [1] = R * Δφ;
		d [0] =	R*Δλ;
		d [2]=R * c;

		distanceText.text=""+d[2].ToString("##.0")+"m";



		return d;
	}

}


