Shader "Custom/VehicleShaderD3D7_Opaque"
{
    Properties
    {
        _Color("Main Color", Color) = (1,1,1,1)
        _AmbientColor("Ambient Color", Color) = (1,1,1,1)
        _SpecColor("Specular Color", Color) = (1,1,1,1)
        _EmissiveColor("Emissive Color", Color) = (0,0,0,0)
        _Shininess("Shininess", Range(0.03, 1)) = 1
        _AlphaClipThreshold("AlphaClipThreshold", Range(0.03, 1)) = 0.9
        _MainTex("Main Texture (RGB)", 2D) = "white" {}
        _DamageTex("Damage Texture (RGB)", 2D) = "black" {}
        _Reflection("Reflection Intensity", Range(.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Pass
        {
            //OPAQUE PASS
            Lighting On //remember this! otherwise unity_LightPosition[0] doesnt seem to work
            ZWrite On
            CGPROGRAM
            #pragma vertex MMVehicleVert
            #pragma fragment MMVehicleFragOpaque
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "MMVehicle.cginc"
            ENDCG
        }
    }

    Fallback "VertexLit"
}
