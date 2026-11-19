// Ouroboros threat outline (v0.17): a flat, bright shell drawn just outside a mesh, so guards, turrets and
// cameras read at a glance against a busy, cel-shaded room.
//
// It is the "inverted hull" method with one change. The usual version pushes each vertex along its normal,
// which tears open at the corners of a box. This one pushes each vertex away from the centre of the mesh's
// own bounding box, by the same distance on every axis whatever the object's scale, so boxes, rods and
// balls built from Unity's primitives all get a closed shell of even width.
//
// The shell is drawn after the ink pass (which runs after opaque geometry) so it is never darkened or
// outlined itself. It is tested against depth, so it is hidden by walls: it marks what you can see, it is
// not a wallhack. Width grows with distance so a sniper across the atrium is marked as clearly as a guard
// in the room. Art.ThreatOutline adds and colours the shells.
Shader "Ouroboros/Threat Outline"
{
    Properties
    {
        _Color ("Colour", Color) = (1,0.27,0.12,1)
        _HullCentre ("Mesh bounds centre", Vector) = (0,0,0,0)
        _HullExtents ("Mesh bounds half size", Vector) = (0.5,0.5,0.5,0)
        _MinWidth ("Narrowest (m)", Float) = 0.02
        _MaxWidth ("Widest (m)", Float) = 0.14
        _Spread ("Width per metre of distance", Float) = 0.004
    }
    SubShader
    {
        // Batching would hand the shader vertices already in world space, and the push is worked out in
        // the mesh's own space.
        Tags { "Queue"="Transparent-100" "RenderType"="ThreatOutline" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _HullCentre;
            float4 _HullExtents;
            float _MinWidth;
            float _MaxWidth;
            float _Spread;

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            // One of the object's axes in the world, at unit length, whatever the object's scale.
            float3 UnitAxis(float3 axis)
            {
                float3 world = mul(unity_ObjectToWorld, float4(axis, 0.0)).xyz;
                return world / max(length(world), 0.00001);
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 halfSize = max(_HullExtents.xyz, float3(0.0001, 0.0001, 0.0001));
                float3 outward = (v.vertex.xyz - _HullCentre.xyz) / halfSize;   // -1..1 across the mesh's box

                float3 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float away = length(_WorldSpaceCameraPos - world);
                float width = clamp(away * _Spread, _MinWidth, _MaxWidth);

                world += (UnitAxis(float3(1.0, 0.0, 0.0)) * outward.x
                        + UnitAxis(float3(0.0, 1.0, 0.0)) * outward.y
                        + UnitAxis(float3(0.0, 0.0, 1.0)) * outward.z) * width;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(_Color.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
