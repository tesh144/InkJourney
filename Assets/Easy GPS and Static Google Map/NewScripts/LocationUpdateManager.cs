using System.Collections;
using UnityEngine;
using System;

public class LocationUpdateManager : MonoBehaviour
{
    [Header("Update Settings")]
    public float updateDistanceThreshold = 50f;

    private Vector2 lastPosition;

    public event Action OnLocationUpdate;
    public static LocationUpdateManager instance;

    private void Awake() => instance = this;

    void Start()
    {
        lastPosition = GPSManager.Instance != null
            ? new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude)
            : Vector2.zero;

        StartCoroutine(CheckLocationUpdates());
    }

    IEnumerator CheckLocationUpdates()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            if (GPSManager.Instance == null) continue;

            Vector2 current = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
            if (current == Vector2.zero) continue;

            if (lastPosition == Vector2.zero) { lastPosition = current; continue; }

            float latM = (current.x - lastPosition.x) * 111320f;
            float lonM = (current.y - lastPosition.y) * (111320f * Mathf.Cos(current.x * Mathf.Deg2Rad));

            if (Mathf.Sqrt(latM * latM + lonM * lonM) > updateDistanceThreshold)
            {
                lastPosition = current;
                OnLocationUpdate?.Invoke();
            }
        }
    }

    public void ForceRefresh()
    {
        if (GPSManager.Instance != null)
            lastPosition = new Vector2(GPSManager.Instance.latitude, GPSManager.Instance.longitude);
        OnLocationUpdate?.Invoke();
    }
}
