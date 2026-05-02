Shader "Custom/InkBlotch"
{
    Properties
    {
        _Color         ("Ink Color",       Color)              = (0.04, 0.03, 0.02, 1)
        _Threshold     ("Fill Amount",     Range(0, 1))        = 0
        _Softness      ("Edge Softness",   Range(0.001, 0.08)) = 0.03
        _FlowSpeed     ("Flow Speed",      Float)              = 0.14
        _LobeFrequency ("Lobe Count",      Range(1, 8))        = 3.0
        _LobeScale     ("Lobe Size",       Range(0, 0.8))      = 0.48
        _SplatRange    ("Splat Range",     Range(0, 0.3))      = 0.09
        _SplatScale    ("Splat Density",   Range(2, 16))       = 7.0
        _SplatCutoff   ("Splat Cutoff",    Range(0.5, 0.99))   = 0.78
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Overlay"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float  _Threshold;
            float  _Softness;
            float  _FlowSpeed;
            float  _LobeFrequency;
            float  _LobeScale;
            float  _SplatRange;
            float  _SplatScale;
            float  _SplatCutoff;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            // ── Noise ─────────────────────────────────────────────────────

            float2 _Hash(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float _GNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                float a = dot(_Hash(i + float2(0,0)), f - float2(0,0));
                float b = dot(_Hash(i + float2(1,0)), f - float2(1,0));
                float c = dot(_Hash(i + float2(0,1)), f - float2(0,1));
                float d = dot(_Hash(i + float2(1,1)), f - float2(1,1));

                return 0.5 + 0.5 * lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // ── Vertex / Fragment ─────────────────────────────────────────

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 uv    = float2(i.uv.x * aspect, i.uv.y);
                float t      = _Time.y * _FlowSpeed;

                // ── Polar coords from screen centre ───────────────────────
                float2 center = float2(0.5 * aspect, 0.5);
                float2 d      = uv - center;
                float  dist   = length(d);
                float  angle  = atan2(d.y, d.x);

                // ── Low-frequency angular noise → smooth lobes ────────────
                // Sampling on unit circle avoids the ±π seam
                float2 ac = float2(cos(angle), sin(angle));

                // 3 octaves only — keeps shapes big and rounded
                float lobe = 0.0;
                float la = 0.5, lf = _LobeFrequency;
                for (int j = 0; j < 3; j++)
                {
                    lobe += la * _GNoise(ac * lf + float2(t * (0.09 + j * 0.03),
                                                           t * (0.06 + j * 0.02)));
                    lf *= 2.1;
                    la *= 0.5;
                }
                // lobe ∈ [0,1]

                // ── Expanding boundary ────────────────────────────────────
                float cornerDist = length(float2(aspect * 0.5, 0.5));
                float maxRadius  = cornerDist * 1.4;

                float boundary = _Threshold * maxRadius
                               * (1.0 + (lobe - 0.5) * _LobeScale);

                // ── Main liquid shape ─────────────────────────────────────
                float mainAlpha = smoothstep(boundary + _Softness,
                                              boundary - _Softness,
                                              dist);

                // ── Splatter drops ────────────────────────────────────────
                // Circular drops scattered in a ring just outside the main mass.
                // Uses Voronoi-style cell noise so drops are round, not blobby.
                float splatOuter = boundary + _SplatRange * (0.5 + _Threshold * 0.5);
                float inRing     = smoothstep(splatOuter, boundary, dist)
                                 * (1.0 - mainAlpha);

                // Each cell gets a random jittered centre — gives circular drops
                float2 splatP    = uv * _SplatScale;
                float2 splatCell = floor(splatP);
                float2 splatFrac = frac(splatP);
                float2 jitter    = float2(_GNoise(splatCell + float2(t * 0.07, 0.0)),
                                          _GNoise(splatCell + float2(0.0, t * 0.05 + 31.4)));
                float2 dropCenter = 0.5 + (jitter - 0.5) * 0.7;
                float  dropDist   = length(splatFrac - dropCenter);

                // Threshold cell noise to create sparse, sharp-edged drops
                float cellNoise = _GNoise(splatCell + float2(7.3, t * 0.04 + 19.1));
                float dropAlpha = step(dropDist, 0.22) * step(_SplatCutoff, cellNoise);

                // ── Combine ───────────────────────────────────────────────
                float finalAlpha = saturate(mainAlpha + inRing * dropAlpha);
                return fixed4(_Color.rgb, finalAlpha * _Color.a);
            }
            ENDCG
        }
    }
}
