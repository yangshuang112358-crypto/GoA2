Shader "Goa2/UI3D/Minion"
{
    Properties
    {
        _Color ("Paint", Color) = (1,1,1,1)
        _Metallic ("Metal edge", Range(0,1)) = .3
        _Emission ("Glow", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color;
            float _Metallic, _Emission;
            struct input {float4 vertex:POSITION;float3 normal:NORMAL;float4 color:COLOR;};
            struct output {float4 vertex:SV_POSITION;float3 normal:TEXCOORD0;float3 view:TEXCOORD1;float4 color:COLOR;};
            output vert(input v)
            {
                output o;
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.view=WorldSpaceViewDir(v.vertex);
                o.color=v.color;
                return o;
            }
            fixed4 frag(output i):SV_Target
            {
                float3 n=normalize(i.normal),v=normalize(i.view);
                float3 l=normalize(float3(-.45,1,-.35));
                float diffuse=saturate(dot(n,l));
                float shade=.43+.57*smoothstep(-.08,.88,diffuse);
                float rim=pow(1-saturate(dot(n,v)),3)*.12;
                float spec=pow(saturate(dot(n,normalize(l+v))),28)*_Metallic*.24;
                float3 painted=_Color.rgb*i.color.rgb;
                return fixed4(painted*shade+rim*painted+spec*float3(1,.90,.73)+painted*_Emission,1);
            }
            ENDCG
        }
    }
}
