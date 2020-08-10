using UnityEngine.Assertions;
using UnityEngine;

namespace ComputeShader_utils {

    #region threads
    public struct GPUThreads{
        public int x;
        public int y;
        public int z;

        public GPUThreads(uint x, uint y, uint z){
            this.x = (int)x;
            this.y = (int)y;
            this.z = (int)z;
        }

        public void InitialCheck() {
            Assert.IsTrue(SystemInfo.graphicsShaderLevel >= 50, "Under the DirectCompute5.0 (DX11 GPU) doesn't work : StableFluid");
            Assert.IsTrue(x * y * z <= DirectCompute5_0.MAX_PROCESS, "Resolution is too heigh : Stablefluid");
            Assert.IsTrue(x <= DirectCompute5_0.MAX_X, "THREAD_X is too large : StableFluid");
            Assert.IsTrue(y <= DirectCompute5_0.MAX_Y, "THREAD_Y is too large : StableFluid");
            Assert.IsTrue(z <= DirectCompute5_0.MAX_Z, "THREAD_Z is too large : StableFluid");
        }
    }

    public static class DirectCompute5_0{
        //Use DirectCompute 5.0 on DirectX11 hardware.
        public const int MAX_THREAD   = 1024;
        public const int MAX_X        = 1024;
        public const int MAX_Y        = 1024;
        public const int MAX_Z        = 64;
        public const int MAX_DISPATCH = 65535;
        public const int MAX_PROCESS  = MAX_DISPATCH * MAX_THREAD;
    }
    #endregion threads


    public static class RenderTexture_Utils {

        public static RenderTexture CreateRenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTexture rt = null, FilterMode filterMode = FilterMode.Point) {

            if (rt != null) {
                if (rt.width == width && rt.height == height) return rt;
            }

            ReleaseRenderTexture(rt);
            rt = new RenderTexture(width, height, depth, format);
            rt.enableRandomWrite = true;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = filterMode; //デフォルトはPoint
            rt.Create();
            ClearRenderTexture(rt, Color.clear);
            return rt;
        }

        public static void ReleaseRenderTexture(RenderTexture rt) {
            if (rt == null) return;

            rt.Release();
            Object.Destroy(rt);
        }

        public static void swapRenderTexture(ref RenderTexture ping, ref RenderTexture pong) {
            var temp = ping;
            ping = pong;
            pong = temp;
        }

        public static void ClearRenderTexture(RenderTexture target, Color bg) {
            var active = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, bg);
            RenderTexture.active = active;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void ReleaseLog() {
            Debug.Log("Buffer released");
        }
    }

    public static class ComputeBuffer_Utils {


    }

}