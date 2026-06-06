Shader "SampleClient/PickupAbsorb"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EdgeColor ("Edge Color", Color) = (1,0.95,0.5,1)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _EdgeColor;
            float _Dissolve;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float2 centered = (i.uv - 0.5) * 2.0;
                float radial = saturate(length(centered));
                float wobble = (sin((centered.x * 8.0) + (_Time.y * 6.0)) + cos((centered.y * 9.0) - (_Time.y * 5.0))) * 0.035;
                float mask = saturate((1.0 - radial) + wobble);
                float alpha = smoothstep(_Dissolve - 0.18, _Dissolve + 0.02, mask);
                float edge = alpha - smoothstep(_Dissolve - 0.02, _Dissolve + 0.18, mask);
                c.rgb = lerp(c.rgb, _EdgeColor.rgb, saturate(edge * 2.4));
                c.a *= alpha;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
