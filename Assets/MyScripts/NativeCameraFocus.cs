using System.Runtime.InteropServices;

public static class NativeCameraFocus
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _SetFocusPoint(float x, float y, bool front);
#endif

    public static void SetFocusPoint(float x, float y, bool front)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _SetFocusPoint(x, y, front);
#endif
    }
}
