using System.Runtime.InteropServices;

public static class NativeCameraFlash
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern bool _HasTorch();
    [DllImport("__Internal")] private static extern void _SetTorchMode(bool on);
#endif

    public static bool HasTorch()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return _HasTorch();
#else
        return false;
#endif
    }

    public static void SetTorch(bool on)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _SetTorchMode(on);
#endif
    }
}
