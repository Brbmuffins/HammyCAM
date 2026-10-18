// A soft puff of smoke or dust on a camera-facing quad: a round cloud with a lumpy, churning edge and a little
// shading inside, so it reads as a cloud rather than a ball. Per puff (material property block): _Color (alpha is
// how thick) and _Seed. Premultiplied alpha (keeps alpha for OBS transparency). Unlit, URP.
Shader "Muffin/Smoke"
{
    Properties
    {
        _Color ("Colour (alpha = thickness)", Color) = (0.4, 0.4, 0.42, 0.5)
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.07 + 13.7; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float s = _Seed * 5.17;
                float t = _Time.y * 0.35;
                float n = Fbm(p * 1.6 + float2(s, s * 0.7) + float2(t, -t));
                float r = length(p) * (0.78 + 0.55 * n);
                float m = smoothstep(1.0, 0.25, r);
                m *= 0.65 + 0.5 * Fbm(p * 3.1 - float2(s, t * 1.7)); // wisps inside
                float a = saturate(m) * _Color.a;
                float3 c = _Color.rgb * (0.8 + 0.35 * n + 0.15 * p.y); // lit a little from above
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
