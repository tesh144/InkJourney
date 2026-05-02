using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to a Button (anchored bottom-centre on the canvas).
/// It auto-shows/hides and repositions itself just above the iOS keyboard.
/// Wire the Button's OnClick → KeyboardDoneButton.OnDonePressed().
/// </summary>
public class KeyboardDoneButton : MonoBehaviour
{
    [Tooltip("The RectTransform of this Done button (usually this GameObject)")]
    public RectTransform buttonRect;

    [Tooltip("Root canvas — used to convert screen pixels to canvas units")]
    public Canvas canvas;

    private void Reset()
    {
        buttonRect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    private void Update()
    {
        float kbHeight = TouchScreenKeyboard.area.height;
        bool keyboardUp = kbHeight > 50f;

        if (buttonRect.gameObject.activeSelf != keyboardUp)
            buttonRect.gameObject.SetActive(keyboardUp);

        if (keyboardUp)
        {
            float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            // Anchor must be bottom-centre; anchoredPosition.y drives vertical offset
            Vector2 pos = buttonRect.anchoredPosition;
            pos.y = kbHeight / scaleFactor + 8f;
            buttonRect.anchoredPosition = pos;
        }
    }

    public void OnDonePressed()
    {
        EventSystem.current.SetSelectedGameObject(null);
    }
}
