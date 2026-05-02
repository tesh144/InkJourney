using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StoryPhotoManager : MonoBehaviour
{
    [Header("Story Panel — Photo Area")]
    public GameObject placeholderIcon;   // The camera icon shown before a photo is taken
    public GameObject placeholderText;   // The "Add Photo" text shown before a photo is taken
    public RawImage photoPreview;        // RawImage that shows the captured photo
    public GameObject editDeleteButtons; // Buttons parent (Edit + Delete)

    [Header("Camera Panel")]
    public GameObject cameraPanel;
    public RawImage cameraFeed;
    public GameObject switchCameraButton;
    public GameObject flashButton;      // FlashBG parent — hidden entirely on front camera / no torch
    public GameObject flashOnSelected;  // Flash child — shown when flash is ON, hidden when OFF

    [Header("Focus")]
    public RectTransform focusIndicator;

    [Header("Zoom Buttons")]
    public GameObject zoom05Button;
    public GameObject zoom10Button;
    public GameObject zoom20Button;
    public GameObject zoom05Selected;
    public GameObject zoom10Selected;
    public GameObject zoom20Selected;


    public Texture2D CapturedPhoto { get; private set; }

    private Texture2D _existingPhotoTexture;
    private WebCamTexture webCamTexture;
    private bool usingFrontCamera = false;
    private bool _flashOn = false;
    private Coroutine _focusIndicatorRoutine;

    private void Awake()
    {
        cameraPanel.SetActive(false);
        photoPreview.gameObject.SetActive(false);
        editDeleteButtons.SetActive(false);
        SetPlaceholderVisible(true);

    }

    // Called by LoadForEdit to display an already-uploaded photo without marking it as a new capture
    public void LoadExistingPhoto(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        StartCoroutine(LoadExistingPhotoRoutine(url));
    }

    private IEnumerator LoadExistingPhotoRoutine(string url)
    {
        using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;

            if (_existingPhotoTexture != null) Destroy(_existingPhotoTexture);
            _existingPhotoTexture = ((UnityEngine.Networking.DownloadHandlerTexture)req.downloadHandler).texture;
            ShowPreview(_existingPhotoTexture);
        }
    }

    // Assign to: Add Photo button OnClick + Edit button OnClick
    public void OpenCamera()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("[Camera] No camera devices found.");
            return;
        }

        if (switchCameraButton != null)
            switchCameraButton.SetActive(devices.Length >= 2);

        cameraPanel.SetActive(true);
        StartCamera(usingFrontCamera);
    }

    // Assign to: Switch Camera button OnClick
    public void SwitchCamera()
    {
        if (WebCamTexture.devices.Length < 2) return;

        usingFrontCamera = !usingFrontCamera;
        webCamTexture?.Stop();
        webCamTexture = null;
        StartCamera(usingFrontCamera);
    }

    private void LateUpdate()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            UpdateCameraFeedMaterial();
            HandleFocusTap();
        }
    }

    private void HandleFocusTap()
    {
        if (Input.touchCount != 1) return;
        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                cameraFeed.rectTransform, touch.position, null, out Vector2 local))
            return;

        Rect r = cameraFeed.rectTransform.rect;
        float u = Mathf.Clamp01((local.x - r.xMin) / r.width);
        float v = Mathf.Clamp01((local.y - r.yMin) / r.height);

        // AVFoundation: (0,0) top-left, flip v
        NativeCameraFocus.SetFocusPoint(u, 1f - v, usingFrontCamera);

        if (focusIndicator != null)
        {
            focusIndicator.anchoredPosition = local;
            if (_focusIndicatorRoutine != null) StopCoroutine(_focusIndicatorRoutine);
            _focusIndicatorRoutine = StartCoroutine(ShowFocusIndicator());
        }
    }

    private IEnumerator ShowFocusIndicator()
    {
        focusIndicator.gameObject.SetActive(true);
        yield return new WaitForSeconds(1f);
        focusIndicator.gameObject.SetActive(false);
    }

    private IEnumerator ApplyOrientationWhenReady()
    {
        while (!webCamTexture.didUpdateThisFrame || webCamTexture.width <= 16 || webCamTexture.height <= 16)
            yield return null;

        cameraFeed.rectTransform.localScale = Vector3.one;
        cameraFeed.uvRect = new Rect(0f, 0f, 1f, 1f);

        UpdateCameraFeedMaterial();
    }

    private void UpdateCameraFeedMaterial()
    {
        if (cameraFeed == null || cameraFeed.material == null || webCamTexture == null)
            return;

        int rotation = ((webCamTexture.videoRotationAngle % 360) + 360) % 360;
        bool verticallyMirrored = webCamTexture.videoVerticallyMirrored;

        // Map webcam rotation angle to shader rotation steps.
        // 0 = 0°, 1 = 90° CW, 2 = 180°, 3 = 270° CW
        int rotationSteps = 0;
        switch (rotation)
        {
            case 90:  rotationSteps = 1; break;
            case 180: rotationSteps = 2; break;
            case 270: rotationSteps = 3; break;
            default:  rotationSteps = 0; break;
        }

        float rectW = cameraFeed.rectTransform.rect.width;
        float rectH = cameraFeed.rectTransform.rect.height;
        if (rectW <= 1f || rectH <= 1f) return; // layout not ready yet

        float displayAspect    = rectW / rectH;
        float rawTextureAspect = (webCamTexture.height > 0) ? (float)webCamTexture.width / webCamTexture.height : 1f;

        // After a 90°/270° UV rotation the shader's X and Y axes are swapped relative
        // to the display.  Swapping the two aspect arguments here makes the AspectFill
        // crop land on the correct axis without any shader change.
        float shaderTexAspect, shaderDispAspect;
        if (rotationSteps == 1 || rotationSteps == 3)
        {
            shaderTexAspect  = displayAspect;               // rect W/H
            shaderDispAspect = 1f / rawTextureAspect;       // tex H/W  (e.g. 1080/1920)
        }
        else
        {
            shaderTexAspect  = rawTextureAspect;            // tex W/H  (e.g. 1920/1080)
            shaderDispAspect = displayAspect;               // rect W/H
        }

        float mirrorX = 1f;
        float mirrorY = usingFrontCamera ? 1f : 0f;

        cameraFeed.material.SetFloat("_RotationSteps", rotationSteps);
        cameraFeed.material.SetFloat("_MirrorX", mirrorX);
        cameraFeed.material.SetFloat("_MirrorY", mirrorY);
        cameraFeed.material.SetFloat("_DisplayAspect", shaderDispAspect);
        cameraFeed.material.SetFloat("_TextureAspect", shaderTexAspect);
    }

    private void StartCamera(bool preferFront)
    {
        string deviceName = WebCamTexture.devices[0].name;
        bool foundFront = WebCamTexture.devices[0].isFrontFacing;
        foreach (var d in WebCamTexture.devices)
        {
            if (d.isFrontFacing == preferFront)
            {
                deviceName = d.name;
                foundFront = d.isFrontFacing;
                break;
            }
        }
        usingFrontCamera = foundFront;

        // Torch only works on back camera — hide button when using front
        NativeCameraFlash.SetTorch(false);
        bool hasFlash = !usingFrontCamera && NativeCameraFlash.HasTorch();
        if (flashButton != null) flashButton.SetActive(hasFlash);
        SetFlash(false);

        cameraFeed.rectTransform.localEulerAngles = Vector3.zero;

        float minZoom = NativeCameraZoom.GetMinZoom(usingFrontCamera);
        float maxZoom = NativeCameraZoom.GetMaxZoom(usingFrontCamera);
        if (zoom05Button != null) zoom05Button.SetActive(minZoom <= 0.6f);
        if (zoom10Button != null) zoom10Button.SetActive(true);
        if (zoom20Button != null) zoom20Button.SetActive(maxZoom >= 2f);
        SetZoom(1f);

        webCamTexture = new WebCamTexture(deviceName, 1280, 720);
        cameraFeed.texture = webCamTexture;
        webCamTexture.Play();
        StartCoroutine(ApplyOrientationWhenReady());
    }

    public void OnZoom05() => SetZoom(0.5f);
    public void OnZoom10() => SetZoom(1.0f);
    public void OnZoom20() => SetZoom(2.0f);

    private void SetZoom(float factor)
    {
        NativeCameraZoom.SetZoom(factor, usingFrontCamera);
        bool is05 = Mathf.Approximately(factor, 0.5f);
        bool is10 = Mathf.Approximately(factor, 1.0f);
        bool is20 = Mathf.Approximately(factor, 2.0f);
        if (zoom05Selected != null) zoom05Selected.SetActive(is05);
        if (zoom10Selected != null) zoom10Selected.SetActive(is10);
        if (zoom20Selected != null) zoom20Selected.SetActive(is20);
    }

    // Assign to: Flash on/off buttons OnClick — stores preference; torch fires only during capture
    public void SetFlash(bool on)
    {
        _flashOn = on;
        if (flashOnSelected != null) flashOnSelected.SetActive(on);
    }

    // Assign to: Capture button OnClick
    public void CapturePhoto()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying) return;
        StartCoroutine(CaptureRoutine());
    }

    private IEnumerator CaptureRoutine()
    {
        if (_flashOn)
        {
            NativeCameraFlash.SetTorch(true);
            yield return new WaitForSeconds(0.4f);
        }

        Texture2D raw = new Texture2D(webCamTexture.width, webCamTexture.height);
        raw.SetPixels(webCamTexture.GetPixels());
        raw.Apply();

        if (_flashOn)
        {
            yield return new WaitForSeconds(0.2f);
            NativeCameraFlash.SetTorch(false);
        }

        Texture2D oriented = ApplyOrientation(raw, webCamTexture.videoRotationAngle, usingFrontCamera);
        oriented = FlipHorizontal(oriented);

        // Crop to match what the live feed shows — use the feed rect ratio only.
        float feedW = cameraFeed.rectTransform.rect.width;
        float feedH = cameraFeed.rectTransform.rect.height;
        float displayRatio = feedW / feedH;

        CapturedPhoto = CropToAspect(oriented, displayRatio);
        if (CapturedPhoto != oriented) Destroy(oriented);

        CloseCamera();
        ShowPreview(CapturedPhoto);
    }

    // Applies the same orientation used by the live feed so the captured image matches
    private Texture2D ApplyOrientation(Texture2D src, int rotationAngle, bool isFrontCamera)
    {
        int cwSteps = (rotationAngle % 360) / 90;
        for (int i = 0; i < cwSteps; i++)
            src = RotateCW90(src);
        if (!isFrontCamera)
            src = FlipHorizontal(src);
        return src;
    }

    private Texture2D FlipHorizontal(Texture2D src)
    {
        int w = src.width, h = src.height;
        Color32[] srcPixels = src.GetPixels32();
        Color32[] dstPixels = new Color32[w * h];
        Texture2D dst = new Texture2D(w, h);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dstPixels[y * w + (w - 1 - x)] = srcPixels[y * w + x];

        dst.SetPixels32(dstPixels);
        dst.Apply();
        Destroy(src);
        return dst;
    }

    // 90° clockwise rotation — returns a new texture with dimensions swapped
    private Texture2D RotateCW90(Texture2D src)
    {
        int w = src.width, h = src.height;
        Color32[] srcPixels = src.GetPixels32();
        Color32[] dstPixels = new Color32[w * h];
        Texture2D dst = new Texture2D(h, w);

        for (int newX = 0; newX < h; newX++)
            for (int newY = 0; newY < w; newY++)
                dstPixels[newY * h + newX] = srcPixels[newX * w + (w - 1 - newY)];

        dst.SetPixels32(dstPixels);
        dst.Apply();
        Destroy(src);
        return dst;
    }

    private Texture2D FlipVertical(Texture2D src)
    {
        int w = src.width, h = src.height;
        Color32[] srcPixels = src.GetPixels32();
        Color32[] dstPixels = new Color32[w * h];
        Texture2D dst = new Texture2D(w, h);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dstPixels[(h - 1 - y) * w + x] = srcPixels[y * w + x];

        dst.SetPixels32(dstPixels);
        dst.Apply();
        Destroy(src);
        return dst;
    }

    // Centre-crops a Texture2D to the given aspect ratio (width / height)
    private Texture2D CropToAspect(Texture2D source, float targetAspect)
    {
        if (targetAspect <= 0f) return source;

        int srcW = source.width;
        int srcH = source.height;
        float srcAspect = (float)srcW / srcH;

        int cropW, cropH;
        if (srcAspect > targetAspect)
        {
            cropH = srcH;
            cropW = Mathf.RoundToInt(srcH * targetAspect);
        }
        else
        {
            cropW = srcW;
            cropH = Mathf.RoundToInt(srcW / targetAspect);
        }

        cropW = Mathf.Clamp(cropW, 1, srcW);
        cropH = Mathf.Clamp(cropH, 1, srcH);

        int x = (srcW - cropW) / 2;
        int y = (srcH - cropH) / 2;

        Color[] pixels = source.GetPixels(x, y, cropW, cropH);
        Texture2D result = new Texture2D(cropW, cropH);
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    // Assign to: Backdrop button OnClick
    public void CloseCamera()
    {
        SetFlash(false);
        NativeCameraFlash.SetTorch(false);
        webCamTexture?.Stop();
        cameraPanel.SetActive(false);
    }

    // Assign to: Delete button OnClick
    public void DeletePhoto()
    {
        CapturedPhoto = null;
        photoPreview.texture = null;
        photoPreview.gameObject.SetActive(false);
        editDeleteButtons.SetActive(false);
        SetPlaceholderVisible(true);
    }

    // Call this when the story panel is closed/reset to clear state
    public void Reset()
    {
        CloseCamera();
        if (CapturedPhoto != null) Destroy(CapturedPhoto);
        if (_existingPhotoTexture != null) { Destroy(_existingPhotoTexture); _existingPhotoTexture = null; }
        DeletePhoto();
    }

    private void ShowPreview(Texture2D photo)
    {
        photoPreview.texture = photo;
        photoPreview.gameObject.SetActive(true);
        editDeleteButtons.SetActive(true);
        SetPlaceholderVisible(false);
    }

    private void SetPlaceholderVisible(bool visible)
    {
        if (placeholderIcon != null) placeholderIcon.SetActive(visible);
        if (placeholderText != null) placeholderText.SetActive(visible);
    }

    private void OnDestroy()
    {
        webCamTexture?.Stop();
    }
}