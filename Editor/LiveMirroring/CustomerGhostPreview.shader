Shader "Hidden/ThreadLight/CustomerGhostPreview"
{
    Properties { _Color ("Color", Color) = (0.3, 0.8, 1, 0.3) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
