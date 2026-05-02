using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class DynamicCurvedLine : MonoBehaviour
{
    public List<RectTransform> uiObjects = new List<RectTransform>();
    public int curveResolution = 20;
    public Color lineColor = Color.blue;
    public float lineWidth = 0.1f;

    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = 0;
    }

    void Update()
    {
        DrawLine();
    }

    private void DrawLine()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        if (uiObjects == null || uiObjects.Count < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        List<Vector3> curvePoints = new List<Vector3>();

        for (int i = 0; i < uiObjects.Count - 1; i++)
        {
            if (uiObjects[i] == null || uiObjects[i + 1] == null)
                continue;

            Vector3 p0 = uiObjects[i].position;
            Vector3 p1 = (uiObjects[i].position + uiObjects[i + 1].position) / 2f;
            Vector3 p2 = uiObjects[i + 1].position;

            for (int j = 0; j <= curveResolution; j++)
            {
                float t = j / (float)curveResolution;
                Vector3 curvePoint =
                    Mathf.Pow(1 - t, 2) * p0 +
                    2 * (1 - t) * t * p1 +
                    Mathf.Pow(t, 2) * p2;

                curvePoints.Add(curvePoint);
            }
        }

        lineRenderer.positionCount = curvePoints.Count;

        if (curvePoints.Count > 0)
            lineRenderer.SetPositions(curvePoints.ToArray());
    }

    void OnDrawGizmos()
    {
        if (uiObjects == null || uiObjects.Count < 2)
            return;

        Gizmos.color = lineColor;

        for (int i = 0; i < uiObjects.Count - 1; i++)
        {
            if (uiObjects[i] == null || uiObjects[i + 1] == null)
                continue;

            Vector3 previousPoint = uiObjects[i].position;

            for (int j = 1; j <= curveResolution; j++)
            {
                float t = j / (float)curveResolution;

                Vector3 p0 = uiObjects[i].position;
                Vector3 p1 = (uiObjects[i].position + uiObjects[i + 1].position) / 2f;
                Vector3 p2 = uiObjects[i + 1].position;

                Vector3 currentPoint =
                    Mathf.Pow(1 - t, 2) * p0 +
                    2 * (1 - t) * t * p1 +
                    Mathf.Pow(t, 2) * p2;

                Gizmos.DrawLine(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }
        }
    }
}