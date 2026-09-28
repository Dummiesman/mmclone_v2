Shader "Lights/LightGlow"
{
    Properties
    {
        _MainTex ("Glow Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One (additive)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            struct GlowInstance
            {
                float3 position;
                float  size;
                float4 color;
            };

            StructuredBuffer<GlowInstance> _Glows;
            sampler2D _MainTex;

            // Corner offsets in (right, up) units.
            // Original fan: (-1,-1) (1,-1) (1,1) (-1,1) -> triangles 0,1,2 and 0,2,3
            static const float2 kCorners[6] =
            {
                float2(-1, -1), float2( 1, -1), float2( 1,  1),
                float2(-1, -1), float2( 1,  1), float2(-1,  1)
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                GlowInstance g = _Glows[iid];
                float2 c = kCorners[vid];

                // Camera right/up in world space (rows of the view matrix),
                // same vectors the original read out of sm_Modelview.
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up    = UNITY_MATRIX_V[1].xyz;

                float3 worldPos = g.position + (right * c.x + up * c.y) * g.size;

                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                // Original: S 0->1 along right, T 0->1 along up, with D3D's T=0 at the image top.
                // Unity's V=0 is the image bottom, so flip to keep the texture orientation identical.
                o.uv = float2(c.x * 0.5 + 0.5, 0.5 - c.y * 0.5);
                o.color = g.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
}
