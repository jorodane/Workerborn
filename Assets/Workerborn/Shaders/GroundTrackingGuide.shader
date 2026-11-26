Shader "Workerborn/Ground Tracking Guide"
{
    Properties
    {
        _Color ("Guide Color", Color) = (1, 1, 1, 0.75)
        [HideInInspector] _FloorBounds ("Ground Bounds", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HasDirection ("Show Pointer", Float) = 1
        [HideInInspector] _GuideRadius ("Radius", Float) = 0.78
        [HideInInspector] _GuideHalfWidth ("Half Width", Float) = 0.034
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-30" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _FloorBounds;
                float _HasDirection;
                float _GuideRadius;
                float _GuideHalfWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 plane : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 plane : TEXCOORD0;
                float2 ground : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.plane = input.plane;
                output.ground = world.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.plane;
                float ring = abs(length(p) - _GuideRadius) - _GuideHalfWidth;
                float pointer = max(0.76 - p.y, 0.86 * abs(p.x) + 0.48 * (p.y - 1.06));
                float edge = min(ring, lerp(2.0, pointer, _HasDirection));
                float coverage = saturate(0.5 - edge / max(fwidth(edge), 0.0001));
                clip(input.ground - _FloorBounds.xy);
                clip(_FloorBounds.zw - input.ground);
                return half4(_Color.rgb, _Color.a * coverage);
            }
            ENDHLSL
        }
    }
}
