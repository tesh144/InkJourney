using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InterestTagButton : MonoBehaviour
{
    public string tagId;
    public TextMeshProUGUI labelPrimary;
    public TextMeshProUGUI labelSecondary;
    public GameObject selectedIndicator;
    public Button button;

    public bool isStatic = false; // If true, the button won't toggle selection on click

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(OnClick);

        CollectLabelReferences();
    }

    public void CollectLabelReferences()
    {
        if (labelPrimary == null || labelSecondary == null)
        {
            var labels = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (labels.Length > 0 && labelPrimary == null)
                labelPrimary = labels[0];
            if (labels.Length > 1 && labelSecondary == null)
                labelSecondary = labels[1];
        }
    }

    private void Start()
    {
        RefreshLabels();
        if(!isStatic){        
            SetSelected(UserProfileManager.instance?.HasInterest(tagId) == true);
        }
    }

    public void OnClick()
    {
        if (isStatic) return;

        if (UserProfileManager.instance == null || string.IsNullOrWhiteSpace(tagId))
            return;

        if (UserProfileManager.instance.HasInterest(tagId))
        {
            UserProfileManager.instance.DeselectInterest(tagId);
            SetSelected(false);
        }
        else
        {
            UserProfileManager.instance.SelectInterest(tagId);
            SetSelected(UserProfileManager.instance.HasInterest(tagId));
        }
    }

    public void SetSelected(bool selected)
    {
        selectedIndicator?.SetActive(selected);
        if (button != null)
            button.interactable = true;
    }

    public void RefreshLabels()
    {
        string display = TagManager.instance?.GetDisplayName(tagId) ?? tagId;
        if (labelPrimary != null)
            labelPrimary.text = display;
        if (labelSecondary != null)
            labelSecondary.text = display;
    }
}
