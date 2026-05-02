using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TagManager : MonoBehaviour
{
    public static TagManager instance;

    [Serializable]
    public class TagDefinition
    {
        public string tagId;
        public string displayName;
    }

    [Header("Tag definitions")]
    public List<TagDefinition> tags = new List<TagDefinition>();

    [Header("Count UI")]
    public TextMeshProUGUI selectedTagsCountText;
    public TextMeshProUGUI selectedInterestsCountText;
    public string selectedTagsPrefix = "Tags: ";
    public string selectedInterestsPrefix = "Interests: ";

    [Header("Limits")]
    public int maxStoryTags = 3;
    public int maxInterests = 5;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            RefreshAllTagButtonLabels();
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void RefreshAllTagButtonLabels()
    {
        var storyButtons = FindObjectsOfType<StoryTagButton>(true);
        foreach (var button in storyButtons)
            button.RefreshLabels();

        var interestButtons = FindObjectsOfType<InterestTagButton>(true);
        foreach (var button in interestButtons)
            button.RefreshLabels();
    }

    private TagDefinition FindTagDefinition(string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return null;

        var exactMatch = tags.Find(t => string.Equals(t.tagId, tagId, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
            return exactMatch;

        if (int.TryParse(tagId.Trim(), out int index) && index >= 0 && index < tags.Count)
            return tags[index];

        return null;
    }

    public string GetDisplayName(string tagId)
    {
        var def = FindTagDefinition(tagId);
        if (def != null)
            return def.displayName;

        return string.IsNullOrWhiteSpace(tagId) ? string.Empty : tagId;
    }

    public string GetCanonicalTagId(string tagId)
    {
        var def = FindTagDefinition(tagId);
        return def != null ? def.tagId : tagId?.Trim() ?? string.Empty;
    }

    public bool IsValidTag(string tagId)
    {
        return FindTagDefinition(tagId) != null;
    }

    public void UpdateStoryTagCount(int selectedCount)
    {
        if (selectedTagsCountText == null)
            return;

        selectedTagsCountText.text = $"{selectedTagsPrefix}{selectedCount}/{maxStoryTags}";
    }

    public void UpdateInterestCount(int selectedCount)
    {
        if (selectedInterestsCountText == null)
            return;

        selectedInterestsCountText.text = $"{selectedInterestsPrefix}{selectedCount}/{maxInterests}";
    }
}
