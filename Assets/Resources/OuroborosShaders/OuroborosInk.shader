// Ouroboros ink pass (v0.15). A camera effect for the built-in render pipeline (Art.InkOutline drives it).
//
// It finds edges from the camera's depth and normals and draws them as ink:
//   - the line is deep indigo, not black;
//   - its thickness swells and thins along its length and its path wanders by a pixel or so, the way a
//     brushed line does, so boxes stop looking ruled;
//   - it thins out with distance instead of turning far geometry into a black mass.
// The same pass grades the picture: a little more saturation, shadows pushed toward indigo and highlights
// toward ochre, and a soft vignette.
Shader "Hidden/Ouroboros/Ink"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _InkColor ("Ink (alpha = strength)", Color) = (0.1,0.07,0.2,0.9)
        _Thickness ("Line thickness in pixels", Range(0.5,4)) = 1.4
        _Wobble ("Brush wobble", Range(0,2)) = 0.8
        _DepthEdge ("Depth sensitivity", Range(0.2,6)) = 1.6
        _NormalEdge ("Normal sensitivity", Range(0.1,2)) = 0.55
        _FadeStart ("Lines start to thin at (m)", Float) = 35
        _FadeEnd ("Lines gone by (m)", Float) = 110
        // (v0.23) The grade and the vignette are off: over toon shading they read as a film laid on the screen.
        // The defaults below leave the picture exactly as the toon shaders drew it; only the ink lines are added.
        // [v0.22] _Saturation = 1.14, _ShadowTone = (0.93,0.92,1.06,1), _HighTone = (1.05,1.01,0.93,1), _Vignette = 0.28
        _Saturation ("Saturation", Range(0,2)) = 1
        _ShadowTone ("Shadow tone", Color) = (1,1,1,1)
        _HighTone ("Highlight tone", Color) = (1,1,1,1)
        _Vignette ("Vignette", Range(0,1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthNormalsTexture;
            fixed4 _InkColor;
            float _Thickness;
            float _Wobble;
            float _DepthEdge;
            float _NormalEdge;
            float _FadeStart;
            float _FadeEnd;
            float _Saturation;
            fixed4 _ShadowTone;
            fixed4 _HighTone;
            float _Vignette;

            // Cheap value noise: enough to vary a line, far too coarse to see as noise.
            float Hash (float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise (float2 p)
            {
                float2 c = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(c);
                float b = Hash(c + float2(1.0, 0.0));
                float d = Hash(c + float2(0.0, 1.0));
                float e = Hash(c + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
            }

            void ReadDepthNormal (float2 uv, out float depth, out float3 normal)
            {
                float4 encoded = tex2D(_CameraDepthNormalsTexture, uv);
                DecodeDepthNormal(encoded, depth, normal);
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 colour = tex2D(_MainTex, i.uv);

                float2 uv = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0.0) uv.y = 1.0 - uv.y;
                #endif

                float2 texel = abs(_MainTex_TexelSize.xy);
                float2 screen = i.uv * _ScreenParams.xy;

                // The brush: the line wanders a little and its weight changes along its length.
                float n1 = Noise(screen * 0.035);
                float n2 = Noise(screen * 0.021 + 17.0);
                float2 wander = (float2(n1, n2) - 0.5) * texel * (2.0 * _Wobble);
                float weight = _Thickness * lerp(1.0, 0.55 + n1 * 1.1, saturate(_Wobble));
                float2 reach = texel * weight;

                float d0; float3 nrm0;
                ReadDepthNormal(uv + wander, d0, nrm0);

                // Lines thin with distance, so a far wall is not a tangle.
                float metres = d0 * _ProjectionParams.z;
                float closeness = 1.0 - smoothstep(_FadeStart, _FadeEnd, metres);

                float dA; float3 nA; ReadDepthNormal(uv + wander + reach * float2(1.0, 1.0), dA, nA);
                float dB; float3 nB; ReadDepthNormal(uv + wander - reach * float2(1.0, 1.0), dB, nB);
                float dC; float3 nC; ReadDepthNormal(uv + wander + reach * float2(-1.0, 1.0), dC, nC);
                float dD; float3 nD; ReadDepthNormal(uv + wander - reach * float2(-1.0, 1.0), dD, nD);

                // Depth is compared relative to how far away it is, or the floor at a distance is all edge.
                float depthDiff = (abs(dA - dB) + abs(dC - dD)) / max(d0, 0.0005);
                float depthEdge = smoothstep(0.02, 0.05, depthDiff * _DepthEdge * 0.25);

                float normalDiff = length(nA - nB) + length(nC - nD);
                float normalEdge = smoothstep(0.35, 0.6, normalDiff * _NormalEdge);

                float edge = saturate(max(depthEdge, normalEdge)) * closeness;
                // The sky has no edges of its own.
                edge *= step(d0, 0.999);

                // Grade.
                float lum = dot(colour.rgb, float3(0.299, 0.587, 0.114));
                colour.rgb = lerp(float3(lum, lum, lum), colour.rgb, _Saturation);
                colour.rgb *= lerp(_ShadowTone.rgb, _HighTone.rgb, smoothstep(0.08, 0.85, lum));

                // Ink.
                colour.rgb = lerp(colour.rgb, _InkColor.rgb, edge * _InkColor.a);

                // Vignette, toward the ink colour and not toward black.
                float2 centred = i.uv - 0.5;
                float corner = smoothstep(0.35, 0.95, dot(centred, centred) * 2.2);
                colour.rgb = lerp(colour.rgb, colour.rgb * lerp(float3(1.0, 1.0, 1.0), _InkColor.rgb * 2.2, 0.6), corner * _Vignette);

                return colour;
            }
            ENDCG
        }
    }
    FallBack Off
}
