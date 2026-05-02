Shader "UI/RawImageCameraCover"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 0 = 0°, 1 = 90° CW, 2 = 180°, 3 = 270° CW
        _RotationSteps ("Rotation Steps", Float) = 0

        // 0 or 1
        _MirrorX ("Mirror X", Float) = 0
        _MirrorY ("Mirror Y", Float) = 0

        // Aspect ratios
        _DisplayAspect ("Display Aspect", Float) = 2
        _TextureAspect ("Texture Aspect", Float) = 1.7777778
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _MainTex_ST;

            float _RotationSteps;
            float _MirrorX;
            float _MirrorY;
            float _DisplayAspect;
            float _TextureAspect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float2 RotateUV(float2 uv, float steps)
            {
                // Rotate around center (0.5, 0.5)
                uv -= 0.5;

                if (steps < 0.5) // 0
                {
                    // no-op
                }
                else if (steps < 1.5) // 90 CW
                {
                    uv = float2(uv.y, -uv.x);
                }
                else if (steps < 2.5) // 180
                {
                    uv = float2(-uv.x, -uv.y);
                }
                else // 270 CW
                {
                    uv = float2(-uv.y, uv.x);
                }

                uv += 0.5;
                return uv;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Apply rotation first
                uv = RotateUV(uv, _RotationSteps);

                // Optional mirroring
                if (_MirrorX > 0.5)
                    uv.x = 1.0 - uv.x;

                if (_MirrorY > 0.5)
                    uv.y = 1.0 - uv.y;

                // Aspect values used for cover-cropping
                float texAspect = max(_TextureAspect, 0.0001);
                float displayAspect = max(_DisplayAspect, 0.0001);

                // Aspect-fill crop
                if (texAspect > displayAspect)
                {
                    // texture is wider -> crop left/right
                    float visibleWidth = displayAspect / texAspect;
                    uv.x = (uv.x - 0.5) * visibleWidth + 0.5;
                }
                else
                {
                    // texture is taller -> crop top/bottom
                    float visibleHeight = texAspect / displayAspect;
                    uv.y = (uv.y - 0.5) * visibleHeight + 0.5;
                }

                // Clamp outside range to transparent
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return fixed4(0,0,0,0);

                fixed4 col = tex2D(_MainTex, uv) * IN.color;
                return col;
            }
            ENDCG
        }
    }
}
