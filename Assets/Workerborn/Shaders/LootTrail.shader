Shader "Workerborn/Effects/Loot Light"
{
    Properties
    {
        [HDR] _Tint ("Light", Color) = (1.4, 1.1, .62, .85)
        _UseVertexColor ("Use trail gradient", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _UseVertexColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings result;
                result.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                result.color = lerp(half4(1, 1, 1, 1), input.color, _UseVertexColor) * _Tint;
                return result;
            }

            half4 frag(Varyings input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
