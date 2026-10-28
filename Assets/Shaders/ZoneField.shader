// The closing circle (BoardZoneView), cast down on the board and whatever
// stands on it by a projector: outside the circle a slow-moving haze, at
// its edge a bright line with a glow, and the next circle as a dashed line
// (seal red, pulsing, in the round it closes at the end of). Worked out
// per pixel from the world position, so the lines are a pixel or two wide
// at any zoom and follow the ground up and down the landscape.
Shader "Alkkagi/Zone Field"
{
    Properties
    {
        _Haze ("Outside", Color) = (0.22, 0.3, 0.78, 0.4)
        _Edge ("Edge", Color) = (0.62, 0.76, 1, 1)
        _Next ("Next circle", Color) = (1, 0.97, 0.9, 0.8)
        _Warn ("Next circle, closing", Color) = (0.86, 0.26, 0.2, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ColorMask RGB
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Haze, _Edge, _Next, _Warn;
            float4 _Circle;  // x, y: the middle (world x, z); z: the circle now (huge: none yet); w: the next (0: none)
            float _Warned;   // 0 or 1: the next closes as this round ends
            float _Below;    // world height under which nothing is touched (the room's floor)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.world = mul(unity_ObjectToWorld, vertex).xyz;
                return o;
            }

            // a over b, both straight alpha.
            fixed4 Over(fixed4 a, fixed4 b)
            {
                float alpha = a.a + b.a * (1 - a.a);
                return fixed4((a.rgb * a.a + b.rgb * b.a * (1 - a.a)) / max(alpha, 1e-4), alpha);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (i.world.y < _Below) return 0;
                float2 off = i.world.xz - _Circle.xy;
                float d = length(off);
                float pixel = max(fwidth(d), 1e-4);
                float t = _Time.y;

                // Outside: haze drifting outward, thicker by the edge.
                float beyond = d - _Circle.z;
                float outside = smoothstep(-pixel, pixel, beyond);
                float drift = 0.5 + 0.5 * sin(beyond * 22 - t * 2.2 + sin(i.world.x * 3.1 + t * 0.7) * 1.4 + sin(i.world.z * 2.7 - t * 0.6) * 1.4);
                fixed4 color = fixed4(_Haze.rgb, _Haze.a * outside * (0.78 + 0.22 * drift) * (0.8 + 0.4 * exp(-max(beyond, 0) / 0.35)));

                // The edge: a line two pixels wide, and a glow either side of it.
                float edge = saturate(1.5 - abs(beyond) / pixel) + 0.4 * exp(-abs(beyond) / 0.04);
                color = Over(fixed4(_Edge.rgb, saturate(edge) * _Edge.a), color);

                // The next circle: dashes about 0.1 long, creeping round.
                if (_Circle.w > 0)
                {
                    float turns = atan2(off.y, off.x) / 6.2832 + 0.5;
                    float dashes = max(8, round(6.2832 * _Circle.w / 0.1));
                    float dash = step(0.45, frac(turns * dashes + t * 0.15));
                    float stroke = saturate(1.2 - abs(d - _Circle.w) / pixel) * dash;
                    float pulse = lerp(1, 0.65 + 0.35 * sin(t * 6.2832), _Warned);
                    fixed4 next = lerp(_Next, _Warn, _Warned);
                    color = Over(fixed4(next.rgb, stroke * next.a * pulse), color);
                }
                return color;
            }
            ENDCG
        }
    }
}
