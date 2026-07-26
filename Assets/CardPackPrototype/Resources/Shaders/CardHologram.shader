Shader "CardOpen/Hologram"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+50"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Cull Back
            Lighting Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct VertexToFragment
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDirection : TEXCOORD2;
                float2 viewTilt : TEXCOORD3;
            };

            float _Intensity;

            VertexToFragment vert(AppData input)
            {
                VertexToFragment output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.viewDirection = normalize(_WorldSpaceCameraPos.xyz - worldPosition);
                float3 objectViewDirection = normalize(mul((float3x3)unity_WorldToObject, output.viewDirection));
                output.viewTilt = objectViewDirection.xy / max(abs(objectViewDirection.z), 0.35);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(VertexToFragment input) : SV_Target
            {
                float2 viewTilt = clamp(input.viewTilt, -1.0, 1.0);
                float angleOffset = dot(viewTilt, float2(0.68, 0.46));
                float hue = input.uv.x * 1.35 + input.uv.y * 0.55 + angleOffset * 0.48;
                float3 rainbow = 0.5 + 0.5 * cos(6.28318 * (hue + float3(0.0, 0.33, 0.67)));

                float diagonal = frac(input.uv.x * 0.75 + input.uv.y * 0.45 + angleOffset);
                float sweep = smoothstep(0.38, 0.5, diagonal) * (1.0 - smoothstep(0.5, 0.62, diagonal));
                float sparklePhase = input.uv.x * 83.0 + input.uv.y * 59.0
                    + dot(viewTilt, float2(31.0, 27.0));
                float sparkle = pow(saturate(sin(sparklePhase)), 18.0);
                float facing = abs(dot(normalize(input.worldNormal), normalize(input.viewDirection)));
                float angleShine = pow(1.0 - facing, 1.5);

                float alpha = (0.055 + sweep * 0.22 + sparkle * 0.18 + angleShine * 0.17) * _Intensity;
                float3 color = rainbow * (0.72 + sweep * 0.55 + sparkle);
                return fixed4(color, saturate(alpha));
            }
            ENDCG
        }
    }
}
