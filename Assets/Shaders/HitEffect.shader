// HitEffects' bursts: flat vertex colours, blended, lying on the board and
// depth-tested, so the pieces (drawn before, as opaques) stand over them -
// an effect never hides a piece. Each pixel takes only the first colour
// drawn to it (the stencil): HitEffects draws front to back, so a fading
// shape fades as one instead of what's under it showing through.
Shader "Alkkagi/Hit Effect"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
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
