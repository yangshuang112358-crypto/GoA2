Shader "Goa2/UI3D/HeroAura" {
 Properties { _Color("Color",Color)=(.4,.8,1,1) _Opacity("Opacity",Range(0,1))=.5 _Mode("Water / shadow / lightning",Float)=0 _Phase("Offset",Float)=0 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #pragma target 3.0
   fixed4 _Color;float _Opacity,_Mode,_Phase;
   struct v2f{float4 vertex:SV_POSITION;float3 position:TEXCOORD0;float2 uv:TEXCOORD1;};
   v2f vert(appdata_base v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.position=v.vertex.xyz;o.uv=v.texcoord.xy;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 f=frac(p);f=f*f*(3-2*f);p=floor(p);return lerp(lerp(hash(p),hash(p+float2(1,0)),f.x),lerp(hash(p+float2(0,1)),hash(p+1),f.x),f.y);}
   fixed4 frag(v2f i):SV_Target {
     float t=_Time.y+_Phase;
     if(_Mode>1.5){float core=pow(saturate(1-abs(i.uv.y*2-1)),.6);return fixed4(lerp(_Color.rgb,float3(.9,1,1),core*.7),_Opacity*core);}
     float edge=pow(saturate(sin(i.uv.y*3.14159)),1.5);
     float ends=smoothstep(0,.13,i.uv.x)*(1-smoothstep(.79,1,i.uv.x));
     if(_Mode>.5){
       float smoke=noise(float2(i.uv.x*8-t*.35,i.uv.y*3+t*.15));
       smoke*=noise(float2(i.uv.x*17+t*.12,i.uv.y*5));
       return fixed4(_Color.rgb*(.6+smoke*.9),edge*ends*_Opacity*smoothstep(.02,.55,smoke));
     }
     float flow=sin(i.uv.x*48-i.uv.y*11-t*3.3);
     float foam=smoothstep(.65,.95,noise(float2(i.uv.x*50-t*2,i.uv.y*12)))+pow(saturate(flow),8)*.5;
     float3 color=lerp(_Color.rgb,float3(.68,.96,1),saturate(foam));
     return fixed4(color,edge*ends*_Opacity*(.52+.38*saturate(flow)+foam*.3));
   }
   ENDCG
  }
 }
}
