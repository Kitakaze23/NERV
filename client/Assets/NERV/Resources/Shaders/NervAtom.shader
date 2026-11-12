// NERV: «стеклянный» атом с неоновым ободком.
// Тело — цвет элемента с мягким (wrap) освещением, острый блик, внутреннее свечение по центру
// и HDR-ободок по Френелю (его подхватывает Bloom → неон). Поддерживает GPU-инстансинг.
Shader "NERV/Atom"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Float) = 2.5
        _RimStrength ("Rim Strength", Float) = 1.6
        _Gloss ("Gloss", Float) = 80
        _Core ("Core Glow", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half _Gloss;
            half _Core;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight();
                half nv = saturate(dot(n, v));
                half wrap = saturate(dot(n, l.direction) * 0.5 + 0.5);
                half spec = pow(saturate(dot(n, normalize(l.direction + v))), _Gloss);
                half fres = pow(1.0 - nv, _RimPower);
                half3 ambient = SampleSH(n);
                half3 body = _BaseColor.rgb * (ambient * 0.9 + l.color * wrap * wrap * 0.7);
                half3 col = body
                          + _BaseColor.rgb * pow(nv, 3.0) * _Core
                          + l.color * spec * 1.2
                          + _RimColor.rgb * fres * _RimStrength;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            half fragDepth(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragNormals
            #pragma multi_compile_instancing
            half4 fragNormals(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
