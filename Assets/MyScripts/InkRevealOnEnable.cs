using UnityEngine;

/// <summary>
/// Attach to any panel/screen. Plays the ink clearing animation each time it's enabled.
/// Works with either InkTransition (shader) or SpriteSheetTransition.
/// </summary>
public class InkRevealOnEnable : MonoBehaviour
{
    private void OnEnable()
    {
        if (SpriteSheetTransition.instance != null)
            SpriteSheetTransition.instance.RevealOnly();
        else
            InkTransition.instance?.RevealOnly();
    }
}
