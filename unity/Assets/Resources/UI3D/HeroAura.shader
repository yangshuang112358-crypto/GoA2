Shader "Goa2/UI3D/HeroAura" {
 Properties { _Color("Color",Color)=(.4,.8,1,1) _Opacity("Opacity",Range(0,1))=.5 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"}
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   fixed4 _Color;float _Opacity;
   struct v2f{float4 vertex:SV_POSITION;float3 position:TEXCOORD0;};
   v2f vert(appdata_base v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.position=v.vertex.xyz;return o;}
   fixed4 frag(v2f i):SV_Target {float wave=.62+.38*sin(i.position.x*17+i.position.z*11-_Time.y*3);return fixed4(_Color.rgb*(.8+wave*.4),_Opacity*wave);}
   ENDCG
  }
 }
}
