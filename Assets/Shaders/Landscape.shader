// The landscape board (BoardTerrain): a relief carved from one block of
// wood. Its level ground and slopes are the wood's face, the grain laid by
// world position; the faces of its steps are end grain, darker; go board
// lines run over the ground (so a shot's length can be judged), and faint
// contour lines ring the rises.
Shader "Alkkagi/Landscape"
{
    Properties
    {
        _MainTex ("Grain", 2D) = "white" {}
        _Tile ("World size of the grain (x, z)", Vector) = (3, 3, 0, 0)
        _Color ("Face", Color) = (1, 1, 1, 1)
        _CliffColor ("End grain", Color) = (0.45, 0.3, 0.18, 1)
        _LineColor ("Lines", Color) = (0.2, 0.13, 0.08, 1)
        _LineEvery ("Line spacing", Float) = 0.6
        _LineWidth ("Line width", Float) = 0.012
        _ContourEvery ("Contour spacing (height)", Float) = 0.05
        _Glossiness ("Smoothness", Range(0, 1)) = 0.3
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
        fixed4 _Color, _CliffColor, _LineColor;
        float _LineEvery, _LineWidth, _ContourEvery;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        // How much of a line of the given spacing covers this point, from the
        // screen's own rate of change, so it stays a hairline at any distance.
        float Lines(float2 p, float every, float width)
        {
            float2 cell = abs(frac(p / every + 0.5) - 0.5) * every;
            float2 blur = fwidth(p) * 0.75;
            float2 on = 1 - smoothstep(width - blur, width + blur, cell);
            return max(on.x, on.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float up = saturate(IN.worldNormal.y);
            float level = smoothstep(0.55, 0.85, up);
            fixed3 face = tex2D(_MainTex, IN.worldPos.xz / _Tile.xy).rgb * _Color.rgb;
            fixed3 c = lerp(_CliffColor.rgb * face * 1.6, face, level);
            // The board's lines on the ground, not on the steps' faces.
            float grid = Lines(IN.worldPos.xz, _LineEvery, _LineWidth) * level;
            // Contours where the ground rises (not on the flat).
            float rise = saturate((1 - up) * 12) * level;
            float h = IN.worldPos.y / _ContourEvery;
            float contour = (1 - smoothstep(0.0, fwidth(h) * 1.5, abs(frac(h + 0.5) - 0.5))) * rise;
            c = lerp(c, _LineColor.rgb, saturate(grid * 0.8 + contour * 0.35));
            o.Albedo = c;
            o.Metallic = 0;
            o.Smoothness = _Glossiness * lerp(0.5, 1, level);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
