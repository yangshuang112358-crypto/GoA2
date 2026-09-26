Shader "Goa2/UI3D/Board"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct v2f { float4 position : SV_POSITION; float light : TEXCOORD0; };
            v2f vert(appdata_base v) {
                v2f o; o.position = UnityObjectToClipPos(v.vertex);
                o.light = .53 + .47 * saturate(dot(UnityObjectToWorldNormal(v.normal), normalize(float3(-.4,1,-.3))));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb * i.light, 1); }
            ENDCG
        }
    }
}
