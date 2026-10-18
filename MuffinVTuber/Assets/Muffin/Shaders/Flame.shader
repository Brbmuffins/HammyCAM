// A tongue of fire on a camera-facing quad (uv 0..1, x across, y up; about twice as tall as wide): a teardrop
// shape torn up by rising noise, white-yellow at the hot core through orange to a dark red edge that fades out.
// Per flame (material property block): _Seed so tongues differ, _Intensity 0..1 (it grows and dies with this).
// Premultiplied alpha, so the glow adds light and the alpha still reads for OBS transparency. Unlit, URP.
Shader "Muffin/Flame"
{
    Properties
    {
        _Seed ("Seed", Float) = 0
        _Intensity ("Intensity", Range(0, 1.5)) = 1
        _Speed ("Rise speed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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
                float _Seed;
                float _Intensity;
                float _Speed;
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
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = float2((i.uv.x - 0.5) * 2.0, i.uv.y); // x -1..1, y 0..1
                float s = _Seed * 7.31;

                // Rising turbulence bends the flame, more toward its top.
                float2 q = p;
                q.x += (Fbm(float2(p.x * 1.4 + s, p.y * 2.2 - t * 1.9)) - 0.5) * 0.7 * (0.15 + p.y);
                q.y += (Fbm(float2(p.x * 2.0 - s, p.y * 2.0 - t * 2.6)) - 0.5) * 0.18;

                // Teardrop: round and wide near the bottom, drawn up to a point.
                float h = saturate(q.y);
                float width = 0.62 * pow(saturate(1.0 - h), 0.65) * saturate(h * 5.0 + 0.35);
                float across = abs(q.x) / max(width, 1e-3);
                float heat = saturate(1.0 - across * across);
                heat *= saturate((1.0 - h) * 1.25);

                // Licks of flame tearing off the top, and flicker.
                float tear = Fbm(float2(p.x * 3.2 + s, p.y * 4.5 - t * 4.2));
                heat -= tear * 0.55 * smoothstep(0.15, 0.9, h);
                heat *= 0.85 + 0.3 * Noise(float2(t * 9.0, s));
                heat = saturate(heat * 1.35 * _Intensity);

                // Dark red edge → deep orange → orange-yellow, with only a small pale core low down.
                float3 c = lerp(float3(0.5, 0.03, 0.0), float3(0.95, 0.25, 0.02), smoothstep(0.05, 0.32, heat));
                c = lerp(c, float3(1.0, 0.55, 0.08), smoothstep(0.32, 0.68, heat));
                c = lerp(c, float3(1.0, 0.85, 0.45), smoothstep(0.85, 1.0, heat) * saturate(1.0 - h * 1.6));
                float a = smoothstep(0.03, 0.45, heat);
                return half4(c * a * 0.9, a * 0.85);
            }
            ENDHLSL
        }
    }
}
