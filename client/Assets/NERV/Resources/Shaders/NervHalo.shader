// NERV: мягкий светящийся ореол (аддитивный). Используется для ионов, подсветки и вспышек.
// Яркость — «кольцо» по Френелю: светится край, центр почти прозрачен, поэтому атом под ореолом виден.
Shader "NERV/Halo"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.8, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Falloff ("Falloff", Float) = 1.5
        _Hollow ("Hollow", Float) = 6
        _Pulse ("Pulse Amount", Float) = 0.15
        _PulseSpeed ("Pulse Speed", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Falloff;
                half _Hollow;
                half _Pulse;
                half _PulseSpeed;
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
                float phase : TEXCOORD2;
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
                // Фаза пульсации — от положения центра, чтобы ионы «дышали» не в унисон.
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                o.phase = dot(c, float3(37.1, 17.3, 23.7));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half nv = saturate(dot(normalize(i.normalWS), normalize(GetWorldSpaceViewDir(i.positionWS))));
                half a = pow(nv, _Falloff) * (1.0 - pow(nv, _Hollow));
                half pulse = 1.0 + _Pulse * sin(_Time.y * _PulseSpeed + i.phase);
                return half4(_Color.rgb * a * _Intensity * pulse, 1.0);
            }
            ENDHLSL
        }
    }
}
