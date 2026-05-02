using System.Collections;
using UnityEngine;
using System;

public class LocationUpdateManager : MonoBehaviour
{
    [Header("Update Settings")]
    public float updateDistanceThreshold = 50f; // Meters
    public float updateTimeThreshold = 30f; // Seconds

    private Vector2 lastPosition;
    private float lastUpdateTime;

    public event Action OnLocationUpdate; // Event triggered when an update occurs
    public static LocationUpdateManager instance;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        if (GPSManager.Instance != null)
            lastPosition = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
        else
            lastPosition = Vector2.zero;

        lastUpdateTime = Time.time;
        StartCoroutine(CheckLocationUpdates());
    }

    IEnumerator CheckLocationUpdates()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f); // Check every second

            if (GPSManager.Instance != null)
            {
                Vector2 currentPosition = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
                if (currentPosition == Vector2.zero)
                    continue;

                if (lastPosition == Vector2.zero)
                {
                    lastPosition = currentPosition;
                    lastUpdateTime = Time.time;
                    continue;
                }

                float latM = (currentPosition.x - lastPosition.x) * 111320f;
                float lonM = (currentPosition.y - lastPosition.y) * (111320f * Mathf.Cos(currentPosition.x * Mathf.Deg2Rad));
                float distanceMoved = Mathf.Sqrt(latM * latM + lonM * lonM);
                float timeElapsed = Time.time - lastUpdateTime;

                // Trigger update if the user has moved enough OR enough time has passed
                if (distanceMoved > updateDistanceThreshold || timeElapsed > updateTimeThreshold)
                {
                    lastPosition = currentPosition;
                    lastUpdateTime = Time.time;
                    OnLocationUpdate?.Invoke(); // Trigger event
                }
            }
        }
    }

    // Public function for manual refresh
    public void ForceRefresh()
    {
        lastUpdateTime = Time.time;
        if (GPSManager.Instance != null)
            lastPosition = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
        OnLocationUpdate?.Invoke();
    }
}