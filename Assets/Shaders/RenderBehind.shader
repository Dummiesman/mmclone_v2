Shader "Custom/RenderBehindUnlit" {
    Properties{
        _MainTex("Base (RGB)", 2D) = "white" {}
    }

        SubShader{
            Tags{ "Queue" = "Background-1" "RenderType" = "Transparent" }

            Cull Off
            ZWrite Off
            Lighting Off
            Fog { Mode Off }

            Blend SrcAlpha OneMinusSrcAlpha

            Pass {
                SetTexture[_MainTex]
            }
    }

}


