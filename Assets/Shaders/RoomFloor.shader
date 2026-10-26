// The room's floor (RoomBuilder): its boards laid by world position, so the
// pattern repeats at a set size however big the floor, falling off into
// shade away from the middle so the eye stays on the board.
Shader "Alkkagi/RoomFloor"
{
    Properties
    {
        _MainTex ("Boards", 2D) = "white" {}
        _Tile ("World size of one tile (x, z)", Vector) = (3, 3, 0, 0)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.3
        _ShadeFrom ("Shade begins (distance)", Float) = 4
        _ShadeTo ("Shade full (distance)", Float) = 16
        _ShadeMin ("Brightness at full shade", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        float4 _Tile;
        half _Glossiness;
        float _ShadeFrom, _ShadeTo, _ShadeMin;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.worldPos.xz / _Tile.xy);
            // A little wider than deep, as the screen is.
            float away = length(IN.worldPos.xz * float2(0.75, 1));
            o.Albedo = c.rgb * lerp(1, _ShadeMin, smoothstep(_ShadeFrom, _ShadeTo, away));
            o.Metallic = 0;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
