Shader "Goa2/UI3D/Minion"
{
    Properties
    {
        _Color ("Paint", Color) = (1,1,1,1)
        _Metallic ("Metal edge", Range(0,1)) = .3
        _Emission ("Glow", Range(0,1)) = 0
        _Roughness ("Roughness", Range(.08,1)) = .55
        _Surface ("Surface: metal/cloth/leather/skin/gem", Float) = 0
        _MainTex ("Authored albedo (UV)", 2D) = "white" {}
        _NormalMap ("Authored tangent normal (linear RGB)", 2D) = "bump" {}
        _MaskMap ("Roughness R / AO G (linear)", 2D) = "white" {}
        _UseAuthoredMaps ("Use authored maps", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color;
            float _Metallic, _Emission, _Roughness, _Surface, _UseAuthoredMaps;
            sampler2D _MainTex, _NormalMap, _MaskMap;
            struct input {float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct output {float4 vertex:SV_POSITION;float3 normal:TEXCOORD0;float3 view:TEXCOORD1;float3 local:TEXCOORD2;float2 uv:TEXCOORD3;float3 tangent:TEXCOORD4;float3 bitangent:TEXCOORD5;float4 color:COLOR;};
            float grain(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            float softNoise(float3 p){float3 f=frac(p);f=f*f*(3-2*f);p=floor(p);return lerp(lerp(lerp(grain(p),grain(p+float3(1,0,0)),f.x),lerp(grain(p+float3(0,1,0)),grain(p+float3(1,1,0)),f.x),f.y),lerp(lerp(grain(p+float3(0,0,1)),grain(p+float3(1,0,1)),f.x),lerp(grain(p+float3(0,1,1)),grain(p+1),f.x),f.y),f.z);}
            output vert(input v)
            {
                output o;
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.view=WorldSpaceViewDir(v.vertex);
                o.color=v.color;
                o.local=v.vertex.xyz;o.uv=v.uv;
                o.tangent=UnityObjectToWorldDir(v.tangent.xyz);
                o.bitangent=cross(o.normal,o.tangent)*v.tangent.w*unity_WorldTransformParams.w;
                return o;
            }
            fixed4 frag(output i):SV_Target
            {
                float3 n=normalize(i.normal),v=normalize(i.view);
                float3 tn=tex2D(_NormalMap,i.uv).xyz*2-1;
                float3 mapped=normalize(n*tn.z+normalize(i.tangent)*tn.x+normalize(i.bitangent)*tn.y);
                if(_UseAuthoredMaps>.5)n=mapped;
                float2 masks=tex2D(_MaskMap,i.uv).rg;
                float rough=clamp(_Roughness*lerp(1,masks.r,_UseAuthoredMaps),.08,1);
                float ao=lerp(1,masks.g,_UseAuthoredMaps);
                // UV coordinates stay attached during skinning; object-space noise
                // would slide across arms and weapons while bones animate.
                float3 surface=float3(i.uv*6,0);
                float variation=softNoise(surface*7);
                float detail=.94+variation*.09;
                float footprint=length(fwidth(surface));
                // Fade fine detail under minification instead of shimmering at board zoom.
                if(_Surface>.5 && _Surface<1.5){
                    float weave=sin(surface.x*260)*sin(surface.y*260);
                    float filter=1-saturate(footprint*130);
                    detail*=1+weave*.028*filter;
                }else if(_Surface>1.5 && _Surface<2.5)detail*=lerp(.99,.94+softNoise(surface*85)*.1,1-saturate(footprint*42));
                else if(_Surface<.5)detail*=lerp(.985,.95+softNoise(surface*float3(32,180,4))*.07,1-saturate(footprint*90));
                float3 painted=_Color.rgb*i.color.rgb*lerp(float3(1,1,1),tex2D(_MainTex,i.uv).rgb,_UseAuthoredMaps)*detail;
                float3 key=normalize(float3(-.45,1,-.35)),fill=normalize(float3(.65,.35,.5));
                float diffuse=saturate(dot(n,key)),bounce=saturate(dot(n,fill));
                float3 ambient=lerp(float3(.22,.25,.29),float3(.43,.49,.53),n.y*.5+.5);
                float3 light=ambient+diffuse*float3(.67,.61,.49)+bounce*float3(.08,.13,.18);
                float exponent=lerp(110,7,rough*rough);
                float spec=pow(saturate(dot(n,normalize(key+v))),exponent);
                float3 f0=lerp(float3(.035,.035,.035),painted,_Metallic);
                float rim=pow(1-saturate(dot(n,v)),4);
                float3 reflection=lerp(float3(.06,.09,.13),float3(.58,.66,.72),smoothstep(-.18,.8,reflect(-v,n).y));
                float3 color=painted*light*ao*(1-_Metallic*.25)+spec*f0*(1.1-rough*.55)
                    +reflection*f0*(.15+rim*.35)+painted*_Emission;
                return fixed4(color,1);
            }
            ENDCG
        }
    }
}
