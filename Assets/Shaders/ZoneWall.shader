// The closing circle's wall (BoardZoneView): a curtain of light standing on
// the circle, brightest at the board and fading upward, its streaks
// drifting round. Added light, seen from both sides; from straight above
// it's only a line, in the free camera a wall.
Shader "Alkkagi/Zone Wall"
{
    Properties
    {
        _Color ("Light", Color) = (0.42, 0.56, 1, 0.55)
        _Top ("Board top (world y)", Float) = 0
        _Height ("Fades out over (world)", Float) = 0.3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Top, _Height;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; // x: round the circle, 0 to 1
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.height = mul(unity_ObjectToWorld, v.vertex).y - _Top;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float fade = saturate(1 - i.height / _Height) * saturate((i.height + 0.02) / 0.03);
                float t = _Time.y;
                float streaks = 0.75 + 0.15 * sin(i.uv.x * 6.2832 * 90 + t * 1.3) + 0.1 * sin(i.uv.x * 6.2832 * 37 - t * 0.9 + i.height * 12);
                return fixed4(_Color.rgb, _Color.a * fade * fade * streaks);
            }
            ENDCG
        }
    }
}
