Shader "SampleClient/PlayerJelly"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WobbleAmount ("Wobble Amount", Range(0, 1)) = 0.35
        _WobbleSpeed ("Wobble Speed", Range(0, 12)) = 4
        _Phase ("Phase", Float) = 0
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
            float _WobbleAmount;
            float _WobbleSpeed;
            float _Phase;

            v2f vert (appdata v)
            {
                v2f o;

                float2 local = v.vertex.xy;
                float radius = saturate(length(local) * 1.5);
                float edge = smoothstep(0.15, 1.0, radius);
                float t = (_Time.y * _WobbleSpeed) + _Phase;

                float waveA = sin((local.y * 7.0) + t);
                float waveB = cos((local.x * 6.0) - (t * 1.15));
                float waveC = sin(((local.x + local.y) * 8.5) + (t * 0.85));
                float squash = sin(t * 1.6);

                local.x += ((waveA * 0.75) + (waveC * 0.25)) * _WobbleAmount * edge * 0.18;
                local.y += ((waveB * 0.75) - (waveC * 0.25)) * _WobbleAmount * edge * 0.18;
                local.x *= 1.0 + (squash * _WobbleAmount * 0.18);
                local.y *= 1.0 - (squash * _WobbleAmount * 0.18);

                o.vertex = UnityObjectToClipPos(float4(local, v.vertex.z, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float2 centered = (i.uv - 0.5) * 2.0;
                float radial = saturate(length(centered));
                float rim = smoothstep(0.42, 0.95, radial);
                float highlight = saturate(1.0 - length(centered - float2(-0.18, 0.22)) * 1.9);
                c.rgb = lerp(c.rgb, c.rgb * 1.18, rim * 0.22);
                c.rgb += highlight * 0.16 * c.a;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
