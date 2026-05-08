using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

public class InkRewardCounter : MonoBehaviour
{
    [System.Serializable]
    public class WordThreshold
    {
        public int minWords;
        public int inkReward;
    }

    [Header("Word Count Thresholds (highest matching tier wins)")]
    public List<WordThreshold> wordThresholds = new List<WordThreshold>
    {
        new WordThreshold { minWords = 50,  inkReward = 5  },
        new WordThreshold { minWords = 150, inkReward = 10 },
        new WordThreshold { minWords = 300, inkReward = 15 },
    };

    [Header("Flat Rewards")]
    public int inkPerTag        = 3;
    public int maxTagsRewarded  = 5;
    public int stickerReward    = 5;
    public int photoReward      = 10;
    public int titleReward      = 5;
    public int publicReward     = 5;
    public int fontReward       = 3;
    public int paragraphReward  = 3;

    [Header("Display")]
    public TextMeshProUGUI rewardText;
    public GameObject      updateIndicator;
    [SerializeField] float tickDelay    = 0.2f;
    [SerializeField] float tickDuration = 0.6f;

    int _reward;
    int _displayed;
    int _stagedReward;
    Coroutine _tickCoroutine;

    public int CurrentReward => _reward;

    void OnEnable()
    {
        UpdateDisplay(_reward, false);
    }

    public void Recalculate(string content, string title, int tagCount,
                            bool hasSticker, bool hasPhoto, bool isPublic, bool hasCustomFont = false)
    {
        int newReward = 0;

        // Word count — highest matching tier only
        int words = CountWords(content);
        int wordReward = 0;
        foreach (var t in wordThresholds)
            if (words >= t.minWords && t.inkReward > wordReward)
                wordReward = t.inkReward;
        newReward += wordReward;

        // Per-tag reward (capped)
        newReward += Mathf.Min(tagCount, maxTagsRewarded) * inkPerTag;

        if (hasSticker)        newReward += stickerReward;
        if (hasPhoto)          newReward += photoReward;
        if (!string.IsNullOrWhiteSpace(title)) newReward += titleReward;
        if (isPublic)          newReward += publicReward;
        if (hasCustomFont)     newReward += fontReward;
        if (HasParagraphs(content)) newReward += paragraphReward;

        if (newReward == _reward) return;

        int from = _displayed;
        _reward  = newReward;
        if (updateIndicator != null) updateIndicator.SetActive(true);
        if (_tickCoroutine != null) StopCoroutine(_tickCoroutine);
        _tickCoroutine = StartCoroutine(Tick(from, newReward));
    }

    public void Apply()
    {
        Debug.Log($"Applying ink reward: {_reward}");
        if (_tickCoroutine != null) { StopCoroutine(_tickCoroutine); _tickCoroutine = null; }

        if (_reward > 0)
            InkManager.instance?.ApplyWithDelay(_reward);
        Debug.Log($"Applied {_reward} ink to player profile.");
        _reward    = 0;
        _displayed = 0;
        if (rewardText != null) rewardText.text = Format(0);
    }

    // Call before the form resets — captures the reward then zeroes the display.
    // Follow up with ApplyStaged() when the reward panel is dismissed.
    public void Stage()
    {
        _stagedReward = _reward;
        ResetWithoutApplying();
    }

    public void ApplyStaged()
    {
        if (_stagedReward > 0)
            InkManager.instance?.AddInk(_stagedReward);
        _stagedReward = 0;
    }

    public void ResetWithoutApplying()
    {
        if (_tickCoroutine != null) { StopCoroutine(_tickCoroutine); _tickCoroutine = null; }
        _reward    = 0;
        _displayed = 0;
        if (rewardText != null) rewardText.text = Format(0);
    }

    void UpdateDisplay(int value, bool animate)
    {
        if (!animate)
        {
            _displayed = value;
            if (rewardText != null) rewardText.text = Format(value);
            return;
        }
        if (_tickCoroutine != null) StopCoroutine(_tickCoroutine);
        _tickCoroutine = StartCoroutine(Tick(_displayed, value));
    }

    IEnumerator Tick(int from, int to)
    {
        if (tickDelay > 0f) yield return new WaitForSeconds(tickDelay);
        float elapsed = 0f;
        while (elapsed < tickDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / tickDuration);
            _displayed = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
            if (rewardText != null) rewardText.text = Format(_displayed);
            yield return null;
        }
        _displayed = to;
        if (rewardText != null) rewardText.text = Format(to);
        _tickCoroutine = null;
    }

    static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Split(text.Trim(), @"\s+").Length;
    }

    static bool HasParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // At least two non-empty blocks separated by a blank line
        var blocks = Regex.Split(text.Trim(), @"\n\s*\n");
        int nonEmpty = 0;
        foreach (var b in blocks)
            if (!string.IsNullOrWhiteSpace(b)) nonEmpty++;
        return nonEmpty >= 2;
    }

    static string Format(int amount)
    {
        string prefix = amount > 0 ? "+" : "";
        if (amount >= 1_000_000)
        {
            float m = amount / 1_000_000f;
            return prefix + (m % 1f == 0f ? $"{(int)m}m" : $"{m:0.#}m");
        }
        if (amount >= 10_000)
        {
            float k = amount / 1_000f;
            return prefix + (k % 1f == 0f ? $"{(int)k}k" : $"{k:0.#}k");
        }
        return prefix + amount.ToString();
    }
}
