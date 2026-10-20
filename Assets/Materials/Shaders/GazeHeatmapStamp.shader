Shader "Hidden/GazeHeatmapStamp"
{
    Properties { _MainTex ("Prev", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float  _Decay, _Radius, _Intensity, _Stamp;
            float2 _Uv;

            float frag(v2f_img i) : SV_Target
            {
                float prev = tex2D(_MainTex, i.uv).r * _Decay;
                float2 d = i.uv - _Uv;
                float sigma = max(_Radius, 1e-4);
                float blob = exp(-dot(d, d) / (2.0 * sigma * sigma)) * _Intensity * _Stamp;
                return prev + blob;
            }
            ENDCG
        }
    }
}
