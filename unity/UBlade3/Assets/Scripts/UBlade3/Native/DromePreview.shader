// The N2 drome preview surface: the original 24 bit texture MODULATEd by the vertex colour the
// renderer baked from the shipped material and palette records, exactly as D3DRend::DrawFace
// (HYPERX 0x004328c0) blended its texture stage and as SWRend::DrawFace (HYPERX 0x00496e00) wrote
// the palette colour on the software path. Unlit: the original lighting is already in the vertex
// colour, so Unity lights and shadows must not add anything.
Shader "UBlade3/DromePreview"
{
    Properties
    {
        _MainTex ("Original texture (white for untextured faces)", 2D) = "white" {}
        // The original renderers cull a back facing single sided face. The property exists so the
        // preview's debug captures can show both sides when proving the winding; shipped previews
        // leave it at Back.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }
        Cull [_Cull]
        Lighting Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return tex2D(_MainTex, input.uv) * input.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
