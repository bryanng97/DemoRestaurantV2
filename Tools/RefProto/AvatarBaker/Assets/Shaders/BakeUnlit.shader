Shader "Bake/Unlit"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Color;
            struct a { float4 v : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (a i) { v2f o; o.p = UnityObjectToClipPos(i.v); o.uv = i.uv; return o; }
            float4 frag (v2f i) : SV_Target { float4 c = tex2D(_MainTex, i.uv) * _Color; c.a = 1; return c; }
            ENDCG
        }
    }
}
