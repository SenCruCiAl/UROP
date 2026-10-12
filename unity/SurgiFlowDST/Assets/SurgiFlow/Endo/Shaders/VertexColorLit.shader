// Cheap two-sided, vertex-coloured, lit shader for the procedural tooth meshes (URP).
// Main light + constant ambient + Blinn-Phong highlight. No shadows -> very light on an 8 GB / laptop GPU.
Shader "SurgiFlow/VertexColorLit"
{
    Properties
    {
        _Ambient ("Ambient", Range(0, 1)) = 0.38
        _Smoothness ("Smoothness", Range(0, 1)) = 0.45
        _SpecStrength ("Specular strength", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Ambient;
                half _Smoothness;
                half _SpecStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                if (dot(n, v) < 0) n = -n;                       // two-sided: always face the viewer
                Light l = GetMainLight();
                half ndl = saturate(dot(n, l.direction));
                half3 h = normalize(l.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(8, 96, _Smoothness)) * _SpecStrength * ndl;
                half3 albedo = SRGBToLinear(i.color.rgb);        // vertex colours are authored in sRGB
                half3 col = albedo *(_Ambient + ndl * l.color.rgb * (1 - _Ambient)) + spec * l.color.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
