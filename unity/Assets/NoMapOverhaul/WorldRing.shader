// A level ring in the 3D view, drawn on the HUD canvas. The mesh is the same for every ring (RingMesh in the mod): each
// vertex names a point around the ring, the side of that point its chord runs to, and its lane across the line. The
// vertex stage turns the point into camera space from the ring's centre and radii, which the mod passes relative to the
// camera so the numbers stay small, clips the chord against the near plane and the screen grown by a guard band, and
// extrudes it to a constant pixel width. The clipping and the mitred joins are RingProjection's and StrokeGraphic's,
// so a ring drawn here matches one the mod builds on the CPU.
Shader "NoMapOverhaul/WorldRing"
{
    Properties
    {
        _RingCenter ("Centre in camera space (m)", Vector) = (0, 0, 100, 0)
        _RingEast ("East radius in camera space (m)", Vector) = (10, 0, 0, 0)
        _RingNorth ("North radius in camera space (m)", Vector) = (0, 0, 10, 0)
        _RingCamera ("Half screen (px), focal length (px), near (m)", Vector) = (960, 540, 540, 0.1)
        _RingStroke ("Half width (px), feather (px), guarded half view across, up", Vector) = (1, 1, 1.8, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // RingProjection.Segments; the mesh has a chord per segment.
            #define SEGMENTS 128
            // StrokeGraphic's MiterLimit.
            #define MITER_LIMIT 3.0

            float4 _RingCenter;
            float4 _RingEast;
            float4 _RingNorth;
            float4 _RingCamera;
            float4 _RingStroke;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 code : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            float3 RingPoint(float index)
            {
                float s, c;
                sincos(index * (6.28318530718 / SEGMENTS), s, c);
                return _RingCenter.xyz + _RingEast.xyz * c + _RingNorth.xyz * s;
            }

            // How far inside each clip plane a camera-space point is, scaled but signed: negative is outside. The
            // planes are RingProjection's: near, then the guarded left, right, bottom, and top.
            void Distances(float3 p, out float d[5])
            {
                d[0] = p.z - _RingCamera.w;
                d[1] = _RingStroke.z * p.z + p.x;
                d[2] = _RingStroke.z * p.z - p.x;
                d[3] = _RingStroke.w * p.z + p.y;
                d[4] = _RingStroke.w * p.z - p.y;
            }

            // The part of the chord from a to b inside every clip plane, as fractions along it; empty when x > y.
            float2 Clip(float3 a, float3 b)
            {
                float da[5];
                float db[5];
                Distances(a, da);
                Distances(b, db);
                float2 t = float2(0, 1);
                [unroll]
                for (int plane = 0; plane < 5; plane++)
                {
                    if (da[plane] < 0 && db[plane] < 0)
                    {
                        return float2(1, 0);
                    }

                    if (da[plane] < 0)
                    {
                        t.x = max(t.x, da[plane] / (da[plane] - db[plane]));
                    }
                    else if (db[plane] < 0)
                    {
                        t.y = min(t.y, da[plane] / (da[plane] - db[plane]));
                    }
                }

                return t;
            }

            float2 ToScreen(float3 p)
            {
                return _RingCamera.xy + p.xy * (_RingCamera.z / p.z);
            }

            // The screen direction of the chord's visible part, from its lower-numbered end.
            float2 Direction(float3 from, float3 to, float2 t)
            {
                float2 d = ToScreen(lerp(from, to, t.y)) - ToScreen(lerp(from, to, t.x));
                float length2 = dot(d, d);
                return length2 > 1e-12 ? d * rsqrt(length2) : float2(1, 0);
            }

            float2 Normal(float2 direction)
            {
                return float2(-direction.y, direction.x);
            }

            v2f vert(appdata v)
            {
                v2f o;
                // code.x is the point, as -1 - point when the vertex's chord runs to the point before it; code.y the
                // lane: its sign the side of the line, 1 on the line's edge, 2 at the end of the fade past it.
                float toNext = v.code.x >= 0 ? 1 : 0;
                float index = v.code.x >= 0 ? v.code.x : -v.code.x - 1;
                float first = index - 1 + toNext;

                // Both ends of a chord clip it from its lower-numbered point, so they agree on what shows.
                float3 from = RingPoint(first);
                float3 to = RingPoint(first + 1);
                float2 t = Clip(from, to);
                if (t.x > t.y)
                {
                    o.vertex = float4(0, 0, 0, 1);
                    o.color = 0;
                    return o;
                }

                float own = 1 - toNext;
                float visible = clamp(own, t.x, t.y);
                float3 at;
                float2 normal;
                float stretch = 1;
                if (visible != own)
                {
                    // Cut by a clip plane: a square end on the chord's own normal.
                    at = lerp(from, to, visible);
                    normal = Normal(Direction(from, to, t));
                }
                else
                {
                    // In view: mitred with the chord on the other side, as StrokeGraphic joins a closed line. The
                    // vertices of both chords meeting here work it out from the same three points.
                    at = RingPoint(index);
                    float3 before = RingPoint(index - 1);
                    float3 after = RingPoint(index + 1);
                    float2 inbound = Normal(Direction(before, at, Clip(before, at)));
                    float2 outbound = Normal(Direction(at, after, Clip(at, after)));
                    float2 miter = inbound + outbound;
                    normal = inbound;
                    if (dot(miter, miter) >= 1e-6)
                    {
                        normal = normalize(miter);
                        stretch = min(1 / max(dot(normal, inbound), 1e-3), MITER_LIMIT);
                    }
                }

                float lane = v.code.y;
                float edge = abs(lane) > 1.5 ? 0.5 : -0.5;
                float reach = sign(lane) * max(0, _RingStroke.x + edge * _RingStroke.y);
                float2 pixel = ToScreen(at) + normal * (reach * stretch);
                // Straight from screen pixels to clip space: the matrices Unity draws a screen-space canvas with don't
                // map pixels there. _ProjectionParams.x is -1 when Unity flips the target upside down.
                o.vertex = float4(pixel / _RingCamera.xy - 1, 0.5, 1);
                o.vertex.y *= _ProjectionParams.x;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // As UI/Default does: alpha rounded to 1/255 steps, then premultiplied.
                half4 color = i.color;
                color.a = round(color.a * 255.0) / 255.0;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
