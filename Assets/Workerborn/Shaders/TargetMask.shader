Shader "Hidden/Workerborn/TargetMask"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    TEXTURE2D(_SelectionBaseMap); SAMPLER(sampler_SelectionBaseMap);
    float4 _SelectionMapST;
    float4 _SelectionId;
    float4x4 _SelectionViewProjection;
    float _SelectionCutoff, _SelectionAspect, _SelectionOutlineWidth;
    struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
    Varyings Surface(Attributes input)
    {
        UNITY_SETUP_INSTANCE_ID(input);
        Varyings output;
        output.positionCS=mul(_SelectionViewProjection,mul(UNITY_MATRIX_M,float4(input.positionOS.xyz,1)));
        output.uv=input.uv*_SelectionMapST.xy+_SelectionMapST.zw;
        return output;
    }
    Varyings Outline(Attributes input)
    {
        Varyings output=Surface(input);
        // Same screen-space extrusion as the supplied FlatKit Outline pass.
        float4 normalCS=mul(_SelectionViewProjection,mul(UNITY_MATRIX_M,float4(input.normalOS,0)));
        float2 normal=normalCS.xy/max(length(normalCS.xy),0.00001);
        output.positionCS.xy+=normal/float2(_SelectionAspect,1)*_SelectionOutlineWidth*output.positionCS.w*0.005;
        return output;
    }
    half4 SurfaceColor(Varyings input):SV_Target
    {
        clip(SAMPLE_TEXTURE2D(_SelectionBaseMap,sampler_SelectionBaseMap,input.uv).a-_SelectionCutoff);
        return _SelectionId;
    }
    half4 OutlineColor(Varyings input):SV_Target { return _SelectionId; }
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
            Name "OutlineMask"
            ZWrite On ZTest LEqual Cull Front
            HLSLPROGRAM
            #pragma vertex Outline
            #pragma fragment OutlineColor
            ENDHLSL
        }
    }
}
