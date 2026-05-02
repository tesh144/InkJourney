using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StoryContentLinkHandler : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text targetText;

    private void Awake()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (targetText == null)
            return;

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(targetText, eventData.position, eventData.pressEventCamera);
        if (linkIndex == -1)
            return;

        string url = targetText.textInfo.linkInfo[linkIndex].GetLinkID();
        if (!string.IsNullOrWhiteSpace(url))
            Application.OpenURL(url);
    }
}
