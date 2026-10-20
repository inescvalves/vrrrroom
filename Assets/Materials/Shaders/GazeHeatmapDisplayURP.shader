// URP, stereo-instanced display shader for Single Pass Instanced / Multi-view.
// Draw on a MeshRenderer QUAD (not a SpriteRenderer) — the Quad path renders
// correctly in both eyes on Multi-view, unlike sprite batching.
Shader "GazeHeatmap/DisplayURP"
{
    Properties
    {
        _HeatTex ("Heat (RFloat)", 2D) = "black" {}
        _GradientTex ("Gradient LUT", 2D) = "white" {}
        _Gain ("Intensity gain", Range(0.1, 10)) = 2.0
        _Alpha ("Overall opacity", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_HeatTex);      SAMPLER(sampler_HeatTex);
            TEXTURE2D(_GradientTex);  SAMPLER(sampler_GradientTex);
            float _Gain, _Alpha;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float h = saturate(SAMPLE_TEXTURE2D(_HeatTex, sampler_HeatTex, IN.uv).r * _Gain);
                half4 c = SAMPLE_TEXTURE2D(_GradientTex, sampler_GradientTex, float2(h, 0.5));
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }
}
