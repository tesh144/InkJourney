using System.Runtime.InteropServices;

public static class NativeCameraZoom
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _SetZoomFactor(float factor, bool front);
    [DllImport("__Internal")] private static extern float _GetMinZoomFactor(bool front);
    [DllImport("__Internal")] private static extern float _GetMaxZoomFactor(bool front);
#endif

    public static void SetZoom(float factor, bool front)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _SetZoomFactor(factor, front);
#endif
    }

    public static float GetMinZoom(bool front)
    {
#if UNITY_IOS && !UNITY_EDITOR
        return _GetMinZoomFactor(front);
#else
        return 1f;
#endif
    }

    public static float GetMaxZoom(bool front)
    {
#if UNITY_IOS && !UNITY_EDITOR
        return _GetMaxZoomFactor(front);
#else
        return 1f;
#endif
    }
}
