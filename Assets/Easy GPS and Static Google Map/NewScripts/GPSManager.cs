using UnityEngine;
using System.Collections;
using UnityEngine.Android;
using System;

public class GPSManager : MonoBehaviour
{
    public static GPSManager Instance { get; private set; }
    public float latitude;
    public float longitude;
    public static event Action<float, float> OnPositionSampled;

    [Header("Sampling")]
    [Tooltip("How often GPSManager refreshes latitude/longitude values")]
    public float gpsSampleIntervalSeconds = 1f;

    [System.Serializable]
    public class MockLocation
    {
        public string name;
        public float latitude;
        public float longitude;
    }

    [Space]
    public MockLocation[] mockLocations = new MockLocation[]
    {
        // London (alphabetical)
        new MockLocation { name = "London - Balham",        latitude = 51.442340f, longitude = -0.153413f },
        new MockLocation { name = "London - Brixton",       latitude = 51.459767f, longitude = -0.116016f },
        new MockLocation { name = "London - Camden",        latitude = 51.539747f, longitude = -0.143013f },
        new MockLocation { name = "London - Camberwell",    latitude = 51.477741f, longitude = -0.103626f },
        new MockLocation { name = "London - Covent Garden", latitude = 51.512062f, longitude = -0.122939f },
        new MockLocation { name = "London - Greenwich",     latitude = 51.480285f, longitude = -0.006020f },
        new MockLocation { name = "London - Mitcham",       latitude = 51.408529f, longitude = -0.174202f },
        new MockLocation { name = "London - Notting Hill",  latitude = 51.512831f, longitude = -0.202448f },
        new MockLocation { name = "London - Peckham",       latitude = 51.469906f, longitude = -0.068213f },
        new MockLocation { name = "London - Poplar",        latitude = 51.515100f, longitude = -0.020700f },
        new MockLocation { name = "London - Shoreditch",    latitude = 51.518017f, longitude = -0.070949f },
        new MockLocation { name = "London - Soho",          latitude = 51.514245f, longitude = -0.131771f },
        new MockLocation { name = "London - South Bank",    latitude = 51.502500f, longitude = -0.119384f },
        new MockLocation { name = "London - Tooting",       latitude = 51.418986f, longitude = -0.164336f },
        new MockLocation { name = "London - Westminster",   latitude = 51.501010f, longitude = -0.141563f },
        new MockLocation { name = "London - Wimbledon",     latitude = 51.421906f, longitude = -0.207743f },
        // Manchester (alphabetical)
        new MockLocation { name = "Manchester - Altrincham",      latitude = 53.387769f, longitude = -2.349309f },
        new MockLocation { name = "Manchester - Castlefield",     latitude = 53.473763f, longitude = -2.248817f },
        new MockLocation { name = "Manchester - Northern Quarter", latitude = 53.484368f, longitude = -2.236054f },
        new MockLocation { name = "Manchester - Piccadilly",      latitude = 53.481043f, longitude = -2.226332f },
    };

    [Tooltip("Index into mockLocations array (0-19). Expand the list above to see all options.")]
    public int mockLocationIndex = 9; // Default: London - Poplar

    [Header("Editor Simulation")]
    public bool useEditorSimulation = true;
    [Tooltip("When enabled, simulated GPS moves every sample by speed + heading")]
    public bool editorSimulateMovement = false;
    public float editorSpeedMetersPerSecond = 1.6f;
    [Range(0f, 360f)] public float editorHeadingDegrees = 90f;
    public bool editorAddJitter = false;
    public float editorJitterMeters = 1.5f;
    [Tooltip("If enabled, uses manual override lat/lon instead of selected mock location")]
    public bool editorManualOverride = false;
    public float editorOverrideLatitude;
    public float editorOverrideLongitude;

    private float unity_latitude  => mockLocations[mockLocationIndex].latitude;
    private float unity_longitude => mockLocations[mockLocationIndex].longitude;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    public void Refresh()
    {
        StopAllCoroutines();
        StartCoroutine(InitializeAndTrackRoutine());
    }

    private void Start()
    {
        StartCoroutine(InitializeAndTrackRoutine());
    }

    private IEnumerator InitializeAndTrackRoutine()
    {
#if UNITY_EDITOR
        if (useEditorSimulation)
        {
            if (editorManualOverride)
            {
                latitude = editorOverrideLatitude;
                longitude = editorOverrideLongitude;
            }
            else
            {
                latitude = unity_latitude;
                longitude = unity_longitude;
            }

            PublishSample();

            while (true)
            {
                float dt = Mathf.Max(0.05f, gpsSampleIntervalSeconds);

                if (editorManualOverride)
                {
                    latitude = editorOverrideLatitude;
                    longitude = editorOverrideLongitude;
                }
                else if (editorSimulateMovement)
                {
                    float distanceMeters = Mathf.Max(0f, editorSpeedMetersPerSecond) * dt;
                    float headingRad = editorHeadingDegrees * Mathf.Deg2Rad;
                    float northMeters = Mathf.Cos(headingRad) * distanceMeters;
                    float eastMeters = Mathf.Sin(headingRad) * distanceMeters;
                    ApplyMetersOffset(northMeters, eastMeters);
                }

                if (editorAddJitter && editorJitterMeters > 0f)
                {
                    float jitterDistance = UnityEngine.Random.Range(0f, editorJitterMeters);
                    float jitterHeading = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    ApplyMetersOffset(Mathf.Cos(jitterHeading) * jitterDistance, Mathf.Sin(jitterHeading) * jitterDistance);
                }

                PublishSample();
                yield return new WaitForSeconds(dt);
            }
        }
#endif

        // Request location permission on Android
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitUntil(() => Permission.HasUserAuthorizedPermission(Permission.FineLocation));
        }
#endif

        // Wait for the permission screen to grant location before we call Start()
        while (LocationPermissionScreen.instance != null && !LocationPermissionScreen.LocationGranted)
            yield return new WaitForSeconds(0.5f);

        // Check if location services are enabled
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location services are disabled by the user. Please enable location services in settings.");
            yield break;
        }

        // Start GPS
        Input.location.Start();

        // Wait for GPS to initialize
        int maxWait = 10;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        // Check for timeout or failure
        if (maxWait <= 0 || Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogError("Failed to get GPS location. Please check your device's location settings.");
            yield break;
        }

        // Retrieve real GPS coordinates
        latitude = Input.location.lastData.latitude;
        longitude = Input.location.lastData.longitude;

        PublishSample();

        while (true)
        {
            if (Input.location.status == LocationServiceStatus.Running)
            {
                latitude = Input.location.lastData.latitude;
                longitude = Input.location.lastData.longitude;
                PublishSample();
            }

            yield return new WaitForSeconds(Mathf.Max(0.05f, gpsSampleIntervalSeconds));
        }
    }

    public void SetEditorLocation(float lat, float lon)
    {
        editorManualOverride = true;
        editorOverrideLatitude = lat;
        editorOverrideLongitude = lon;
        latitude = lat;
        longitude = lon;
        PublishSample();
    }

    public void NudgeEditorMeters(float northMeters, float eastMeters)
    {
        ApplyMetersOffset(northMeters, eastMeters);
        if (editorManualOverride)
        {
            editorOverrideLatitude = latitude;
            editorOverrideLongitude = longitude;
        }
        PublishSample();
    }

    private void PublishSample()
    {
        OnPositionSampled?.Invoke(latitude, longitude);
    }

    private void ApplyMetersOffset(float northMeters, float eastMeters)
    {
        float latDelta = northMeters / 111320f;
        float lonScale = 111320f * Mathf.Cos(latitude * Mathf.Deg2Rad);
        if (Mathf.Abs(lonScale) < 1f)
            lonScale = 1f;
        float lonDelta = eastMeters / lonScale;

        latitude += latDelta;
        longitude += lonDelta;
    }
}

