Shader "Goa2/UI3D/Coin" {
 Properties { _Color ("Tint", Color)=(1,1,1,1) _Metallic ("Metallic", Range(0,1))=.8 }
 SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color; float _Metallic;
 struct v2f {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float3 world:TEXCOORD1;float3 local:TEXCOORD2;};
 v2f vert(appdata_base v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.local=v.vertex.xyz;return o;}
 fixed4 frag(v2f i):SV_Target {
  float3 n=normalize(i.normal),v=normalize(_WorldSpaceCameraPos-i.world),l=normalize(float3(-.4,1,-.3));
  float diffuse=.32+.55*saturate(dot(n,l));
  float spec=pow(saturate(dot(n,normalize(l+v))),72)*1.25;
  float rim=pow(1-saturate(dot(n,v)),4)*.35;
  float grain=frac(sin(dot(i.local.xz,float2(127.1,311.7)))*43758.5453)*.035;
  float3 metalTint=lerp(float3(1,1,1),_Color.rgb,_Metallic);
  return fixed4(_Color.rgb*(diffuse+grain)+metalTint*(spec+rim),1);
 }
 ENDCG } }
}