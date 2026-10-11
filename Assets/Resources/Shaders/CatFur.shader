Shader "BPG/CatFur"
{
    Properties
    {
        [MainColor] _Color("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            output.color = input.color * _Color;
            return output;
        }
        half4 Frag(Varyings input) : SV_Target
        {
            // Изогнутые тонкие волоски: отдельная текстура не нужна.
            float y = input.uv.y;
            float center = 0.5 + 0.16 * sin(y * 5.5);
            float strand = 1.0 - smoothstep(0.025, 0.095, abs(input.uv.x - center));
            float second = 1.0 - smoothstep(0.02, 0.07, abs(input.uv.x - center + y * 0.24));
            float tips = smoothstep(0.0, 0.18, y) * (1.0 - smoothstep(0.65, 1.0, y));
            half4 color = input.color;
            color.a *= max(strand, second * 0.65) * tips;
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "Fur2D"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "FurForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
