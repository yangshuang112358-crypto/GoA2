Shader "Goa2/UI3D/Terrain" {
 Properties { _Color("Tint",Color)=(1,1,1,1) _Grass("Grass",Float)=0 _Rock("Rock",Float)=0 }
 SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _Color;float _Grass,_Rock;
 struct v2f {float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;};
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 v2f vert(appdata_base v){v2f o;float3 w=mul(unity_ObjectToWorld,v.vertex).xyz;w.x+=_Grass*sin(_Time.y*1.6+w.x*2+w.z)*max(0,v.vertex.y-.025)*.13;o.pos=mul(UNITY_MATRIX_VP,float4(w,1));o.world=w;o.normal=UnityObjectToWorldNormal(v.normal);return o;}
 fixed4 frag(v2f i):SV_Target {
  float3 n=normalize(i.normal);float light=.48+.52*saturate(dot(n,normalize(float3(-.4,1,-.3))));
  float broad=noise(i.world.xz*1.4),grain=noise(i.world.xz*39);
  float crack=smoothstep(.46,.48,noise(i.world.xz*7+i.world.y*3))-smoothstep(.49,.52,noise(i.world.xz*7+i.world.y*3));
  float strata=pow(saturate(sin(i.world.y*23+noise(i.world.xz*4)*9)),14)*_Rock;
  float shade=.78+broad*.28+grain*.14-_Rock*crack*.16-strata*.10;
  float3 tint=lerp(_Color.rgb,_Color.rgb*float3(.82,1.09,.72),_Grass*broad);
  return fixed4(tint*light*shade,1);
 }
 ENDCG } }
}
