Shader "Custom/UI_SpriteOutline"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0, 50)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineWidth;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // Multi-ring circular sampling — approximates a distance field.
                // Each ring is weighted by proximity: closer rings count more,
                // producing a smooth gradient from sprite edge to outline edge.
                const int STEPS = 16;
                const int RINGS = 4;

                float gradientAlpha = 0;

                for (int r = 1; r <= RINGS; r++)
                {
                    float t = (float)r / RINGS; // 0.25 .. 1.0
                    float2 ringOffset = _MainTex_TexelSize.xy * _OutlineWidth * t;

                    // Weight: inner rings (closer to sprite) are stronger → solid core, soft outer edge
                    float weight = 1.0 - (t * 0.75);

                    float ringSum = 0;
                    for (int s = 0; s < STEPS; s++)
                    {
                        float angle = (6.28318530718 / STEPS) * s;
                        ringSum += tex2D(_MainTex, i.uv + float2(cos(angle), sin(angle)) * ringOffset).a;
                    }

                    gradientAlpha = max(gradientAlpha, (ringSum / STEPS) * weight);
                }

                // Only draw outline where the sprite itself is transparent
                float outlineA = gradientAlpha * (1.0 - col.a) * _OutlineColor.a;

                fixed4 outline = fixed4(_OutlineColor.rgb, outlineA);
                return lerp(outline, col, col.a);
            }
            ENDCG
        }
    }
}
