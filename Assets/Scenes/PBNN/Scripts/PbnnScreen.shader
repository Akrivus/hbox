Shader "PBNN/Screen"
{
    Properties
    {
        _MainTex ("Image", 2D) = "black" {}
        _ImageAspect ("Image aspect", Float) = 1.777778
        _ScreenAspect ("Screen aspect", Float) = 1.777778
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _ImageAspect; float _ScreenAspect;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv - .5;
                float ratio = max(_ImageAspect, .001) / max(_ScreenAspect, .001);
                if (ratio > 1) uv.y *= ratio; else uv.x /= ratio;
                uv += .5;
                if (any(uv < 0) || any(uv > 1)) return half4(0,0,0,1);
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
