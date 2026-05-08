using System.Collections;
using TMPro;
using UnityEngine;

public class InkCounter : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI inkText;
    [SerializeField] GameObject gainIndicator;

    [Header("Tick Animation")]
    [SerializeField] float tickDelay    = 0.2f;
    [SerializeField] float tickDuration = 0.6f;

    int _displayed;
    Coroutine _tickCoroutine;

    void OnEnable()
    {
        InkManager.onInkChanged += OnInkChanged;
        if (InkManager.instance != null)
            SetImmediate(InkManager.instance.CurrentInk);
    }

    void OnDisable()
    {
        InkManager.onInkChanged -= OnInkChanged;
    }

    void OnInkChanged(int newAmount, bool isGain)
    {
        if (_tickCoroutine != null) StopCoroutine(_tickCoroutine);
        _tickCoroutine = StartCoroutine(Tick(_displayed, newAmount));

        if (isGain && gainIndicator != null)
            gainIndicator.SetActive(true);
        else if (!isGain && gainIndicator != null)
            gainIndicator.SetActive(false);
    }

    void SetImmediate(int amount)
    {
        _displayed = amount;
        if (inkText != null) inkText.text = Format(amount);
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
            if (inkText != null) inkText.text = Format(_displayed);
            yield return null;
        }
        _displayed = to;
        if (inkText != null) inkText.text = Format(to);
        _tickCoroutine = null;
    }

    static string Format(int amount)
    {
        if (amount >= 1_000_000)
        {
            float m = amount / 1_000_000f;
            return (m % 1f == 0f ? $"{(int)m}m" : $"{m:0.#}m");
        }
        if (amount >= 10_000)
        {
            float k = amount / 1_000f;
            return (k % 1f == 0f ? $"{(int)k}k" : $"{k:0.#}k");
        }
        return amount.ToString();
    }
}
