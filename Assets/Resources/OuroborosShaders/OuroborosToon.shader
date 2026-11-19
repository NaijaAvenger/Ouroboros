// Ouroboros toon surface (v0.15). Built-in render pipeline, forward rendering.
//
// What makes it this game's look and not a stock cel shader:
//   1. The woven edge. Light does not end at a line. Between full light and full shade there is a band where
//      a repeating mark decides, thread by thread, which side each point falls on: short strokes, a diamond
//      lattice or dots. Cast shadows get the same woven edge.
//   2. Indigo shade, ochre light. Shade is tinted toward indigo and light toward warm ochre.
//   3. Ring glints. The highlight is a ring with a core, not a blob.
//   4. An ochre rim on the lit side of upright surfaces.
// Point and spot lights fall in two stepped pools with the same woven edge.
//
// It lives in a Resources folder so that Shader.Find works in a build. Outlines are a separate camera pass
// (OuroborosInk.shader). Opaque only. The marks are plain geometric shapes drawn by the shader.
Shader "Ouroboros/Toon"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _ShadeColor ("Shade tint", Color) = (0.42,0.36,0.62,1)
        _LightTint ("Light tint", Color) = (1,0.95,0.84,1)
        _RimColor ("Rim (alpha = strength)", Color) = (1,0.74,0.32,0.3)
        _GlintColor ("Glint (alpha = strength)", Color) = (1,0.92,0.7,0.35)
        _GlintPower ("Glint tightness", Range(4,128)) = 36
        [Enum(Strokes,0,Diamonds,1,Dots,2)] _Weave ("Woven edge", Float) = 0
        _WeaveScale ("Marks per metre", Range(0.5,12)) = 3
        _BandWidth ("Width of the woven edge", Range(0,0.4)) = 0.2
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "Lighting.cginc"
    #include "AutoLight.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    fixed4 _Color;
    fixed4 _EmissionColor;
    fixed4 _ShadeColor;
    fixed4 _LightTint;
    fixed4 _RimColor;
    fixed4 _GlintColor;
    float _GlintPower;
    float _Weave;
    float _WeaveScale;
    float _BandWidth;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 worldNormal : TEXCOORD1;
        float3 worldPos : TEXCOORD2;
        UNITY_LIGHTING_COORDS(3,4)
        UNITY_FOG_COORDS(5)
        float3 vertexLight : TEXCOORD6;
    };

    v2f vert (appdata_full v)
    {
        v2f o;
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
        o.worldNormal = UnityObjectToWorldNormal(v.normal);
        o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        UNITY_TRANSFER_LIGHTING(o, v.texcoord1.xy);
        UNITY_TRANSFER_FOG(o, o.pos);

        // Lights past the per-pixel limit arrive here as vertex lights. Without this they would simply vanish.
        o.vertexLight = float3(0.0, 0.0, 0.0);
        #ifdef VERTEXLIGHT_ON
        o.vertexLight = Shade4PointLights(
            unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
            unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
            unity_4LightAtten0, o.worldPos, o.worldNormal);
        #endif
        return o;
    }

    // The mark that weaves the edge of the light. 1 where there is a mark, 0 between marks.
    // It is laid in world space on whichever plane the surface faces, so it stays put and keeps its size.
    float WeaveMark (float3 worldPos, float3 n)
    {
        float3 a = abs(n);
        float2 uv = worldPos.xz;
        if (a.x > a.y && a.x > a.z) uv = worldPos.zy;
        else if (a.z > a.y) uv = worldPos.xy;
        uv *= _WeaveScale;

        float2 cell = floor(uv);
        float2 f = frac(uv) - 0.5;

        // Strokes: a short dash in every cell, lying one way in one cell and the other way in the next.
        float turn = frac((cell.x + cell.y) * 0.5) * 2.0;
        float2 d = turn > 0.5 ? f.yx : f.xy;
        float strokes = step(abs(d.y), 0.11) * step(abs(d.x), 0.36);

        // Diamonds: the outline of a diamond in every cell.
        float diamonds = step(abs(abs(f.x) + abs(f.y) - 0.32), 0.07);

        // Dots.
        float dots = step(length(f), 0.2);

        float mark = strokes;
        if (_Weave > 1.5) mark = dots;
        else if (_Weave > 0.5) mark = diamonds;

        // Far away the marks are smaller than a pixel and would shimmer: fade to an even half tone.
        float away = length(_WorldSpaceCameraPos - worldPos);
        return lerp(mark, 0.5, saturate((away - 16.0) / 20.0));
    }

    // 1 in light, 0 in shade. Inside the band the mark moves the line, which is what weaves the edge.
    float WovenStep (float value, float low, float high, float mark)
    {
        float threshold = lerp(low, high, mark);
        float soft = fwidth(value) + 0.008;
        return smoothstep(threshold - soft, threshold + soft, value);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        // The sun (or the scene's main light), ambient light and emission.
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(UnityWorldSpaceLightDir(i.worldPos));
                float3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float mark = WeaveMark(i.worldPos, n);

                // Half-Lambert, so the band sits on the side of a form and not at its very edge.
                float lit = (dot(n, l) * 0.5 + 0.5) * atten;
                float tone = WovenStep(lit, 0.5 - _BandWidth * 0.5, 0.5 + _BandWidth * 0.5, mark);

                float3 ambient = ShadeSH9(float4(n, 1.0)) + i.vertexLight;
                float3 shade = ambient * _ShadeColor.rgb * 1.9;
                float3 light = ambient + _LightColor0.rgb * _LightTint.rgb * 0.95;
                float3 colour = albedo * lerp(shade, light, tone);

                // Ring glint: a ring and a core, only where the light reaches.
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), _GlintPower);
                float ring = smoothstep(0.42, 0.47, spec) - smoothstep(0.7, 0.75, spec) + smoothstep(0.9, 0.94, spec);
                colour += _GlintColor.rgb * (ring * _GlintColor.a * tone);

                // Ochre rim on upright surfaces. Floors are left alone, or every distant floor would glow.
                float rim = 1.0 - saturate(dot(n, v));
                float upright = 1.0 - saturate(abs(n.y) * 1.4);
                colour += _RimColor.rgb * (smoothstep(0.66, 0.72, rim) * upright * tone * _RimColor.a);

                colour += _EmissionColor.rgb;

                fixed4 result = fixed4(colour, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }

        // Every other light: two stepped pools with a woven edge.
        Pass
        {
            Name "FORWARD_DELTA"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(UnityWorldSpaceLightDir(i.worldPos));
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                float mark = WeaveMark(i.worldPos, n);

                float reach = saturate(dot(n, l)) * atten;
                float tone = 0.55 * WovenStep(reach, 0.05, 0.05 + _BandWidth * 0.6, mark)
                           + 0.45 * WovenStep(reach, 0.3, 0.3 + _BandWidth * 0.8, mark);

                fixed4 result = fixed4(albedo * _LightColor0.rgb * _LightTint.rgb * tone, 1.0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, result, fixed4(0, 0, 0, 0));
                return result;
            }
            ENDCG
        }
    }

    // Supplies the shadow caster and the depth passes, and is what draws if this shader cannot.
    FallBack "Diffuse"
}
