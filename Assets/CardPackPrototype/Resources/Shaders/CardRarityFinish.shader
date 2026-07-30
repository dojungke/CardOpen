Shader "CardOpen/RarityFinish"
{
    Properties
    {
        _EffectMode ("Effect Mode", Float) = 0
        _Tint ("Effect Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+40"
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

            float _EffectMode;
            fixed4 _Tint;
            float _Intensity;

            float RandomCell(float2 cell)
            {
                return frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
            }

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
                float facing = abs(dot(normalize(input.worldNormal), normalize(input.viewDirection)));
                float angleGloss = pow(1.0 - facing, 1.35);
                float2 viewTilt = clamp(input.viewTilt, -1.0, 1.0);

                float diagonal = frac(input.uv.x * 0.78 + input.uv.y * 0.42 + dot(viewTilt, float2(0.72, 0.48)));
                float angleBand = smoothstep(0.34, 0.48, diagonal) * (1.0 - smoothstep(0.48, 0.64, diagonal));

                float2 grid = input.uv * float2(22.0, 38.0);
                float2 cell = floor(grid);
                float2 cellPoint = frac(grid) - 0.5;
                float randomValue = RandomCell(cell);
                float sparkleMask = step(0.82, randomValue);
                float sparkleShape = smoothstep(0.13, 0.0, length(cellPoint));
                float anglePhase = dot(viewTilt, float2(7.3, 9.1));
                float angleResponse = 0.12 + 0.88 * pow(saturate(sin(anglePhase + randomValue * 18.0)), 8.0);
                float sparkle = sparkleMask * sparkleShape * angleResponse;

                float legendaryEnabled = step(2.5, _EffectMode);
                float sparkleEnabled = step(0.5, _EffectMode) * (1.0 - legendaryEnabled);
                float glossAlpha = (0.035 + angleGloss * 0.13 + angleBand * 0.22) * _Intensity;
                float sparkleAlpha = sparkle * sparkleEnabled * 0.78 * _Intensity;

                // Legendary: large four-point stars and long crystal shards.
                // Their highlights and sweep positions respond only to the card viewing angle.
                float2 legendaryGrid = input.uv * float2(10.0, 18.0);
                float2 legendaryCell = floor(legendaryGrid);
                float2 legendaryPoint = frac(legendaryGrid) - 0.5;
                float legendaryRandom = RandomCell(legendaryCell + 37.0);
                float legendaryMask = step(0.72, legendaryRandom);

                float rotation = legendaryRandom * 6.2831853;
                float rotationSin = sin(rotation);
                float rotationCos = cos(rotation);
                float2 rotatedPoint = float2(
                    legendaryPoint.x * rotationCos - legendaryPoint.y * rotationSin,
                    legendaryPoint.x * rotationSin + legendaryPoint.y * rotationCos);

                float verticalRay = smoothstep(0.055, 0.0, abs(rotatedPoint.x))
                    * smoothstep(0.46, 0.08, abs(rotatedPoint.y));
                float horizontalRay = smoothstep(0.055, 0.0, abs(rotatedPoint.y))
                    * smoothstep(0.34, 0.06, abs(rotatedPoint.x));
                float diamondCore = smoothstep(0.18, 0.015,
                    abs(rotatedPoint.x) + abs(rotatedPoint.y));
                float crystalStar = saturate(max(verticalRay, horizontalRay) + diamondCore);

                float legendaryPhase = dot(viewTilt, float2(9.7, 12.3));
                float legendaryResponse = 0.08 + 0.92
                    * pow(saturate(sin(legendaryPhase + legendaryRandom * 21.0)), 10.0);
                float legendarySparkle = legendaryMask * crystalStar * legendaryResponse;

                float legendarySweepCoordinate = frac(
                    input.uv.x * 0.62 - input.uv.y * 0.26
                    + dot(viewTilt, float2(0.58, -0.44)));
                float legendarySweepA = smoothstep(0.38, 0.46, legendarySweepCoordinate)
                    * (1.0 - smoothstep(0.46, 0.54, legendarySweepCoordinate));
                float legendarySweepB = smoothstep(0.62, 0.67, legendarySweepCoordinate)
                    * (1.0 - smoothstep(0.67, 0.72, legendarySweepCoordinate));
                float legendarySweep = saturate(legendarySweepA + legendarySweepB * 0.62);

                float legendaryAlpha = (legendarySparkle * 0.92
                    + legendarySweep * 0.28 + angleGloss * 0.12)
                    * legendaryEnabled * _Intensity;
                float alpha = saturate(glossAlpha * (1.0 - legendaryEnabled)
                    + sparkleAlpha + legendaryAlpha);

                float3 regularColor = _Tint.rgb
                    * (0.8 + angleBand * 0.5 + sparkle * 0.65);
                float3 legendaryColor = lerp(
                    _Tint.rgb,
                    float3(0.88, 0.98, 1.0),
                    saturate(legendarySparkle + legendarySweep * 0.55));
                float3 color = lerp(regularColor, legendaryColor, legendaryEnabled);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}