Shader "Goa2/UI3D/Coin" {
 Properties { _Color ("Tint", Color)=(1,1,1,1) _Metallic ("Metallic", Range(0,1))=.8 _Roughness ("Roughness",Range(.05,1))=.25 _Gem ("Cut gem",Range(0,1))=0 }
 SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color; float _Metallic,_Roughness,_Gem;
 struct v2f {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float3 world:TEXCOORD1;float3 local:TEXCOORD2;};
 v2f vert(appdata_base v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.local=v.vertex.xyz;return o;}
 fixed4 frag(v2f i):SV_Target {
  float3 n=normalize(i.normal),v=normalize(_WorldSpaceCameraPos-i.world),l=normalize(float3(-.4,1,-.3));
  float3 reflection=reflect(-v,n);
  // Analytic studio environment: reflected softboxes move with the camera,
  // unlike the previous flat diffuse tint. No scene cubemap dependency.
  float broad=pow(saturate(dot(reflection,normalize(float3(-.55,.75,-.35)))),lerp(18,3,_Roughness));
  float edge=pow(saturate(dot(reflection,normalize(float3(.65,.55,.18)))),lerp(120,12,_Roughness));
  float spec=pow(saturate(dot(n,normalize(l+v))),lerp(180,12,_Roughness));
  float fresnel=pow(1-saturate(dot(n,v)),5);
  float grain=sin(i.local.x*1900+i.local.y*711)*sin(i.local.z*1550)*.008;
  float shade=.23+.45*saturate(dot(n,l));
  float3 reflected=float3(1,.84,.51)*broad*.95+float3(.72,.87,1)*edge*1.5+float3(1,.96,.8)*spec*1.4;
  float3 metal=_Color.rgb*(shade+grain)+lerp(float3(.08,.08,.08),_Color.rgb,_Metallic)*reflected+fresnel*float3(.32,.27,.18);
  float3 gem=_Color.rgb*(.38+.8*saturate(dot(n,l)))+reflected*.6+fresnel*float3(.55,.8,1)*.8;
  return fixed4(lerp(metal,gem,_Gem),1);
 }
 ENDCG } }
}
