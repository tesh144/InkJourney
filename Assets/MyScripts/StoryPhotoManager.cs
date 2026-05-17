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


    public event System.Action onPhotoChanged;

    public Texture2D CapturedPhoto { get; private set; }
    public Texture2D ActivePhotoTexture => CapturedPhoto ?? _existingPhotoTexture;

    private Texture2D _existingPhotoTexture;
    private bool _existingPhotoOwned;
    private WebCamTexture webCamTexture;
    private bool usingFrontCamera = false;
    private bool _flashOn = false;
    private Coroutine _focusIndicatorRoutine;

    // Cached shader property IDs — avoids string hashing every frame
    private static readonly int PropRotationSteps = Shader.PropertyToID("_RotationSteps");
    private static readonly int PropMirrorX       = Shader.PropertyToID("_MirrorX");
    private static readonly int PropMirrorY       = Shader.PropertyToID("_MirrorY");
    private static readonly int PropDisplayAspect = Shader.PropertyToID("_DisplayAspect");
    private static readonly int PropTextureAspect = Shader.PropertyToID("_TextureAspect");

    private int   _currentRotationSteps = 0;
    private float _rawTextureAspect     = 1f;

    // Tracks last-applied material values so we only call SetFloat when something changes
    private int   _matRotationSteps  = -1;
    private float _matMirrorX        = -1f;
    private float _matMirrorY        = -1f;
    private float _matDisplayAspect  = -1f;
    private float _matTextureAspect  = -1f;

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

        var cached = PhotoAsset.GetCached(url);
        if (cached != null)
        {
            if (_existingPhotoTexture != null && _existingPhotoOwned) Destroy(_existingPhotoTexture);
            _existingPhotoTexture = cached;
            _existingPhotoOwned   = false;
            ShowPreview(_existingPhotoTexture);
            return;
        }

        StartCoroutine(LoadExistingPhotoRoutine(url));
    }

    private IEnumerator LoadExistingPhotoRoutine(string url)
    {
        using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;

            if (_existingPhotoTexture != null && _existingPhotoOwned) Destroy(_existingPhotoTexture);
            _existingPhotoTexture = ((UnityEngine.Networking.DownloadHandlerTexture)req.downloadHandler).texture;
            _existingPhotoOwned   = true;
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
        if (webCamTexture == null || !webCamTexture.isPlaying) return;
        HandleFocusTap();
        // Only recalculate material when the camera has delivered a new frame
        if (webCamTexture.didUpdateThisFrame)
            UpdateCameraFeedMaterial();
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

        // Only push to GPU when values have actually changed
        var mat = cameraFeed.material;
        if (rotationSteps  != _matRotationSteps)  { mat.SetFloat(PropRotationSteps, rotationSteps);   _matRotationSteps  = rotationSteps; }
        if (mirrorX        != _matMirrorX)        { mat.SetFloat(PropMirrorX,       mirrorX);         _matMirrorX        = mirrorX; }
        if (mirrorY        != _matMirrorY)        { mat.SetFloat(PropMirrorY,       mirrorY);         _matMirrorY        = mirrorY; }
        if (!Mathf.Approximately(shaderDispAspect, _matDisplayAspect)) { mat.SetFloat(PropDisplayAspect, shaderDispAspect); _matDisplayAspect = shaderDispAspect; }
        if (!Mathf.Approximately(shaderTexAspect,  _matTextureAspect)) { mat.SetFloat(PropTextureAspect,  shaderTexAspect);  _matTextureAspect  = shaderTexAspect; }

        _currentRotationSteps = rotationSteps;
        _rawTextureAspect     = rawTextureAspect;
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

        webCamTexture = new WebCamTexture(deviceName, 960, 540);
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

        // GPU blit: reuse the camera shader (already has correct rotation/mirror values)
        // but override the display aspect to exactly 2:1 so the crop lands correctly.
        int outW = webCamTexture.width;
        int outH = outW / 2;
        var rt = RenderTexture.GetTemporary(outW, outH, 0, RenderTextureFormat.ARGB32);

        var capMat = new Material(cameraFeed.material);
        float captureDispAspect, captureTexAspect;
        if (_currentRotationSteps == 1 || _currentRotationSteps == 3)
        {
            captureTexAspect  = 2f;
            captureDispAspect = 1f / _rawTextureAspect;
        }
        else
        {
            captureTexAspect  = _rawTextureAspect;
            captureDispAspect = 2f;
        }
        capMat.SetFloat(PropDisplayAspect, captureDispAspect);
        capMat.SetFloat(PropTextureAspect, captureTexAspect);

        Graphics.Blit(webCamTexture, rt, capMat);
        Destroy(capMat);

        CapturedPhoto = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        var prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        CapturedPhoto.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
        CapturedPhoto.Apply();
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);

        if (_flashOn)
        {
            yield return new WaitForSeconds(0.2f);
            NativeCameraFlash.SetTorch(false);
        }

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
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }
        // Reset cached material state so it's recalculated fresh next time
        _matRotationSteps = -1;
        if (cameraPanel != null) cameraPanel.SetActive(false);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            if (webCamTexture != null && webCamTexture.isPlaying)
                webCamTexture.Pause();
            NativeCameraFlash.SetTorch(false);
        }
        else if (webCamTexture != null && !webCamTexture.isPlaying && cameraPanel != null && cameraPanel.activeSelf)
        {
            webCamTexture.Play();
        }
    }

    // Assign to: Delete button OnClick
    public void DeletePhoto()
    {
        CapturedPhoto = null;
        photoPreview.texture = null;
        photoPreview.gameObject.SetActive(false);
        editDeleteButtons.SetActive(false);
        SetPlaceholderVisible(true);
        onPhotoChanged?.Invoke();
    }

    // Call this when the story panel is closed/reset to clear state
    public void Reset()
    {
        CloseCamera();
        if (CapturedPhoto != null) Destroy(CapturedPhoto);
        if (_existingPhotoTexture != null && _existingPhotoOwned) Destroy(_existingPhotoTexture);
        _existingPhotoTexture = null;
        _existingPhotoOwned   = false;
        DeletePhoto();
    }

    private void ShowPreview(Texture2D photo)
    {
        photoPreview.gameObject.SetActive(true);
        ApplyPhotoToRawImage(photo, photoPreview);
        editDeleteButtons.SetActive(true);
        SetPlaceholderVisible(false);
        onPhotoChanged?.Invoke();
    }

    // Single canonical method for displaying a photo — use this everywhere.
    // If the RawImage has an AspectRatioFitter, it will auto-size to the texture with zero crop.
    // Otherwise it center-fills the container with minimal crop.
    public static void ApplyPhotoToRawImage(Texture2D tex, RawImage img)
    {
        if (img == null || tex == null) return;
        img.texture = tex;
        img.color   = Color.white;
        var fitter = img.GetComponent<AspectRatioFitter>();
        if (fitter != null)
        {
            fitter.aspectRatio = (float)tex.width / tex.height;
            img.uvRect = new Rect(0f, 0f, 1f, 1f);
        }
        else
        {
            img.uvRect = CenterFillRect(tex, img.rectTransform);
        }
    }

    // Center-fill crop: use when you need the Rect value separately.
    public static Rect CenterFillRect(Texture2D tex, RectTransform container)
    {
        if (tex == null) return new Rect(0f, 0f, 1f, 1f);
        Rect r = container.rect;
        float containerAspect = (r.width > 1f && r.height > 1f) ? r.width / r.height : 2f;
        float texAspect = (float)tex.width / tex.height;
        if (texAspect > containerAspect)
        {
            float u = containerAspect / texAspect;
            return new Rect((1f - u) * 0.5f, 0f, u, 1f);
        }
        else
        {
            float v = texAspect / containerAspect;
            return new Rect(0f, (1f - v) * 0.5f, 1f, v);
        }
    }

    private void SetPlaceholderVisible(bool visible)
    {
        if (placeholderIcon != null) placeholderIcon.SetActive(visible);
        if (placeholderText != null) placeholderText.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }
    }
}