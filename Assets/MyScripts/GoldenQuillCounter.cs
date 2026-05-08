using System.Collections;
using TMPro;
using UnityEngine;

public class GoldenQuillCounter : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI quillText;
    [SerializeField] GameObject gainIndicator;

    [Header("Tick Animation")]
    [SerializeField] float tickDelay    = 0.2f;
    [SerializeField] float tickDuration = 0.6f;

    int      _displayed;
    Coroutine _tickCoroutine;

    void OnEnable()
    {
        GoldenQuillManager.onQuillChanged += OnQuillChanged;
        if (GoldenQuillManager.instance != null)
            SetImmediate(GoldenQuillManager.instance.CurrentQuills);
    }

    void OnDisable() => GoldenQuillManager.onQuillChanged -= OnQuillChanged;

    void OnQuillChanged(int newAmount, bool isGain)
    {
        if (_tickCoroutine != null) StopCoroutine(_tickCoroutine);
        _tickCoroutine = StartCoroutine(Tick(_displayed, newAmount));

        if (gainIndicator != null) gainIndicator.SetActive(isGain);
    }

    void SetImmediate(int amount)
    {
        _displayed = amount;
        if (quillText != null) quillText.text = amount.ToString();
    }

    IEnumerator Tick(int from, int to)
    {
        if (tickDelay > 0f) yield return new WaitForSeconds(tickDelay);
        float elapsed = 0f;
        while (elapsed < tickDuration)
        {
            elapsed += Time.deltaTime;
            _displayed = Mathf.RoundToInt(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / tickDuration)));
            if (quillText != null) quillText.text = _displayed.ToString();
            yield return null;
        }
        _displayed = to;
        if (quillText != null) quillText.text = to.ToString();
        _tickCoroutine = null;
    }
}
