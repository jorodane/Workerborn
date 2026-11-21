Shader "Hidden/Workerborn/TargetMask"
{
    Properties
    {
        [HideInInspector] _MainTex ("Blit input", 2D) = "black" {}
        [HideInInspector] _OutlineTint ("Outline tint", Color) = (1,1,1,1)
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    TEXTURE2D(_SelectionBaseMap); SAMPLER(sampler_SelectionBaseMap);
    float4 _SelectionMapST;
    float4 _SelectionId;
    float4x4 _SelectionViewProjection;
    float _SelectionCutoff;
    TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
    float4 _OutlineTint;
    struct BlitAttributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
    struct BlitVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
    BlitVaryings BlitVertex(BlitAttributes input)
    {
        BlitVaryings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }
    float4 Composite(BlitVaryings input) : SV_Target
    {
        float4 edge = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
        return float4(_OutlineTint.rgb, edge.r * (1.0 - edge.a) * _OutlineTint.a);
    }
    struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
    Varyings Surface(Attributes input)
    {
        UNITY_SETUP_INSTANCE_ID(input);
        Varyings output;
        output.positionCS=mul(_SelectionViewProjection,mul(UNITY_MATRIX_M,float4(input.positionOS.xyz,1)));
        output.uv=input.uv*_SelectionMapST.xy+_SelectionMapST.zw;
        return output;
    }
    half4 SurfaceColor(Varyings input):SV_Target
    {
        if (_SelectionCutoff > 0)
            clip(SAMPLE_TEXTURE2D(_SelectionBaseMap,sampler_SelectionBaseMap,input.uv).a-_SelectionCutoff);
        return _SelectionId;
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SurfaceMask"
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Surface
            #pragma fragment SurfaceColor
            ENDHLSL
        }
        Pass
        {
            Name "CompositeFlatKitOutline"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex BlitVertex
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
