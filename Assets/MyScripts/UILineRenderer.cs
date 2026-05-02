using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas-native animated route line.
/// All visual settings are driven by MapRouteManager and applied via ApplySettings().
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UILineRenderer : Graphic
{
    // ── Settings (set by MapRouteManager) ──────────────────────────────────
    [HideInInspector] public float lineWidth        = 10f;
    [HideInInspector] public float maxSegmentLength = 20f;
    [HideInInspector] public int   roundSegments    = 6;

    [HideInInspector] public float drawOnDuration = 0.8f;

    [HideInInspector] public Color brightColor  = new Color(0.45f, 1f,  1f,   1f);
    [HideInInspector] public Color dimColor     = new Color(0.05f, 0.3f, 0.38f, 1f);
    [HideInInspector] public float waveLength   = 120f;
    [HideInInspector] public float flowSpeed    = 70f;
    [HideInInspector] public bool  reverseFlow  = false;

    [HideInInspector] public float pulseWidthAmount = 2f;
    [HideInInspector] public float pulseWidthSpeed  = 2f;
    [HideInInspector] public float pulseAlphaMin    = 0.55f;
    [HideInInspector] public float pulseAlphaMax    = 1f;
    [HideInInspector] public float pulseAlphaSpeed  = 1.5f;

    // ── Private state ──────────────────────────────────────────────────────
    private enum AnimState { Hidden, DrawOn, Idle }

    private readonly List<Vector2> _points = new List<Vector2>();
    private float[]  _arcLengths;
    private float    _totalLength;

    private AnimState _state        = AnimState.Hidden;
    private float     _drawProgress = 0f;
    private float     _flowOffset   = 0f;
    private float     _pulseTime    = 0f;

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetPoints(IList<Vector2> points)
    {
        _points.Clear();
        foreach (var p in points) _points.Add(p);
        RebuildArcLengths();
        _drawProgress = 0f;
        _flowOffset   = 0f;
        _pulseTime    = 0f;
        _state        = AnimState.DrawOn;
        SetVerticesDirty();
    }

    // Repositions the line without replaying the draw-on animation.
    public void SetPointsSilent(IList<Vector2> points)
    {
        _points.Clear();
        foreach (var p in points) _points.Add(p);
        RebuildArcLengths();
        _drawProgress = 1f;
        _state        = AnimState.Idle;
        SetVerticesDirty();
    }

    public void ClearPoints()
    {
        _points.Clear();
        _state = AnimState.Hidden;
        SetVerticesDirty();
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_state == AnimState.Hidden || _points.Count < 2) return;

        _flowOffset += (reverseFlow ? -1f : 1f) * flowSpeed * Time.deltaTime;

        if (_state == AnimState.DrawOn)
        {
            _drawProgress = Mathf.MoveTowards(_drawProgress, 1f,
                Time.deltaTime / Mathf.Max(0.001f, drawOnDuration));
            if (Mathf.Approximately(_drawProgress, 1f))
                _state = AnimState.Idle;
        }

        if (_state == AnimState.Idle) _pulseTime += Time.deltaTime;

        SetVerticesDirty();
    }

    // ── Mesh ───────────────────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Count < 2 || _state == AnimState.Hidden) return;

        float visibleLen = _totalLength * _drawProgress;
        float halfW      = CurrentHalfWidth();

        // Start cap
        Vector2 startDir = (_points[1] - _points[0]).normalized;
        EmitCap(vh, _points[0], -startDir, halfW, WaveColor(0f));

        for (int i = 0; i < _points.Count - 1; i++)
        {
            float segStart = _arcLengths[i];
            if (segStart >= visibleLen) break;

            float   segEnd  = _arcLengths[i + 1];
            bool    clipped = segEnd > visibleLen;
            Vector2 p0      = _points[i];
            Vector2 p1      = clipped ? SampleArc(visibleLen) : _points[i + 1];
            float   t1      = clipped ? visibleLen             : segEnd;

            if ((p1 - p0).sqrMagnitude < 0.01f) continue;

            Vector2 segPerp = SegmentPerp(p0, p1, halfW);
            EmitSegment(vh, p0, p1, segStart, t1, halfW, segPerp, segPerp);

            // Round join at each interior route point
            if (!clipped && i < _points.Count - 2)
                EmitRoundJoin(vh, p1, halfW, WaveColor(_arcLengths[i + 1]));
        }

        // End cap at the current tip of the visible line
        Vector2 tipDir = TipDirection(visibleLen);
        EmitCap(vh, SampleArc(visibleLen), tipDir, halfW, WaveColor(visibleLen));
    }

    // ── Round join ─────────────────────────────────────────────────────────
    // Full disc at the join point — covers both the outer gap and the inner
    // seam where adjacent quads meet, giving a clean rounded corner on all sides.

    private void EmitRoundJoin(VertexHelper vh, Vector2 pos, float halfW, Color32 col)
    {
        int n     = Mathf.Max(3, roundSegments * 2);
        int base0 = vh.currentVertCount;
        AddVert(vh, pos, col);
        for (int s = 0; s < n; s++)
        {
            float a = s * (Mathf.PI * 2f / n);
            AddVert(vh, pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * halfW, col);
        }
        for (int s = 0; s < n; s++)
            vh.AddTriangle(base0, base0 + 1 + s, base0 + 1 + (s + 1) % n);
    }

    // ── Segment emission ───────────────────────────────────────────────────

    private void EmitSegment(VertexHelper vh,
        Vector2 p0, Vector2 p1, float t0, float t1, float halfW,
        Vector2 startMiter, Vector2 endMiter)
    {
        float   len     = Vector2.Distance(p0, p1);
        int     n       = Mathf.Max(1, Mathf.CeilToInt(len / maxSegmentLength));
        Vector2 segPerp = SegmentPerp(p0, p1, halfW);

        for (int s = 0; s < n; s++)
        {
            float   f0   = (float)s       / n;
            float   f1   = (float)(s + 1) / n;
            Vector2 sp0  = Vector2.Lerp(p0, p1, f0);
            Vector2 sp1  = Vector2.Lerp(p0, p1, f1);
            float   st0  = Mathf.Lerp(t0, t1, f0);
            float   st1  = Mathf.Lerp(t0, t1, f1);

            // Miter only at the real route endpoints; inner subdivision uses segPerp
            Vector2 perpStart = s == 0     ? startMiter : segPerp;
            Vector2 perpEnd   = s == n - 1 ? endMiter   : segPerp;

            EmitQuad(vh, sp0, sp1, perpStart, perpEnd, WaveColor(st0), WaveColor(st1));
        }
    }

    // ── Quad ──────────────────────────────────────────────────────────────

    private void EmitQuad(VertexHelper vh,
        Vector2 p0, Vector2 p1,
        Vector2 perp0, Vector2 perp1,
        Color32 c0, Color32 c1)
    {
        int idx = vh.currentVertCount;
        AddVert(vh, p0 - perp0, c0);
        AddVert(vh, p0 + perp0, c0);
        AddVert(vh, p1 + perp1, c1);
        AddVert(vh, p1 - perp1, c1);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    // ── End cap ────────────────────────────────────────────────────────────

    private void EmitCap(VertexHelper vh,
        Vector2 pos, Vector2 outDir, float halfW, Color32 col)
    {
        float centerAngle = Mathf.Atan2(outDir.y, outDir.x);
        float startAngle  = centerAngle - Mathf.PI * 0.5f;
        int   base0       = vh.currentVertCount;

        AddVert(vh, pos, col);
        AddVert(vh, pos + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * halfW, col);

        for (int s = 1; s <= roundSegments; s++)
        {
            float a = startAngle + Mathf.PI * s / roundSegments;
            AddVert(vh, pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * halfW, col);
            vh.AddTriangle(base0, base0 + s, base0 + s + 1);
        }
    }

    // ── Colour ────────────────────────────────────────────────────────────

    private float CurrentHalfWidth()
    {
        float w = lineWidth;
        if (_state == AnimState.Idle)
            w += Mathf.Sin(_pulseTime * pulseWidthSpeed) * pulseWidthAmount;
        return w * 0.5f;
    }

    private Color32 WaveColor(float arcPos)
    {
        float phase = (arcPos - _flowOffset) / Mathf.Max(1f, waveLength) * Mathf.PI * 2f;
        float t     = (Mathf.Sin(phase) + 1f) * 0.5f;
        Color c     = Color.Lerp(dimColor, brightColor, t);

        if (_state == AnimState.Idle)
            c.a *= Mathf.Lerp(pulseAlphaMin, pulseAlphaMax,
                (Mathf.Sin(_pulseTime * pulseAlphaSpeed) + 1f) * 0.5f);

        return c;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Vector2 SegmentPerp(Vector2 a, Vector2 b, float halfW)
    {
        Vector2 d = (b - a).normalized;
        return new Vector2(-d.y, d.x) * halfW;
    }

    private Vector2 TipDirection(float visibleLen)
    {
        for (int i = _points.Count - 2; i >= 0; i--)
            if (_arcLengths[i] < visibleLen)
                return (_points[i + 1] - _points[i]).normalized;
        return (_points[1] - _points[0]).normalized;
    }

    private Vector2 SampleArc(float t)
    {
        if (_arcLengths == null || _points.Count < 2) return Vector2.zero;
        if (t <= 0f)           return _points[0];
        if (t >= _totalLength) return _points[_points.Count - 1];

        for (int i = 1; i < _arcLengths.Length; i++)
        {
            if (_arcLengths[i] >= t)
            {
                float seg  = _arcLengths[i] - _arcLengths[i - 1];
                float frac = seg > 0f ? (t - _arcLengths[i - 1]) / seg : 0f;
                return Vector2.Lerp(_points[i - 1], _points[i], frac);
            }
        }
        return _points[_points.Count - 1];
    }

    private void RebuildArcLengths()
    {
        _arcLengths  = new float[_points.Count];
        _totalLength = 0f;
        for (int i = 1; i < _points.Count; i++)
        {
            _totalLength  += Vector2.Distance(_points[i - 1], _points[i]);
            _arcLengths[i] = _totalLength;
        }
    }

    private static void AddVert(VertexHelper vh, Vector2 pos, Color32 col)
    {
        var v = new UIVertex();
        v.position = pos;
        v.color    = col;
        v.uv0      = Vector2.zero;
        vh.AddVert(v);
    }
}
