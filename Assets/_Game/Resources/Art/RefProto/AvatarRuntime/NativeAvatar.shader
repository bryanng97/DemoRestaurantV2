Shader "DemoRestaurant/NativeAvatar"
{
    Properties { _MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _NativeDepthTest("Depth test",Float)=4 _NativeItemMask("Item alpha mask",Float)=0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZTest [_NativeDepthTest]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Color; float _NativeItemMask;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct v2f {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
            v2f vert(appdata v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float4 frag(v2f v):SV_Target{float4 c=tex2D(_MainTex,v.uv)*_Color;if(_NativeItemMask>0.5)clip(c.a-0.01);c.a=1;return c;}
            ENDCG
        }
    }
}
