// A soap bubble on a sphere: clear in the middle, a thin-film rainbow swirling round the rim, and a sharp window
// glint plus a soft one. Per bubble (material property block): _Seed so the colours differ, _Alpha to fade it as
// it pops. Premultiplied alpha (keeps alpha for OBS transparency). Unlit, URP.
Shader "Muffin/Bubble"
{
    Properties
    {
        _Seed ("Seed", Float) = 0
        _Alpha ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+15" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Seed;
                float _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float facing = saturate(dot(n, v));
                float rim = pow(1.0 - facing, 2.2);

                // Thin film: the colour shifts with the angle and swirls slowly over the surface.
                float hue = frac((1.0 - facing) * 1.6 + _Seed * 0.37 + _Time.y * 0.15 + n.y * 0.35 + n.x * 0.2);
                float3 film = 0.5 + 0.5 * cos(6.2832 * (hue + float3(0.0, 0.33, 0.67)));

                // A sharp glint from a window up and to the left, and a soft one low on the other side.
                float3 light = normalize(float3(-0.4, 0.8, -0.45));
                float glint = pow(saturate(dot(n, normalize(light + v))), 140.0) * 1.8;
                float soft = pow(saturate(dot(n, normalize(float3(0.5, -0.35, -0.6) + v))), 30.0) * 0.3;

                float a = saturate(0.05 + rim * 0.9) * _Alpha;
                float3 c = film * (0.25 + rim * 0.85);
                return half4(c * a + (glint + soft) * _Alpha, saturate(a + glint * 0.6));
            }
            ENDHLSL
        }
    }
}
