Shader "Custom/D3D7"
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
    }

    SubShader
    {
        //RenderType is a SubShader tag - it was a no-op inside the Pass blocks.
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            //OPAQUE PASS
            Lighting On
            ZWrite On
            CGPROGRAM
            #pragma vertex MMD3D7Vert
            #pragma fragment MMD3D7FragOpaque
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile __ UNLIT
            #pragma multi_compile __ SHADOWMAP
            #include "MMD3D7.cginc"
            ENDCG
        }

        Pass
        {
            //TRANSPARENT PASS
            Blend SrcAlpha OneMinusSrcAlpha
            Lighting On
            ZWrite Off
            CGPROGRAM
            #pragma vertex MMD3D7Vert
            #pragma fragment MMD3D7FragTransparent
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile __ UNLIT
            #pragma multi_compile __ SHADOWMAP
            #include "MMD3D7.cginc"
            ENDCG
        }
    }

    Fallback "VertexLit"
}
