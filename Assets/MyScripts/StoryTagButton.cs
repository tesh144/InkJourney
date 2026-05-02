using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StoryTagButton : MonoBehaviour
{
    public string tagId;
    public TextMeshProUGUI labelPrimary;
    public TextMeshProUGUI labelSecondary;
    public GameObject selectedIndicator;
    public Button button;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(OnClick);

        if (labelPrimary == null || labelSecondary == null)
        {
            var labels = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (labels.Length > 0 && labelPrimary == null) labelPrimary = labels[0];
            if (labels.Length > 1 && labelSecondary == null) labelSecondary = labels[1];
        }
    }

    private void Start()
    {
        RefreshLabels();
    }

    private void OnEnable()
    {
        SetSelected(CreateNewStory.instance != null && CreateNewStory.instance.IsTagSelected(tagId));
    }

    public void OnClick()
    {
        if (CreateNewStory.instance == null || string.IsNullOrWhiteSpace(tagId))
            return;

        if (CreateNewStory.instance.IsTagSelected(tagId))
        {
            CreateNewStory.instance.RemoveTag(tagId);
            SetSelected(false);
        }
        else
        {
            CreateNewStory.instance.AddTag(tagId);
            SetSelected(CreateNewStory.instance.IsTagSelected(tagId));
        }
    }

    // Called by CreateNewStory.RefreshTagButtonStates() to sync visual state.
    // Buttons are always interactable — AddTag enforces the max cap.
    public void SetSelected(bool selected)
    {
        selectedIndicator?.SetActive(selected);
        if (button != null)
            button.interactable = true;
    }

    public void RefreshLabels()
    {
        string display = TagManager.instance?.GetDisplayName(tagId) ?? tagId;
        if (labelPrimary != null) labelPrimary.text = display;
        if (labelSecondary != null) labelSecondary.text = display;
    }
}
