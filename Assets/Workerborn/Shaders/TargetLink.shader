Shader "Workerborn/UI/Target Link"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _TargetMask ("Visible character silhouettes", 2D) = "black" {}
        _MaskFlipY ("Mask Y direction", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; float4 local : TEXCOORD2; };
            sampler2D _MainTex, _TargetMask;
            float _MaskFlipY;
            float4 _TargetMask_TexelSize;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o;
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                uv.y = lerp(uv.y, 1 - uv.y, _MaskFlipY);
                float silhouette = 0;
                // One mask texel of clearance protects thin edges at reduced capture resolutions.
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                        silhouette = max(silhouette, tex2D(_TargetMask, uv + float2(x, y) * _TargetMask_TexelSize.xy).a);
                clip(.5 - silhouette);
                fixed4 color = tex2D(_MainTex, i.uv) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
