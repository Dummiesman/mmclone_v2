// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Custom/MC PSDL" {
	Properties{
		_Color("Main Color", Color) = (1,1,1,1)
    _ReflectionColor("Reflection Color", Color) = (1,1,1,1)
    _Reflection("Reflection Intensity", Range(.0, 1.0)) = 0.5
		_MainTex("Main Texture (RGB)", 2D) = "white" {}
		_ReflTex("Reflection Texture (RGB)", 2D) = "white" {}
	}

	SubShader {
        Tags{ "Queue" = "Geometry" "RenderType" ="Transparent" }
		LOD 200
            Blend SrcAlpha OneMinusSrcAlpha
            //ZWrite Off

		CGPROGRAM
#pragma target 4.0
#pragma surface surf BlinnPhong vertex:vert keepalpha

	struct Input {
		float2 uv_MainTex;
		float4 pos;
		float3 normal;
		float4 color : COLOR;
        float3 normalDir : TEXCOORD0;
        float3 viewDir : TEXCOORD1;
	};

	void vert(inout appdata_full v, out Input o)
	{
		UNITY_INITIALIZE_OUTPUT(Input, o);

        float4x4 modelMatrix = unity_ObjectToWorld;
        float4x4 modelMatrixInverse = unity_WorldToObject;

		    float4 mPosition = mul(unity_ObjectToWorld,float4(v.vertex.xyz, 1.0));
        o.pos = mPosition;

        o.color = v.color;
        o.normal = v.normal;

        o.viewDir = mul(modelMatrix, o.pos).xyz - _WorldSpaceCameraPos;
        o.normalDir = normalize(mul(float4(o.normal, 0.0), modelMatrixInverse).xyz);
	}

	uniform sampler2D _MainTex;
	uniform sampler2D _ReflTex;
	uniform half _Reflection;
	uniform float4 _Color;
  uniform float4 _ReflectionColor;
  const float Epsilon = 0.0000001;

	void surf(Input IN, inout SurfaceOutput o)
	{
        //diffuse
        float4 diffuseTexColor = tex2D(_MainTex, float2(IN.uv_MainTex.x, IN.uv_MainTex.y));

        //apply reflection
        float3 reflectionColor = float3(0, 0, 0);
        if (_Reflection > Epsilon) {
            float3 reflectedDir = reflect(IN.viewDir, normalize(IN.normalDir));
            float m = 2. * sqrt(pow(reflectedDir.x, 2.) + pow(reflectedDir.y, 2.) + pow(reflectedDir.z + 1., 2.));
            float2 vN = reflectedDir.xy / m + .5;
            
            reflectionColor = tex2D(_ReflTex, float2(vN.x, 1 - vN.y)).rgb * _Reflection;
            reflectionColor *= diffuseTexColor.a;
            reflectionColor *= _ReflectionColor;
        }

        //write to surf
        o.Albedo = diffuseTexColor.rgb * _Color;
        o.Albedo += reflectionColor.rgb;
        o.Alpha = 1.0;
	}

	ENDCG
	}
	
	FallBack "Custom/D3D7"
}