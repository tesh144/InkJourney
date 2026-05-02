using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;

public class SearchLocation : MonoBehaviour
{
    public static SearchLocation instance;
    public float longitude;
    public float latitude;

    private void Awake()
    {
        instance = this;
    }

    public void GetLongAndLat(float lon, float lat)
    {
        longitude = lon; latitude = lat;
    }

    public void SearchWithPosition()
    {
        SearchOnGoogleMaps(longitude, latitude);
    }

    public static void SearchOnGoogleMaps(float latitude, float longitude)
    {
        // Construct the URL with the latitude and longitude
        string url = $"https://www.google.com/maps?q={latitude},{longitude}";

        // Open the URL in the default web browser
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
