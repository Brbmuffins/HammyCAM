// Slime on the camera glass: a splat blob at the top of a tall quad and a drip running down from it, drawn as
// a soft shape (distance field) with a glossy highlight. Per drip: _Length (0..1 down the quad), _Aspect
// (quad height / width), _Pop (splat size, 1 = full) and _Alpha. Unlit, alpha-blended, URP.
Shader "Muffin/Drip"
{
    Properties
    {
        _Color ("Slime colour", Color) = (0.35, 0.95, 0.2, 0.9)
        _Length ("Drip length (0..1)", Range(0, 1)) = 0.3
        _Aspect ("Quad height / width", Float) = 8
        _Pop ("Splat size", Float) = 1
        _Alpha ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Length;
                float _Aspect;
                float _Pop;
                float _Alpha;
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

            float Capsule(float2 p, float2 a, float2 b, float r)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-5));
                return length(pa - ba * h) - r;
            }

            float SmoothMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1 - h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Units of the quad's width: x -0.5..0.5, y 0 (bottom) .. _Aspect (top).
                float2 p = float2(i.uv.x - 0.5, i.uv.y * _Aspect);
                float top = _Aspect - 0.5;
                float blobR = 0.42 * _Pop;
                float headR = 0.2;
                float headY = lerp(top, headR, _Length);
                float blob = length(p - float2(0, top)) - blobR;
                float trail = Capsule(p, float2(0, top), float2(0, headY), 0.11 + 0.03 * sin(p.y * 3.1));
                float head = length(p - float2(0, headY)) - headR * (0.6 + 0.4 * _Length);
                float d = SmoothMin(SmoothMin(blob, trail, 0.15), head, 0.12);
                float aa = max(fwidth(d), 1e-4);
                half alpha = smoothstep(aa, -aa, d) * _Color.a * _Alpha;
                // Darker rim, a glossy streak on the left.
                half3 rgb = _Color.rgb * lerp(0.75, 1.05, smoothstep(0.0, -0.12, d));
                rgb += smoothstep(-0.02, -0.1, d) * smoothstep(-0.05, -0.2, p.x) * smoothstep(-0.35, -0.12, p.x) * 0.35;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
