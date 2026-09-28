Shader "Goa2/UI3D/Crystal" {
 Properties { _Color("Tint",Color)=(.2,.6,1,1) _Rune("Rune",Float)=0 }
 SubShader { Tags { "RenderType"="Opaque" } Pass { Cull Off
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;float _Rune;
 struct v2f {float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 local:TEXCOORD2;};
 v2f vert(appdata_base v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.normal=UnityObjectToWorldNormal(v.normal);o.local=v.vertex.xyz;return o;}
 fixed4 frag(v2f i):SV_Target {
  float3 n=normalize(i.normal),v=normalize(_WorldSpaceCameraPos-i.world),l=normalize(float3(-.4,1,-.3));
  float fresnel=pow(1-abs(dot(n,v)),3),spec=pow(saturate(dot(n,normalize(l+v))),64);
  float phase=_Time.y*1.25+i.world.x*.17+i.world.z*.13;
  float pulse=.5+.5*sin(phase),band=pow(saturate(sin(i.local.y*11-phase*1.6)),18);
  float3 crystal=_Color.rgb*(.38+.42*saturate(dot(n,l))+.24*pulse)+float3(.65,.8,1)*(fresnel*.45+spec*.8)+_Color.rgb*band*.30;
  return fixed4(lerp(crystal,_Color.rgb*(.83+.17*pulse),_Rune),1);
 }
 ENDCG } }
}
