// A window onto the camera image that shows one of your real features (an eye, the mouth) on the avatar:
// an oval, or cut along the feature's own outline (the eye opening, the lips) so nothing else of your face
// shows. Soft-edged, sharp, or sharp with an outline, with its own brightness, contrast and colour (the cut-outs
// only). The window's centre, size (pixels) and angle follow your face landmarks, so the feature stays upright and
// framed while you move. Unlit, alpha-blended, URP.
Shader "Muffin/FaceCutout"
{
    Properties
    {
        _MainTex ("Camera image", 2D) = "black" {}
        _Center ("Window centre (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Size ("Window size (pixels: x, y)", Vector) = (64, 48, 0, 0)
        _ImageSize ("Camera image size (pixels)", Vector) = (640, 360, 0, 0)
        _Angle ("Window angle (radians)", Float) = 0
        _FlipX ("Mirror", Float) = 0
        _Feather ("Edge softness (0 = sharp)", Range(0, 0.5)) = 0.3
        _Outline ("Outline width (fraction of the window)", Range(0, 0.3)) = 0
        _OutlineColor ("Outline colour", Color) = (0.1, 0.07, 0.12, 1)
        _PolyCount ("Outline points (0 = oval)", Float) = 0
        _Aspect ("Window width / height", Float) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(-0.5, 0.5)) = 0
        _Contrast ("Contrast", Range(0.5, 2)) = 1
        _Saturation ("Saturation (colour)", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        // Colour blends over the avatar; alpha accumulates so a transparent OBS capture keeps the avatar solid.
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Center;
                float4 _Size;
                float4 _ImageSize;
                float _Angle;
                float _FlipX;
                float _Feather;
                float _Outline;
                half4 _OutlineColor;
                half4 _Tint;
                float _PolyCount;
                float _Aspect;
                float _Brightness;
                float _Contrast;
                float _Saturation;
                // Each eye and mouth has its own outline, including when SRP batching is enabled.
                float4 _Poly[16];
            CBUFFER_END
            // The feature's outline: up to 32 points (two per entry), in window units with x times _Aspect,
            // so distances are in window heights. Set from script in the per-material buffer above.

            float2 PolyPoint(int k)
            {
                float4 v = _Poly[k >> 1];
                return (k & 1) ? v.zw : v.xy;
            }

            // Signed distance to the outline in window heights: negative inside.
            float PolyDistance(float2 p)
            {
                int n = (int)_PolyCount;
                float best = 1e5;
                bool inside = false;
                float2 a = PolyPoint(n - 1);
                [loop] for (int k = 0; k < n; k++)
                {
                    float2 b = PolyPoint(k);
                    float2 ab = b - a, ap = p - a;
                    float t = saturate(dot(ap, ab) / max(dot(ab, ab), 1e-8));
                    best = min(best, length(ap - ab * t));
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    a = b;
                }
                return inside ? -best : best;
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 q = i.uv - 0.5;                       // -0.5..0.5 across the window
                if (_FlipX > 0.5) q.x = -q.x;               // mirror view: from here on, q is in the camera picture
                float d, aa, band;
                half alpha;
                if (_PolyCount > 2.5)
                {
                    d = PolyDistance(float2(q.x * _Aspect, q.y));
                    aa = max(fwidth(d), 1e-4);               // one pixel: sharp edges stay smooth, not jagged
                    float edge = max(_Feather * 0.25, aa);
                    alpha = 1 - smoothstep(-edge, edge, d);
                    band = _Outline * 0.5;
                }
                else
                {
                    float r = length(q * 2);                 // 0 at the centre, 1 at the edge
                    d = r - 1;
                    aa = max(fwidth(r), 1e-4);
                    alpha = 1 - smoothstep(1 - max(_Feather * 2, aa), 1, r);
                    band = _Outline;
                }
                // Keep a soft, rounded boundary even if a noisy contour reaches the quad's edge.
                // Otherwise its rectangular mesh can become visible during a wide smile or blink.
                float2 border = abs(q) - 0.38;
                float borderDistance = length(max(border, 0)) + min(max(border.x, border.y), 0) - 0.12;
                float borderAA = max(fwidth(borderDistance), 1e-4);
                alpha *= 1 - smoothstep(-borderAA * 2, 0, borderDistance);
                float2 px = q * _Size.xy;                    // pixels, v up
                float s = sin(_Angle), c = cos(_Angle);
                px = float2(px.x * c - px.y * s, px.x * s + px.y * c);
                float2 uv = _Center.xy + px / _ImageSize.xy;
                half3 rgb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                // Your look, on the cut-outs only: contrast round mid-grey, brightness, then colour (saturation), worked
                // out on the colours as you see them (sRGB), not the linear light the project renders in.
                #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = LinearToSRGB(rgb);
                #endif
                rgb = (rgb - 0.5) * _Contrast + 0.5 + _Brightness;
                rgb = saturate(lerp(dot(rgb, half3(0.299, 0.587, 0.114)).xxx, rgb, _Saturation));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = SRGBToLinear(rgb);
                #endif
                rgb *= _Tint.rgb;
                if (_Outline > 0) rgb = lerp(rgb, _OutlineColor.rgb, smoothstep(-band - aa, -band, d));
                return half4(rgb, alpha * _Tint.a);
            }
            ENDHLSL
        }
    }
}
