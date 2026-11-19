// Ouroboros sky (v0.15): a dusk sky in flat bands, like a printed backdrop, with a ringed sun.
// A skybox shader for the built-in render pipeline. Art.StyleDirector points _SunDirection at the scene's sun.
Shader "Ouroboros/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.16,0.14,0.38,1)
        _Mid ("Mid sky", Color) = (0.72,0.32,0.3,1)
        _Horizon ("Horizon", Color) = (0.98,0.74,0.36,1)
        _Ground ("Below the horizon", Color) = (0.2,0.16,0.26,1)
        _Bands ("Bands", Range(2,16)) = 7
        _BandSoftness ("Band softness", Range(0,1)) = 0.25
        _SunColor ("Sun", Color) = (1,0.9,0.62,1)
        _SunDirection ("Sun direction", Vector) = (0.3,0.35,0.9,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith;
            fixed4 _Mid;
            fixed4 _Horizon;
            fixed4 _Ground;
            float _Bands;
            float _BandSoftness;
            fixed4 _SunColor;
            float4 _SunDirection;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // Height above the horizon, cut into flat bands with slightly soft joins.
                float h = saturate(d.y);
                float scaled = sqrt(h) * _Bands;
                float band = floor(scaled);
                float join = smoothstep(1.0 - _BandSoftness, 1.0, frac(scaled));
                float t = saturate((band + join) / _Bands);

                float3 colour = t < 0.5 ? lerp(_Horizon.rgb, _Mid.rgb, t * 2.0) : lerp(_Mid.rgb, _Zenith.rgb, t * 2.0 - 1.0);
                colour = lerp(_Ground.rgb, colour, smoothstep(-0.06, 0.0, d.y));

                // The sun: a disc, and a ring round it.
                float s = dot(d, normalize(_SunDirection.xyz));
                float disc = smoothstep(0.9968, 0.9974, s);
                float ring = smoothstep(0.989, 0.9896, s) - smoothstep(0.9915, 0.9921, s);
                float glow = pow(saturate(s), 24.0) * 0.25;
                colour = lerp(colour, _SunColor.rgb, saturate(disc + ring * 0.8 + glow));

                return fixed4(colour, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
