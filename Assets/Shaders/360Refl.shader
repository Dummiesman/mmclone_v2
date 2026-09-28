// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Custom/Vehicle Shader" {
	Properties{
		_Color("Main Color", Color) = (1,1,1,1)
        _AmbientColor("Ambient Color", Color) = (1,1,1,1)
        _SpecColor("Specular Color", Color) = (1,1,1,1)
        _EmissiveColor("Emissive Color", Color) = (0,0,0,0)
        _Shininess("Shininess", Range(0.03, 1)) = 1
        _Reflection("Reflection Intensity", Range(.0, 1.0)) = 0.5
		_MainTex("Main Texture (RGB)", 2D) = "white" {}
		_ReflTex("Reflection Texture (RGB)", 2D) = "white" {}
		_DamageTex("Damage Texture (RGB)", 2D) = "black" {}
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

        v.vertex.xyz -= (normalize(v.vertex.xyz) * min(1 - v.color.a, 0.05)); //allow some 3d damage up to 5% :)
		float4 mPosition = mul(unity_ObjectToWorld,float4(v.vertex.xyz, 1.0));
        o.pos = mPosition;

        o.color = v.color;
        o.normal = v.normal;

        o.viewDir = mul(modelMatrix, o.pos).xyz - _WorldSpaceCameraPos;
        o.normalDir = normalize(mul(float4(o.normal, 0.0), modelMatrixInverse).xyz);
	}

	uniform sampler2D _MainTex;
	uniform sampler2D _ReflTex;
	uniform sampler2D _DamageTex;
	uniform half _Reflection;
	uniform float4 _Color;
    uniform half _Shininess;
    uniform float4 _EmissiveColor;
    uniform float4 _AmbientColor;
    const float Epsilon = 0.0000001;

	void surf(Input IN, inout SurfaceOutput o)
	{
        /*
        debug:
        o.Albedo = IN.viewDir;// (reflect(IN.viewDir, normalize(IN.normalDir)), 1.0);
        o.Alpha = 1.0;
        o.Gloss = _SpecColor.rgb;
        o.Specular = _Shininess;
        o.Emission = _EmissiveColor;
        return;*/

        float damageIntensity = 1.0 - IN.color.a;

        //diffuse
        float4 diffuseTexColor = tex2D(_MainTex, float2(IN.uv_MainTex.x, IN.uv_MainTex.y));
        float3 color = diffuseTexColor.rgb * IN.color.a;

        //apply damage
		color += tex2D(_DamageTex, float2(IN.uv_MainTex.x, IN.uv_MainTex.y)).rgb * damageIntensity;
        
        //apply reflection
        float3 reflectionColor = float3(0, 0, 0);
        if (_Reflection > Epsilon && damageIntensity < (1.0 - Epsilon) && diffuseTexColor.a > 0) {
            float3 reflectedDir = reflect(IN.viewDir, normalize(IN.normalDir));
            float m = 2. * sqrt(pow(reflectedDir.x, 2.) + pow(reflectedDir.y, 2.) + pow(reflectedDir.z + 1., 2.));
            float2 vN = reflectedDir.xy / m + .5;

            reflectionColor = tex2D(_ReflTex, float2(vN.x, 1 - vN.y)).rgb * clamp(_Reflection - (damageIntensity * _Reflection), 0, 1);
            reflectionColor *= diffuseTexColor.a;
        }

		//write to surf
        o.Albedo = ((color.rgb - unity_AmbientSky ) + (unity_AmbientSky * _AmbientColor)) * _Color;
		o.Albedo += reflectionColor.rgb;
		o.Alpha = diffuseTexColor.a;
        o.Gloss = _SpecColor.rgb;
        o.Specular = _Shininess;

        //do emission
        o.Emission = _EmissiveColor * diffuseTexColor;
        o.Albedo -= o.Emission;
	}

	ENDCG
	}
	
	FallBack "Custom/D3D7"
}