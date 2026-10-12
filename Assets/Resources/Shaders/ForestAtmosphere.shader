Shader "BPG/ForestAtmosphere"
{
    Properties
    {
        _FogColor("Fog tint", Color) = (0.91,0.96,0.9,1)
        _SunlightColor("Sunlight tint", Color) = (1,0.94,0.77,1)
        _FogParams("Fog opacity / scale / density", Vector) = (0.075,0.03333,0.72,0)
        _FogDrift("Fog world drift", Vector) = (0.18,0.008,0,0)
        _FogHeight("Bottom fog height / fade width", Vector) = (0.35,0.30,0,0)
        _LightParams("Rays / dapples / scale / angle", Vector) = (0.035,0.018,0.04762,0.4452)
        _LightMotion("Width / speed / shimmer", Vector) = (0.72,0.24,0.22,0)
        _AnimationTime("Scaled animation time", Float) = 0
        _Seed("Pattern seed", Float) = 17
    }
    SubShader
    {
        Tags { "Queue"="Transparent+60" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        // Premultiplied fog alpha dims the world gently; light adds color without dimming it.
        Blend One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _FogColor;
            half4 _SunlightColor;
            float4 _FogParams;
            float4 _FogDrift;
            float4 _FogHeight;
            float4 _LightParams;
            float4 _LightMotion;
            float _AnimationTime;
            float _Seed;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 worldXY : TEXCOORD1;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.worldXY = TransformObjectToWorld(input.positionOS.xyz).xy;
            output.uv = input.uv;
            return output;
        }

        // Smooth value noise; the pattern needs no texture asset or large generated bitmap.
        float Hash(float2 p)
        {
            float3 h = frac(float3(p.xyx) * 0.1031);
            h += dot(h, h.yzx + 33.33);
            return frac((h.x + h.y) * h.z);
        }

        float Noise(float2 p)
        {
            float2 cell = floor(p);
            float2 blend = frac(p);
            // Quintic interpolation keeps even the second derivative continuous: no cell seams.
            blend = blend * blend * blend * (blend * (blend * 6.0 - 15.0) + 10.0);
            float a = Hash(cell);
            float b = Hash(cell + float2(1.0, 0.0));
            float c = Hash(cell + float2(0.0, 1.0));
            float d = Hash(cell + float2(1.0, 1.0));
            return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
        }

        float CloudNoise(float2 p)
        {
            // Low frequencies only; fine octaves look like smoke rather than atmospheric haze.
            return Noise(p) * 0.76 + Noise(p * 1.93 + 19.3) * 0.24;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            float time = _AnimationTime;
            float2 seedOffset = float2(_Seed * 0.137, _Seed * 0.319);
            float2 fogPosition = (input.worldXY - _FogDrift.xy * time) * _FogParams.y;
            // Broad horizontal currents sit inside a continuous veil, rather than isolated blobs.
            fogPosition.y *= 2.6;
            float bend = Noise(fogPosition * 0.45 + seedOffset + 3.1) - 0.5;
            fogPosition += float2(bend * 0.18, bend * 0.3);
            float cloud = CloudNoise(fogPosition + seedOffset);
            float wisp = Noise(fogPosition * float2(0.6, 1.75)
                + seedOffset.yx + float2(4.1, -time * 0.009));
            float veil = lerp(0.3, 0.65, _FogParams.z);
            float fogShape = veil + (1.0 - veil) * smoothstep(0.18, 0.85,
                cloud * 0.72 + wisp * 0.28);
            float heightWeight = 1.0 - smoothstep(
                max(0.0, _FogHeight.x - _FogHeight.y),
                min(1.0, _FogHeight.x + _FogHeight.y), input.uv.y);
            float fogAlpha = saturate(_FogParams.x * _FogColor.a * fogShape * heightWeight);

            float lightTime = time * _LightMotion.y;
            float2 lightPosition = input.worldXY * _LightParams.z + seedOffset;
            float lane = lightPosition.x + lightPosition.y * _LightParams.w;
            lane += (Noise(lightPosition * 0.37 + lightTime * 0.015) - 0.5) * 0.3;
            lane += lightTime * 0.018;
            // Random openings in foliage have different widths and gaps. No periodic stripe wave.
            float canopy = Noise(float2(lane * 0.82, lightTime * 0.045 + 13.1)) * 0.72
                + Noise(float2(lane * 1.67 + 5.8, lightTime * 0.032 + 9.3)) * 0.28;
            float opening = lerp(0.66, 0.25, _LightMotion.x);
            float beams = smoothstep(opening, min(0.99, opening + 0.38), canopy);
            beams *= lerp(0.12, 1.0, smoothstep(0.05, 0.95, input.uv.y));

            // A broad, diffuse sun patch makes the air luminous without drawing a sun disk.
            float2 sunCenter = float2(0.72 + (Hash(seedOffset + 2.8) - 0.5) * 0.25, 0.93);
            sunCenter.x += (Noise(float2(lightTime * 0.028, _Seed + 3.7)) - 0.5) * 0.08;
            float2 sunDistance = (input.uv - sunCenter) * float2(1.05, 1.25);
            float sunGlow = exp(-dot(sunDistance, sunDistance) * 3.5);

            float2 dapplePosition = lightPosition * float2(1.2, 1.5);
            dapplePosition += float2(lightTime * 0.042, -lightTime * 0.018);
            float dapples = smoothstep(0.22, 0.88, CloudNoise(dapplePosition + 27.5));
            dapples *= lerp(0.9, 0.55, smoothstep(0.2, 0.95, input.uv.y));
            float shimmer = 1.0 - _LightMotion.z * 0.5
                + Noise(float2(lightTime * 0.065 + lane * 0.23, _Seed + 31.7)) * _LightMotion.z;
            float light = ((beams * 0.68 + sunGlow * 0.45) * _LightParams.x
                + dapples * _LightParams.y) * shimmer;
            half3 color = _FogColor.rgb * fogAlpha
                + _SunlightColor.rgb * _SunlightColor.a * light;
            return half4(color, fogAlpha);
        }
        ENDHLSL

        Pass
        {
            Name "Forest2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "ForestForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
