Shader "Goa2/UI3D/Terrain" {
 Properties { _Color("Tint",Color)=(1,1,1,1) _Grass("Grass",Float)=0 _Rock("Rock",Float)=0 }
 SubShader { Tags { "RenderType"="Opaque" } Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 float4 _Color;float _Grass,_Rock;
 struct v2f {float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 view:TEXCOORD2;};
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 // Even fields preserve the board's 180-degree visual symmetry. Surface detail
 // uses world coordinates, so adjacent imported meshes never show UV seams.
 float stoneNoise(float2 p){return (noise(p)+noise(-p))*.5;}
 // Height offsets vary the grain across a face, with mirror averaging applied
 // after the offset so both halves of the board retain identical pigment.
 float stoneVolume(float2 p,float height,float frequency){
  float2 offset=height*float2(.71,1.37);
  return (noise(p*frequency+offset)+noise(-p*frequency+offset))*.5;
 }
 // Uneven joints between large mineral blocks. Only a minority of the cells
 // expose their boundary, so this does not become a tiled cobblestone pattern.
 float2 stoneJoint(float2 p){
  float2 cell=floor(p),local=frac(p);float first=10,second=10,mark=0;
  [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){
   float2 neighbour=float2(x,y),id=cell+neighbour;
   float2 site=.18+.64*float2(hash(id+float2(31.7,11.3)),hash(id+float2(4.9,73.1)));
   float2 delta=neighbour+site-local;float distance=dot(delta,delta);
   if(distance<first){second=first;first=distance;mark=hash(id+float2(92.1,41.3));}
   else second=min(second,distance);
  }
  float gap=second-first,aa=max(.004,fwidth(gap));
  float crack=(1-smoothstep(.012-aa,.029+aa,gap))*step(.58,mark);
  return float2(crack,mark);
 }
 v2f vert(appdata_base v){v2f o;float3 w=mul(unity_ObjectToWorld,v.vertex).xyz;w.x+=_Grass*sin(_Time.y*1.6+w.x*2+w.z)*max(0,v.vertex.y-.025)*.13;o.pos=mul(UNITY_MATRIX_VP,float4(w,1));o.world=w;o.normal=UnityObjectToWorldNormal(v.normal);o.view=_WorldSpaceCameraPos-w;return o;}
 fixed4 frag(v2f i):SV_Target {
  float3 n=normalize(i.normal);float light=.48+.52*saturate(dot(n,normalize(float3(-.4,1,-.3))));
  if(_Rock>.5){
   float2 q=i.world.xz-float2(.433012702,-.75);
   float broad=stoneVolume(q,i.world.y,1.25),mineral=stoneVolume(q,i.world.y,4.4);
   float grain=stoneVolume(q,i.world.y,23);
   // Visible mid-scale weathering, never periodic height bands. The geometry
   // supplies the major fractures; pigment changes describe the same stone,
   // rather than covering the whole wall in fine black crack lines.
   float weather=saturate((broad-.29)*2.25);
   float mineralVein=smoothstep(.51,.58,mineral)-smoothstep(.60,.69,mineral);
   float recess=smoothstep(.53,.70,stoneVolume(q,i.world.y,2.7));
   float up=saturate(n.y),side=1-up;
   // Folding once about the true board centre keeps sparse cuts symmetric.
   // Use a vertical projection on side faces so fissures descend the wall.
   float2 symmetric=q*(q.x<0 || (q.x==0 && q.y<0) ? -1 : 1);
   float2 jointPlane=up>.65?symmetric*.86:float2(abs(n.x)>abs(n.z)?symmetric.y:symmetric.x,i.world.y*1.4)*.92;
   float2 joint=stoneJoint(jointPlane);
   float darkFoot=(1-smoothstep(.03,.36,i.world.y))*.16;
   float shade=.81+weather*.19+(mineral-.5)*.13+(grain-.5)*.043;
   shade-=side*recess*.075+darkFoot;
   shade+=mineralVein*.035;
   shade+=(joint.y-.5)*.035-joint.x*.23;
   float3 stoneTint=lerp(float3(.80,.87,.88),float3(1.12,1.055,.91),up*.52+weather*.22);
   float moss=(1-smoothstep(.04,.30,i.world.y))*smoothstep(.49,.68,broad)*.12;
   stoneTint=lerp(stoneTint,float3(.71,.80,.60),moss);
   float3 halfDirection=normalize(normalize(i.view)+normalize(float3(-.4,1,-.3)));
   float roughSheen=pow(saturate(dot(n,halfDirection)),9)*.022;
   return fixed4(_Color.rgb*stoneTint*light*shade+roughSheen,1);
  }
  // Keep the earth and grass branch byte-for-byte in appearance.
  float broad=noise(i.world.xz*1.4),grain=noise(i.world.xz*39);
  float crack=smoothstep(.46,.48,noise(i.world.xz*7+i.world.y*3))-smoothstep(.49,.52,noise(i.world.xz*7+i.world.y*3));
  float strata=pow(saturate(sin(i.world.y*23+noise(i.world.xz*4)*9)),14)*_Rock;
  float shade=.78+broad*.28+grain*.14-_Rock*crack*.16-strata*.10;
  float3 tint=lerp(_Color.rgb,_Color.rgb*float3(.82,1.09,.72),_Grass*broad);
  return fixed4(tint*light*shade,1);
 }
 ENDCG } }
}
