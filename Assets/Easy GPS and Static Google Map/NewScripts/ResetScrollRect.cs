using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ResetScrollRect : MonoBehaviour
{
    public ScrollRect scrollRect;
    public float lerpDuration = 0.5f; // Duration of the lerp animation

    public void ResetPosition()
    {
        StartScrollCoroutine(LerpScroll(1f, 0f)); // Top-left corner
    }

    public void ResetToCentre()
    {
        if (MainMapUserCursorController.Instance != null &&
            MainMapUserCursorController.Instance.userCursor != null)
        {
            LerpToCenterTarget(MainMapUserCursorController.Instance.userCursor);
            return;
        }
        StartScrollCoroutine(LerpScroll(0.5f, 0.5f));
    }

    public void ResetToBottom()
    {
        StartScrollCoroutine(LerpScroll(1f, 0.5f)); // Center position
    }

    public void Direct_ResetPosition()
    {
        // Resetting the scroll position to the top-left corner (default)
        scrollRect.verticalNormalizedPosition = 1f;  // 1 means top for vertical scroll
        scrollRect.horizontalNormalizedPosition = 0f; // 0 means left for horizontal scroll
    }

    public void Direct_ResetToCentre()
    {
        if (MainMapUserCursorController.Instance != null &&
            MainMapUserCursorController.Instance.userCursor != null &&
            scrollRect != null && scrollRect.content != null)
        {
            RectTransform content = scrollRect.content;
            RectTransform viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.GetComponent<RectTransform>();
            if (viewport != null)
            {
                Vector2 contentSize  = content.rect.size;
                Vector2 viewportSize = viewport.rect.size;
                Vector2 localInContent = content.InverseTransformPoint(
                    MainMapUserCursorController.Instance.userCursor.position);
                float contentX = localInContent.x + contentSize.x * content.pivot.x;
                float contentY = localInContent.y + contentSize.y * content.pivot.y;
                float hRange = Mathf.Max(0.0001f, contentSize.x - viewportSize.x);
                float vRange = Mathf.Max(0.0001f, contentSize.y - viewportSize.y);
                scrollRect.horizontalNormalizedPosition = Mathf.Clamp01((contentX - viewportSize.x * 0.5f) / hRange);
                scrollRect.verticalNormalizedPosition   = Mathf.Clamp01(1f - ((contentY - viewportSize.y * 0.5f) / vRange));
                return;
            }
        }
        scrollRect.verticalNormalizedPosition = 0.5f;
        scrollRect.horizontalNormalizedPosition = 0.5f;
    }

    private IEnumerator LerpScroll(float targetVertical, float targetHorizontal)
    {
        float elapsedTime = 0f;
        float startVertical = scrollRect.verticalNormalizedPosition;
        float startHorizontal = scrollRect.horizontalNormalizedPosition;

        while (elapsedTime < lerpDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / lerpDuration;
            t = Mathf.SmoothStep(0f, 1f, t); // Smooth transition

            scrollRect.verticalNormalizedPosition = Mathf.Lerp(startVertical, targetVertical, t);
            scrollRect.horizontalNormalizedPosition = Mathf.Lerp(startHorizontal, targetHorizontal, t);

            yield return null;
        }

        // Ensure final values are set exactly
        scrollRect.verticalNormalizedPosition = targetVertical;
        scrollRect.horizontalNormalizedPosition = targetHorizontal;
    }

    public void LerpToCenterTarget(Transform target)
    {
        if (target == null || scrollRect == null) return;
        StartScrollCoroutine(TrackAndCentre(target));
    }

    // Continuously recalculates the correct scroll position every frame so it
    // stays accurate while the map zoom is animating (e.g. snap-zoom from minScale
    // to maxScale).  Exits once the position has settled.
    private IEnumerator TrackAndCentre(Transform target)
    {
        // Run long enough to cover the slowest snap-zoom (snapZoomSmoothing = 2.5 → ~2 s)
        float deadline = Time.time + Mathf.Max(lerpDuration * 5f, 2.5f);

        while (target != null && scrollRect != null && Time.time < deadline)
        {
            (float tH, float tV) = ComputeNormalisedFor(target);

            // Exponential smooth approach — feels identical to the old SmoothStep but
            // recalculates the target each frame as zoom changes.
            float step = 1f - Mathf.Exp(-8f * Time.deltaTime);
            scrollRect.horizontalNormalizedPosition =
                Mathf.Lerp(scrollRect.horizontalNormalizedPosition, tH, step);
            scrollRect.verticalNormalizedPosition =
                Mathf.Lerp(scrollRect.verticalNormalizedPosition, tV, step);

            // Early-exit once we are close and the scale has stopped changing
            float scale      = scrollRect.content != null ? scrollRect.content.localScale.x : 1f;
            yield return null;
            float scaleNext  = scrollRect.content != null ? scrollRect.content.localScale.x : 1f;

            bool settled = Mathf.Abs(scrollRect.horizontalNormalizedPosition - tH) < 0.002f
                        && Mathf.Abs(scrollRect.verticalNormalizedPosition   - tV) < 0.002f
                        && Mathf.Abs(scaleNext - scale) < 0.001f;

            if (settled) yield break;
        }
    }

    // Calculates the normalised scroll position that centres 'target' in the viewport,
    // correctly accounting for the content's current localScale and Z rotation.
    private (float h, float v) ComputeNormalisedFor(Transform target)
    {
        if (scrollRect == null || scrollRect.content == null) return (0.5f, 0.5f);

        RectTransform content  = scrollRect.content;
        RectTransform viewport = scrollRect.viewport != null
            ? scrollRect.viewport
            : scrollRect.GetComponent<RectTransform>();

        if (viewport == null) return (0.5f, 0.5f);

        float   scaleX      = content.localScale.x;
        float   scaleY      = content.localScale.y;
        Vector2 contentSize = content.rect.size;
        Vector2 scaledSize  = new Vector2(contentSize.x * scaleX, contentSize.y * scaleY);
        Vector2 viewSize    = viewport.rect.size;

        // InverseTransformPoint gives the target in content-local space, correctly
        // undoing translation, rotation, and scale in one call.
        Vector2 local = content.InverseTransformPoint(target.position);
        float cx = (local.x + contentSize.x * content.pivot.x) * scaleX;
        float cy = (local.y + contentSize.y * content.pivot.y) * scaleY;

        float overflowH = scaledSize.x - viewSize.x;
        float overflowV = scaledSize.y - viewSize.y;

        float h, v;
        if (overflowH <= 0f)
        {
            h = 0.5f;
        }
        else
        {
            float rawH  = (cx - viewSize.x * 0.5f) / overflowH;
            float blend = Mathf.Clamp01(overflowH / (viewSize.x * 0.1f));
            h = Mathf.Clamp01(Mathf.Lerp(0.5f, rawH, blend));
        }

        if (overflowV <= 0f)
        {
            v = 0.5f;
        }
        else
        {
            float rawV  = (cy - viewSize.y * 0.5f) / overflowV;
            float blend = Mathf.Clamp01(overflowV / (viewSize.y * 0.1f));
            v = Mathf.Clamp01(Mathf.Lerp(0.5f, rawV, blend));
        }

        return (h, v);
    }

    private Coroutine _activeScrollCoroutine;

    private void StartScrollCoroutine(IEnumerator routine)
    {
        if (routine == null) return;

        // Always cancel the previous coroutine first — multiple concurrent
        // coroutines all writing normalizedPosition each frame fight each other.
        if (_activeScrollCoroutine != null)
        {
            StopCoroutine(_activeScrollCoroutine);
            _activeScrollCoroutine = null;
        }

        if (isActiveAndEnabled)
        {
            _activeScrollCoroutine = StartCoroutine(routine);
            return;
        }

        // Fallback: run via an always-active host to avoid StartCoroutine-on-inactive errors.
        if (GPSManager.Instance != null && GPSManager.Instance.isActiveAndEnabled)
        {
            _activeScrollCoroutine = GPSManager.Instance.StartCoroutine(routine);
            return;
        }

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition   = 0.5f;
            scrollRect.horizontalNormalizedPosition = 0.5f;
        }
    }
}
