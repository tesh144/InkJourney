using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TapMoveObject : MonoBehaviour
{
    public RectTransform targetObject; // Assign the UI object in the inspector
    public Canvas canvas; // Assign the Canvas in the inspector

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Detect tap or click
        {
            if (targetObject != null)
            {
                targetObject.gameObject.SetActive(false); // Deactivate
                targetObject.gameObject.SetActive(true);  // Reactivate
            }
        }

        MoveObjectToMousePosition();
    }

    void MoveObjectToMousePosition()
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            Input.mousePosition,
            canvas.worldCamera,
            out localPoint);

        if (targetObject != null)
        {
            targetObject.anchoredPosition = localPoint; // Move to mouse position
        }
    }
}