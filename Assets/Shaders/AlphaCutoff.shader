 Shader "Custom/Alpha Cutoff" {
 Properties {
     _Color ("Main Color", Color) = (1,1,1,1)
     _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
     _Cutoff ("Alpha cutoff lowend", Range(0,1)) = 0.5
     _CutoffHigh ("Alpha cutoff highend", Range(0,1)) = 0.5
 }
 
 SubShader {
     Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
     LOD 200

    CGPROGRAM
        #pragma surface surf Lambert alpha
        sampler2D _MainTex;
        fixed4 _Color;
        float _Cutoff;
        float _CutoffHigh;
        
        struct Input {
            float2 uv_MainTex;
        };
        
        void surf (Input IN, inout SurfaceOutput o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float ca = tex2D(_MainTex, IN.uv_MainTex).a;
            o.Albedo = c.rgb;
            
            if(ca > _Cutoff && ca < _CutoffHigh){
                o.Alpha = 1.0;
            }
            else
            {
                o.Alpha = ca;
            }
        }
    ENDCG

     
 }
 SubShader {
    Tags {"Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout"}
    LOD 200
            ZWrite On
        ColorMask 0
        ZTest Always
    CGPROGRAM
        #pragma surface surf Lambert alphatest:_CutoffHigh
        sampler2D _MainTex;
        fixed4 _Color;
        float _Cutoff;
        

        struct Input {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutput o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            float ca = tex2D(_MainTex, IN.uv_MainTex).a;
            o.Albedo = c.rgb;
            
            if(ca < _Cutoff && ca > 0.85){
                o.Alpha = 1.0;
            }
            else
            {
                o.Alpha = ca;
            }
           
        }
    ENDCG
     
 }
 
 Fallback "Transparent/VertexLit"
 }