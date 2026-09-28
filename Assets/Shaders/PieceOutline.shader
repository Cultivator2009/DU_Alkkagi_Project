// PieceOutline's line round a piece's silhouette on screen. Pass 0 draws a
// marked piece into a mask, whole even where other pieces hide it (so a
// piece half under another is outlined all round), in the channel of its
// mark: r under the cursor, g being aimed, b what the shot would meet.
// Pass 1 lays the line round the mask's edge over the frame: the mark's
// colour next to the piece, and a soft dark fringe outside that so it
// reads on light and dark wood alike.
Shader "Hidden/Alkkagi/Piece Outline"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
    }
    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _MaskChannel;

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return _MaskChannel;
            }
            ENDCG
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Width; // pixels, line and fringe together
            fixed4 _HoverColor;
            fixed4 _AimColor;
            fixed4 _TargetColor;
            fixed4 _FringeColor;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed3 inside = tex2D(_MainTex, i.uv).rgb;
                fixed3 band = 0;   // a marked piece within half the width
                fixed3 fringe = 0; // how much of the circle at the full width reaches one
                for (int k = 0; k < 16; k++)
                {
                    float a = k * 0.39269908;
                    float2 offset = float2(cos(a), sin(a)) * _MainTex_TexelSize.xy * _Width;
                    band = max(band, tex2D(_MainTex, i.uv + offset * 0.5).rgb);
                    fringe += tex2D(_MainTex, i.uv + offset).rgb / 16;
                }
                // Nothing over a marked piece itself, only round it.
                band *= 1 - inside;
                fringe *= 1 - inside;
                // Aimed over target over cursor.
                fixed4 colour = band.g > 0.5 ? _AimColor : band.b > 0.5 ? _TargetColor : band.r > 0.5 ? _HoverColor : 0;
                if (colour.a > 0) return colour;
                float edge = saturate(max(fringe.r, max(fringe.g, fringe.b)) * 3);
                return fixed4(_FringeColor.rgb, _FringeColor.a * edge);
            }
            ENDCG
        }
    }
}
