// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Custom/Cutout Hybrid" {
    Properties{
        _Color("Main Color", Color) = (1,1,1,1)
        _MainTex("Base (RGB) Trans (A)", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5
    }

        SubShader{
        Tags{ "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        LOD 200

            Pass{
            ZTest GEqual
        }

        CGPROGRAM
#pragma surface surf Lambert alpha:fade

        sampler2D _MainTex;
    fixed4 _Color;
    float _Cutoff;

    struct Input {
        float2 uv_MainTex;
    };

    void surf(Input IN, inout SurfaceOutput o) {
        fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
        o.Albedo = c.rgb;
        if (c.a < _Cutoff) {
            o.Alpha = 0.f;
        }
        else {
            o.Alpha = c.a;
        }
    }
    ENDCG
    }

        Fallback "Legacy Shaders/Transparent/VertexLit"
}
