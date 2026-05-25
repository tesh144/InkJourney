using System.Collections.Generic;
using UnityEngine;

public class PhotoShuffle : MonoBehaviour
{
    public PhotoAsset photoAssetPrefab;

    [Header("Rotation")]
    public float rotationMin = -4f;
    public float rotationMax =  4f;

    [Header("Stagger")]
    public float perCardDelay = 0.2f;

    public void SpawnPhotos(List<string> photoUrls)
    {
        Clear();
        for (int i = photoUrls.Count - 1; i >= 0; i--)
        {
            PhotoAsset asset = Instantiate(photoAssetPrefab, transform);
            asset.Initialise(photoUrls[i], rotationMin, rotationMax, extraDelay: i * perCardDelay);
        }
    }

    public void AddPhoto(string url)
    {
        if (string.IsNullOrEmpty(url) || photoAssetPrefab == null) return;
        var asset = Instantiate(photoAssetPrefab, transform);
        asset.transform.SetAsFirstSibling();
        asset.Initialise(url, rotationMin, rotationMax, extraDelay: 0f);
    }

    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
    }

    // Wire this to a button — cycles the top card to the back of the stack.
    public void Shuffle()
    {
        if (transform.childCount < 2) return;

        Transform bottom = transform.GetChild(0);
        PhotoAsset asset  = bottom.GetComponent<PhotoAsset>();

        bottom.gameObject.SetActive(false);
        bottom.SetAsLastSibling();
        bottom.gameObject.SetActive(true);

        asset?.PlayShuffle();
    }
}
