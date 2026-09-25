// HitEffects' bursts: flat vertex colours, blended, drawn over everything
// the way a comic effect sits on top of the picture. Each pixel takes only
// the first colour drawn to it (the stencil): HitEffects draws front to
// back, so a fading star fades as one flat shape instead of its ink outline
// showing through its yellow.
Shader "Alkkagi/Hit Effect"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Stencil
        {
            Ref 1
            Comp NotEqual
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
