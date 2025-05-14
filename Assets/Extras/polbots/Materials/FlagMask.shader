Shader "PolBol/Flag"
{
    Properties
    {
        _MainTex ("Flag (RGB)", 2D) = "white" {}
        _MaskTex ("Mask (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _MainTex;
            sampler2D _MaskTex;

            float4 _MainTex_ST;

            struct Attributes
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv_st : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(IN.pos.xyz);
                o.uv = IN.uv;
                o.uv_st = TRANSFORM_TEX(IN.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 main = tex2D(_MainTex, IN.uv_st);
                half4 mask = tex2D(_MaskTex, IN.uv);

                half alpha = mask.a;
                half3 color = main.rgb * mask.r;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}