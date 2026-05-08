using System.Text;
using TMPro;
using UnityEngine;

public class InkRewardInfoPanel : MonoBehaviour
{
    [Header("References")]
    public InkRewardCounter rewardCounter;
    public TextMeshProUGUI  descriptionText;

    [Header("Sprite")]
    public string inkSpriteName = "ink";

    [Header("Style")]
    public string sectionHeaderColor = "#888888";
    public string dividerColor       = "#444444";
    public string dividerString      = "──────────────────";

    [Header("Show Rules")]
    public bool showWordThresholds = true;
    public bool showParagraphs     = true;
    public bool showTags           = true;
    public bool showPhoto          = true;
    public bool showSticker        = true;
    public bool showPublic         = true;
    public bool showTitle          = true;
    public bool showFont           = false;

    [Header("Panel Root")]
    public GameObject panelRoot;

    void OnEnable() => Refresh();

    public void CloseAndApply()
    {
        InkManager.instance?.ApplyPendingGain();
        (panelRoot != null ? panelRoot : gameObject).SetActive(false);
    }

    [ContextMenu("Refresh")]
    public void Refresh()
    {
        if (rewardCounter == null || descriptionText == null) return;

        string icon   = $"<sprite name=\"{inkSpriteName}\">";
        string header = $"<color={sectionHeaderColor}><b>{{0}}</b></color>";
        string div    = $"<color={dividerColor}>{dividerString}</color>";
        var sb = new StringBuilder();

        bool hasWordSection = showWordThresholds &&
                              rewardCounter.wordThresholds != null &&
                              rewardCounter.wordThresholds.Count > 0;

        bool hasBonusSection = (showParagraphs) ||
                               (showTags && rewardCounter.inkPerTag > 0) ||
                               showPhoto || showSticker || showTitle ||
                               showPublic || showFont;

        if (hasWordSection)
        {
            sb.AppendLine(string.Format(header, "Word count"));
            foreach (var t in rewardCounter.wordThresholds)
                sb.AppendLine($"  {t.minWords}+ words    +{icon}{t.inkReward}");
        }

        if (hasWordSection && hasBonusSection)
        {
            sb.AppendLine();
            sb.AppendLine(div);
        }

        if (hasBonusSection)
        {
            sb.AppendLine(string.Format(header, "Bonuses"));

            if (showParagraphs)
                sb.AppendLine($"  Paragraphs    +{icon}{rewardCounter.paragraphReward}");

            if (showTags && rewardCounter.inkPerTag > 0)
                sb.AppendLine($"  Tags    +{icon}{rewardCounter.inkPerTag} each");

            if (showPhoto)
                sb.AppendLine($"  Photo    +{icon}{rewardCounter.photoReward}");

            if (showSticker)
                sb.AppendLine($"  Sticker    +{icon}{rewardCounter.stickerReward}");

            if (showTitle)
                sb.AppendLine($"  Title    +{icon}{rewardCounter.titleReward}");

            if (showPublic)
                sb.AppendLine($"  Public    +{icon}{rewardCounter.publicReward}");

            if (showFont)
                sb.AppendLine($"  Custom font    +{icon}{rewardCounter.fontReward}");
        }

        descriptionText.text = sb.ToString().TrimEnd();
    }
}
