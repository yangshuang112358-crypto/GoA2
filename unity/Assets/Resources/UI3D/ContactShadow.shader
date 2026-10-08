Shader "Goa2/UI3D/ContactShadow" {
 SubShader {
  Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct v2f {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
   v2f vert(appdata_base v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord.xy*2-1;return o;}
   fixed4 frag(v2f i):SV_Target {float r=length(i.uv);return fixed4(.035,.045,.055,.27*pow(saturate(1-r),1.6));}
   ENDCG
  }
 }
}
