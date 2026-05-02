Shader "UI/MapIllustrated"
{
    Properties
    {
        [PerRendererData] _MainTex ("Map Texture", 2D) = "white" {}
        _InnerRadius ("Inner Radius (fade starts)", Range(0,1)) = 0.55
        _OuterRadius ("Outer Radius (fully transparent)", Range(0,1)) = 0.85
        _Roundness   ("Corner Roundness (2=circle, 20=square)", Range(2,20)) = 6
        _AspectRatio ("Aspect Ratio (W/H)", Float) = 1.0

        [Toggle] _UseEdgeFade ("Edge Fade", Float) = 1

        [Header(Paper Overlay)]
        _PaperTex      ("Paper Grain Texture", 2D) = "white" {}
        _PaperStrength ("Paper Strength", Range(0,1)) = 0.35

        [Header(Colour Grading)]
        _WarmTint   ("Warm Tint", Color) = (1.05, 0.97, 0.88, 1.0)
        _Saturation ("Saturation", Range(0,2)) = 0.85

        // Required by Unity UI
        _StencilComp     ("Stencil Comparison", Float) = 8
        _Stencil         ("Stencil ID",         Float) = 0
        _StencilOp       ("Stencil Operation",  Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask",  Float) = 255
        _ColorMask       ("Color Mask",         Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _ClipRect;
            float     _InnerRadius;
            float     _OuterRadius;
            float     _Roundness;
            float     _AspectRatio;

            sampler2D _PaperTex;
            float4    _PaperTex_ST;
            float     _PaperStrength;

            fixed4    _WarmTint;
            float     _Saturation;
            float     _UseEdgeFade;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex        = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord      = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color         = v.color;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Sample the map texture
                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;

                // --- Colour grading ---
                // Desaturate
                float grey = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(float3(grey, grey, grey), col.rgb, _Saturation);
                // Warm tint
                col.rgb *= _WarmTint.rgb;
                col.rgb = saturate(col.rgb);

                // --- Paper grain overlay ---
                float2 paperUV = TRANSFORM_TEX(IN.texcoord, _PaperTex);
                fixed3 paper   = tex2D(_PaperTex, paperUV).rgb;
                // Remap paper from [0,1] to [1-strength, 1] so it only darkens, never bleaches
                fixed3 paperMul = lerp(fixed3(1,1,1), paper, _PaperStrength);
                col.rgb *= paperMul;

                // --- Edge fade (superellipse) ---
                if (_UseEdgeFade > 0.5)
                {
                    float2 uv = IN.texcoord - 0.5;
                    uv.x *= _AspectRatio;

                    float n    = _Roundness;
                    float dist = pow(pow(abs(uv.x), n) + pow(abs(uv.y), n), 1.0 / n) * 2.0;
                    col.a *= 1.0 - smoothstep(_InnerRadius, _OuterRadius, dist);
                }

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
