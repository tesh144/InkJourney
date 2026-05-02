using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class GPSupdate : MonoBehaviour
{

	public LoadMap mapScript;  
	public Text debugText;
	public float updateGPStime;
	float elapsed;


	void Start()
	{
		StartCoroutine (checkGPS ());
	}


	void FixedUpdate()
	{
		elapsed += Time.fixedDeltaTime;

		if (elapsed > updateGPStime) {
			checkGPS ();

			elapsed = 0;
		}
	}



	// this function checks the GPS status andupdates the player's position
	IEnumerator checkGPS()
	{
		// Wait for permission screen before starting location
		while (LocationPermissionScreen.instance != null && !LocationPermissionScreen.LocationGranted)
			yield return new WaitForSeconds(0.5f);

		// First, check if user has location service enabled
		if (!Input.location.isEnabledByUser)
			yield break;

		debugText.text="startingGPS";

		// Start service before querying location
		Input.location.Start();

		// Wait until service initializes
		int maxWait = 20;
		while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
		{
			yield return new WaitForSeconds(1);
			maxWait--;

			debugText.text="startingGPS:"+maxWait;
		}

		// Service didn't initialize in 20 seconds
		if (maxWait < 1)
		{
			debugText.text="Timed out";
			yield break;
		}

		// Connection has failed
		if (Input.location.status == LocationServiceStatus.Failed)
		{
			debugText.text="Unable to determine device location";
			yield break;
		}
		else
		{
			// Access granted and location value could be retrieved
			debugText.text= ("Location: " + Input.location.lastData.latitude + " " + Input.location.lastData.longitude + " " + Input.location.lastData.altitude + " " + Input.location.lastData.horizontalAccuracy + " " + Input.location.lastData.timestamp);

			// set values of latitude
			mapScript.lat=Input.location.lastData.latitude;
			mapScript.lon = Input.location.lastData.longitude;
		
		}

		debugText.text="GPSupdated";

	}



	void OnDestroy()
	{
		// Stop service if there is no need to query location updates continuously
		Input.location.Stop();
	}


	void OnDisable()
	{
		// Stop service if there is no need to query location updates continuously
		Input.location.Stop();
	}
}