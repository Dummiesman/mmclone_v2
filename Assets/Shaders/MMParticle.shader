Shader "Custom/MMParticle" {
	Properties{
		_MainTex("Particle Texture", 2D) = "white" {}
		_InvFade("Soft Particles Factor", Range(0.01,3.0)) = 1.0

		// non-instanced fallback, per-particle path only. ignored by the instanced and
		// batched paths, which carry this data per instance / per vertex instead.
		_Color("Tint", Color) = (1,1,1,1)
		_UvBlock("UV Block (xy offset, zw scale)", Vector) = (0,0,1,1)
	}

		Category{
			Tags { "Queue" = "Transparent+1" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
			Blend SrcAlpha OneMinusSrcAlpha
			Cull Off Lighting Off ZWrite Off

			SubShader {
				Pass {

					CGPROGRAM
					#pragma vertex vert
					#pragma fragment frag
					#pragma target 2.0
					#pragma multi_compile_particles
					#pragma multi_compile_fog
					#pragma multi_compile_instancing

					// batched fallback: the mesh is one pre-built world-space buffer, so the
					// billboard pivot arrives in POSITION and the spun corner offset in
					// TEXCOORD1 instead of coming from unity_ObjectToWorld.
					// multi_compile_local rather than shader_feature_local - the fallback
					// material is made at runtime, so a shader_feature variant would be
					// stripped out of the build.
					#pragma multi_compile_local _ MMPARTICLE_BATCHED

					#include "UnityCG.cginc"

					sampler2D _MainTex;

					struct appdata_t {
						float4 vertex : POSITION;
						fixed4 color : COLOR;
						float2 texcoord : TEXCOORD0;
					#ifdef MMPARTICLE_BATCHED
						float3 offset : TEXCOORD1;
					#endif
						UNITY_VERTEX_INPUT_INSTANCE_ID
					};

					struct v2f {
						float4 vertex : SV_POSITION;
						fixed4 color : COLOR;
						float2 texcoord : TEXCOORD0;
						UNITY_FOG_COORDS(1)
						#ifdef SOFTPARTICLES_ON
						float4 projPos : TEXCOORD2;
						#endif
						UNITY_VERTEX_OUTPUT_STEREO
					};

					float4 _MainTex_ST;
					float _Alphas[1023];
					float4 _UVs[1023];
					float4 _Colors[1023];

					fixed4 _Color;
					float4 _UvBlock;

					v2f vert(appdata_t v)
					{
						v2f o;
						UNITY_SETUP_INSTANCE_ID(v);
						UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

						// billboard mesh towards camera
#ifdef MMPARTICLE_BATCHED
						// same trick, but the pivot is the vertex itself (object matrix is
						// identity for the batch) and the local vertex is already spun and
						// scaled on the CPU, in the same view-space axes the line below adds it to
						float4 viewPos = mul(UNITY_MATRIX_V, float4(v.vertex.xyz, 1)) + float4(v.offset.xyz, 0);
#else
						float3 vpos = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
						float4 worldCoord = float4(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23, 1);
						float4 viewPos = mul(UNITY_MATRIX_V, worldCoord) + float4(vpos, 0);
#endif
						float4 outPos = mul(UNITY_MATRIX_P, viewPos);
						o.vertex = outPos;

#ifdef SOFTPARTICLES_ON
						o.projPos = ComputeScreenPos(o.vertex);
						COMPUTE_EYEDEPTH(o.projPos.z);
#endif

						// batched is tested first on purpose. this path draws through DrawMesh
						// with no per-instance arrays bound, so if auto-instancing ever turns
						// INSTANCING_ON on for it, the branch below would read a zeroed _Colors
						// and every particle would come out at alpha 0
#if defined(MMPARTICLE_BATCHED)
						// tile and tint are baked into the buffer
						o.color = v.color;
						o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
#elif defined(UNITY_INSTANCING_ENABLED)
						float alpha = _Alphas[unity_InstanceID];
						
						o.color = _Colors[unity_InstanceID];
						o.color.a *= alpha;
						

						float4 tcm = _UVs[unity_InstanceID];
						float2 tc = float2((v.texcoord.x * tcm.z) + tcm.x, (v.texcoord.y * tcm.w) + tcm.y);
						o.texcoord = TRANSFORM_TEX(tc,_MainTex);
#else
						// one draw per particle - tile and tint arrive on a property block
						float2 tc = float2((v.texcoord.x * _UvBlock.z) + _UvBlock.x, (v.texcoord.y * _UvBlock.w) + _UvBlock.y);
						o.texcoord = TRANSFORM_TEX(tc,_MainTex);
						o.color = _Color;
#endif

						UNITY_TRANSFER_FOG(o,o.vertex);
						return o;
					}

					UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
					float _InvFade;

					fixed4 frag(v2f i) : SV_Target
					{
						#ifdef SOFTPARTICLES_ON
						float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
						float partZ = i.projPos.z;
						float fade = saturate(_InvFade * (sceneZ - partZ));
						i.color.a *= fade;
						#endif

						fixed4 col = 1.0f * i.color * tex2D(_MainTex, i.texcoord);
						UNITY_APPLY_FOG(i.fogCoord, col);
						return col;
					}
					ENDCG
				}
			}
		}
}
