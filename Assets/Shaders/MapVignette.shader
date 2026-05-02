Shader "UI/MapVignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Vignette Color", Color) = (0,0,0,1)
        _InnerRadius ("Inner Radius", Range(0,1)) = 0.35
        _OuterRadius ("Outer Radius", Range(0,1)) = 0.75
        _Roundness   ("Corner Roundness (2=circle, 10=square-ish)", Range(2,20)) = 5
        _AspectRatio ("Aspect Ratio (W/H)", Float) = 1.0

        // Required by Unity UI
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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

        Cull      Off
        Lighting  Off
        ZWrite    Off
        ZTest     [unity_GUIZTestMode]
        Blend     SrcAlpha OneMinusSrcAlpha
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
            fixed4    _Color;
            float4    _ClipRect;
            float     _InnerRadius;
            float     _OuterRadius;
            float     _Roundness;
            float     _AspectRatio;

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
                // Centre UVs at origin, correct x for aspect ratio
                float2 uv = IN.texcoord - 0.5;
                uv.x     *= _AspectRatio;

                // Superellipse distance: (|x|^n + |y|^n)^(1/n)
                // n=2 → circle, n=10+ → nearly square with smooth corners
                float n    = _Roundness;
                float dist = pow(pow(abs(uv.x), n) + pow(abs(uv.y), n), 1.0 / n) * 2.0;

                float vignette = smoothstep(_InnerRadius, _OuterRadius, dist);

                // Multiply vertex color fully so the RawImage Color field works as expected
                fixed4 col = _Color * IN.color;
                col.a     *= vignette;

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
