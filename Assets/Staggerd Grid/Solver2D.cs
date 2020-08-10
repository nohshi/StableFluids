using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using ComputeShader_utils;
using static ComputeShader_utils.RenderTexture_Utils;

//staggerdでのstbale fluids
namespace Staggerd {

    public class Solver2D : MonoBehaviour {

        #region Variables
        protected enum ComputeKernels {
            AddSourceVelocity,
            AddSourceDensity,

            AdvectVelocityX,
            AdvectVelocityY,

            DiffuseVelocityX,
            DiffuseVelocityY,

            ApplySourceVelocity,
            ApplySourceDensity,

            ProjectStep1,
            ProjectStep2,
            ProjectStep3X,
            ProjectStep3Y,
            
            DiffuseDensity,
            AdvectDensity,
            DissipateDensity, //徐々に消すやつ

            Draw //描画用テクスチャに描き込み
        }
        
        protected Dictionary<ComputeKernels, int> kernelMap = new Dictionary<ComputeKernels, int>();
        protected GPUThreads    gpuThreads;

        protected RenderTexture sourceVelocityTex;
        protected RenderTexture sourceDensityTex;

        protected RenderTexture vxTex;
        protected RenderTexture prevVxTex;
        protected RenderTexture vyTex;
        protected RenderTexture prevVyTex;

        protected RenderTexture pressureTex; //velocity deffusion tex
        protected RenderTexture divergenceTex;

        protected RenderTexture densityTex;
        protected RenderTexture prevDensityTex;

        protected RenderTexture canvasTex; //描画用のテクスチャ

        protected int sourceVelocityId, sourceDensityId, vxId, prevVxId, vyId, prevVyId,
                      pressureId, divergenceId, densityId, prevDensityId, canvasId,
                      diffId, viscId, dtId, velocityCoefId, densityCoefId, mousePosId, mouseVelId, mouseRadiusId;
        protected int width, height;

        protected Vector2 prevMousePos;
        protected float deltaTime;

        [SerializeField]
        protected ComputeShader computeShader;

        [SerializeField]
        protected float mouseRadius = 0.05f;

        [SerializeField]
        protected float mouseVelocityAmp = 10f;

        [SerializeField]
        protected int jacob_iterate = 10; //ヤコビ法の反復回数

        [SerializeField]
        protected float diff = 0.01f; //拡散係数

        [SerializeField]
        protected float visc = 0.8f; //粘性係数

        [SerializeField]
        protected float velocityCoef = 100f;

        [SerializeField]
        protected float densityCoef = 4f;

        public GameObject plane;
        #endregion Variables

        #region Mono
        
        void Start() {
            Initialize();
        }

        
        void Update() {
            deltaTime = Time.deltaTime;

            //if (width != Screen.width || height != Screen.height) InitializeComputeShader();
            computeShader.SetFloat(diffId, diff);
            computeShader.SetFloat(viscId, visc);
            computeShader.SetFloat(dtId, deltaTime);
            computeShader.SetFloat(velocityCoefId, velocityCoef);
            computeShader.SetFloat(densityCoefId, densityCoef);
            
            Graphics.CopyTexture(vxTex, prevVxTex);
            Graphics.CopyTexture(vyTex, prevVyTex);
            Graphics.CopyTexture(densityTex, prevDensityTex);

            //マウスイベントのようなもの
            mousePressed();
            mouseHolded();

            //
            advect();
            applySource();
            diffuse();
            project();
            

            Draw();
        }

        void OnDestroy(){
            CleanUpRT();
        }

        #endregion Mono

        #region Initialization

        protected void Initialize() {
            uint threadX, threadY, threadZ;

            kernelMap = System.Enum.GetValues(typeof(ComputeKernels))
                .Cast<ComputeKernels>()
                .ToDictionary(t => t, t => computeShader.FindKernel(t.ToString()));

            computeShader.GetKernelThreadGroupSizes(kernelMap[ComputeKernels.Draw], out threadX, out threadY, out threadZ);
            gpuThreads = new GPUThreads(threadX, threadY, threadZ);
            gpuThreads.InitialCheck();

            //property to id
            //tex
            sourceVelocityId = Shader.PropertyToID("sourceVelocity");
            sourceDensityId = Shader.PropertyToID("sourceDensity");
            vxId = Shader.PropertyToID("vx");
            vyId = Shader.PropertyToID("vy");
            prevVxId = Shader.PropertyToID("prevVx");
            prevVyId = Shader.PropertyToID("prevVy");
            pressureId = Shader.PropertyToID("pressure");
            divergenceId = Shader.PropertyToID("divergence");
            densityId = Shader.PropertyToID("density");
            prevDensityId = Shader.PropertyToID("prevDensity");
            canvasId = Shader.PropertyToID("canvas");
            //uniform
            diffId = Shader.PropertyToID("u_diff");
            viscId = Shader.PropertyToID("u_visc");
            dtId = Shader.PropertyToID("u_dt");
            velocityCoefId = Shader.PropertyToID("u_velocityCoef");
            densityCoefId = Shader.PropertyToID("u_densityCoef");
            mousePosId = Shader.PropertyToID("u_mousePos");
            mouseVelId = Shader.PropertyToID("u_mouseVel");
            mouseRadiusId = Shader.PropertyToID("u_mouseRadius");

            InitializeComputeShader();
            
            //テクスチャとしてセット
            plane.GetComponent<Renderer>().material.mainTexture = canvasTex;
        }

        protected virtual void InitializeComputeShader() {
            //var ls = plane.GetComponent<Transform>().localScale;

            width        = 512;
            height       = 512;

            sourceVelocityTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.RGFloat, sourceVelocityTex);
            sourceDensityTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.RFloat, sourceDensityTex);

            vxTex = CreateRenderTexture(width+1, height, 0, RenderTextureFormat.RFloat, vxTex);
            vyTex = CreateRenderTexture(width, height+1, 0, RenderTextureFormat.RFloat, vyTex);
            prevVxTex = CreateRenderTexture(width+1, height, 0, RenderTextureFormat.RFloat, prevVxTex);
            prevVyTex = CreateRenderTexture(width, height+1, 0, RenderTextureFormat.RFloat, prevVyTex);

            pressureTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.RFloat, pressureTex);
            divergenceTex  = CreateRenderTexture(width, height, 0, RenderTextureFormat.RFloat, divergenceTex);

            densityTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.RFloat, densityTex);
            prevDensityTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.RFloat, prevDensityTex);

            canvasTex = CreateRenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, canvasTex);
        }

        #endregion Initialization

        #region mouse
        protected void mousePressed() {
            if (!Input.GetMouseButtonDown(0)) return;
        }

        protected void mouseHolded() {
            if (!Input.GetMouseButton(0)) return;

            addSource();
        }
        #endregion mouse

        #region kernel functions
        protected void addSource() {
            Vector2 mouse = Input.mousePosition;
            Vector2 mouseVel = prevMousePos - mouse;
            mouseVel *= mouseVelocityAmp * deltaTime; //ここ係数調整したりclampしたほうがいいかもしれん
            //Debug.Log(mouseVel);
            prevMousePos = mouse;

            mouse = Camera.main.ScreenToViewportPoint(mouse); //1~0で渡す
            
            mouse.x -= 7/16.0f*0.5f;
            mouse.x *= 16/9.0f;
            //Debug.Log(mouse);

            mouseVel = Vector2.ClampMagnitude(mouseVel, 1);
            
            computeShader.SetVector(mousePosId, mouse);
            computeShader.SetVector(mouseVelId, -mouseVel);
            computeShader.SetFloat(mouseRadiusId, mouseRadius);

            computeShader.SetTexture(kernelMap[ComputeKernels.AddSourceDensity], sourceDensityId, sourceDensityTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.AddSourceDensity], Mathf.CeilToInt(sourceDensityTex.width / gpuThreads.x), Mathf.CeilToInt(sourceDensityTex.height / gpuThreads.y), 1);
            
            computeShader.SetTexture(kernelMap[ComputeKernels.AddSourceVelocity], sourceVelocityId, sourceVelocityTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.AddSourceVelocity], Mathf.CeilToInt(sourceVelocityTex.width / gpuThreads.x), Mathf.CeilToInt(sourceVelocityTex.height / gpuThreads.y), 1);
        }

        protected void applySource() {
            int threadGroupSizeX_d = Mathf.CeilToInt(sourceDensityTex.width / gpuThreads.x);
            int threadGroupSizeY_d = Mathf.CeilToInt(sourceDensityTex.height / gpuThreads.y);

            computeShader.SetTexture(kernelMap[ComputeKernels.ApplySourceDensity], sourceDensityId, sourceDensityTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ApplySourceDensity], densityId, densityTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.ApplySourceDensity], threadGroupSizeX_d, threadGroupSizeY_d, 1);

            computeShader.SetTexture(kernelMap[ComputeKernels.ApplySourceVelocity], sourceVelocityId, sourceVelocityTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ApplySourceVelocity], vxId, vxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ApplySourceVelocity], vyId, vyTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.ApplySourceVelocity], threadGroupSizeX_d, threadGroupSizeY_d, 1);
        }

        protected void advect() {
            int threadGroupSizeX_vx = Mathf.CeilToInt(vxTex.width / gpuThreads.x);
            int threadGroupSizeY_vx = Mathf.CeilToInt(vxTex.height / gpuThreads.y);

            int threadGroupSizeX_vy = Mathf.CeilToInt(vyTex.width / gpuThreads.x);
            int threadGroupSizeY_vy = Mathf.CeilToInt(vyTex.height / gpuThreads.y);

            int threadGroupSizeX_d = Mathf.CeilToInt(densityTex.width / gpuThreads.x);
            int threadGroupSizeY_d = Mathf.CeilToInt(densityTex.height / gpuThreads.y);

            #region advectVelocity
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityX], vxId, vxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityX], prevVxId, prevVxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityX], prevVyId, prevVyTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.AdvectVelocityX], threadGroupSizeX_vx, threadGroupSizeY_vx, 1);

            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityY], vyId, vyTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityY], prevVxId, prevVxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectVelocityY], prevVyId, prevVyTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.AdvectVelocityY], threadGroupSizeX_vy, threadGroupSizeY_vy, 1);
            #endregion

            #region advectDensity
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectDensity], vxId, vxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectDensity], vyId, vyTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.AdvectDensity], densityId, densityTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.AdvectDensity], threadGroupSizeX_d, threadGroupSizeY_d, 1);
            #endregion
        }

        protected void diffuse() {
            int threadGroupSizeX_vx = Mathf.CeilToInt(vxTex.width / gpuThreads.x);
            int threadGroupSizeY_vx = Mathf.CeilToInt(vxTex.height / gpuThreads.y);

            int threadGroupSizeX_vy = Mathf.CeilToInt(vyTex.width / gpuThreads.x);
            int threadGroupSizeY_vy = Mathf.CeilToInt(vyTex.height / gpuThreads.y);

            int threadGroupSizeX_d = Mathf.CeilToInt(densityTex.width / gpuThreads.x);
            int threadGroupSizeY_d = Mathf.CeilToInt(densityTex.height / gpuThreads.y);

            Graphics.CopyTexture(vxTex, prevVxTex);
            Graphics.CopyTexture(vyTex, prevVyTex);
            Graphics.CopyTexture(densityTex, prevDensityTex);

            #region velocity
            for (int i = 0; i < jacob_iterate; i++) {
                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseVelocityX], vxId, vxTex);
                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseVelocityX], prevVxId, prevVxTex);
                computeShader.Dispatch(kernelMap[ComputeKernels.DiffuseVelocityX], threadGroupSizeX_vx, threadGroupSizeY_vx, 1);

                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseVelocityX], vyId, vyTex);
                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseVelocityX], prevVyId, prevVyTex);
                computeShader.Dispatch(kernelMap[ComputeKernels.DiffuseVelocityX], threadGroupSizeX_vy, threadGroupSizeY_vy, 1);
            }
            #endregion

            #region density
            for (int i = 0; i < jacob_iterate; i++) {
                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseDensity], prevDensityId, prevDensityTex);
                computeShader.SetTexture(kernelMap[ComputeKernels.DiffuseDensity], densityId, densityTex);
                computeShader.Dispatch(kernelMap[ComputeKernels.DiffuseDensity], threadGroupSizeX_d, threadGroupSizeY_d, 1);
            }
            #endregion
        }
        
        protected void project() {
            int threadGroupSizeX_vx = Mathf.CeilToInt(vxTex.width / gpuThreads.x);
            int threadGroupSizeY_vx = Mathf.CeilToInt(vxTex.height / gpuThreads.y);

            int threadGroupSizeX_vy = Mathf.CeilToInt(vyTex.width / gpuThreads.x);
            int threadGroupSizeY_vy = Mathf.CeilToInt(vyTex.height / gpuThreads.y);

            int threadGroupSizeX_d = Mathf.CeilToInt(densityTex.width / gpuThreads.x);
            int threadGroupSizeY_d = Mathf.CeilToInt(densityTex.height / gpuThreads.y);

            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep1], divergenceId, divergenceTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep1], vxId, vxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep1], vyId, vyTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.ProjectStep1], threadGroupSizeX_d, threadGroupSizeY_d, 1);

            for (int i = 0; i < jacob_iterate; i++) {
                computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep2], divergenceId, divergenceTex);
                computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep2], pressureId, pressureTex);
                computeShader.Dispatch(kernelMap[ComputeKernels.ProjectStep2], threadGroupSizeX_d, threadGroupSizeY_d, 1);
            }

            #region project3
            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep3X], pressureId, pressureTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep3X], vxId, vxTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.ProjectStep3X], threadGroupSizeX_vx, threadGroupSizeY_vx, 1);

            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep3Y], pressureId, pressureTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.ProjectStep3Y], vyId, vyTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.ProjectStep3Y], threadGroupSizeX_vy, threadGroupSizeY_vy, 1);
            #endregion
        }

        protected void Draw() {
            #region draw
            computeShader.SetTexture(kernelMap[ComputeKernels.Draw], vxId, vxTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.Draw], vyId, vyTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.Draw], densityId, densityTex);
            computeShader.SetTexture(kernelMap[ComputeKernels.Draw], canvasId, canvasTex);
            computeShader.Dispatch(kernelMap[ComputeKernels.Draw], Mathf.CeilToInt(canvasTex.width / gpuThreads.x), Mathf.CeilToInt(canvasTex.height / gpuThreads.y), 1);
            #endregion
        }
        #endregion

        #region release

        void CleanUpRT(){
            ReleaseRenderTexture(sourceVelocityTex);
            ReleaseRenderTexture(sourceDensityTex);

            ReleaseRenderTexture(vxTex);
            ReleaseRenderTexture(vyTex);

            ReleaseRenderTexture(prevVxTex);
            ReleaseRenderTexture(prevVyTex);

            ReleaseRenderTexture(pressureTex);
            ReleaseRenderTexture(divergenceTex);

            ReleaseRenderTexture(densityTex);
            ReleaseRenderTexture(prevDensityTex);

            ReleaseRenderTexture(canvasTex);

            ReleaseLog();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        void ReleaseLog() {
            Debug.Log("Buffer released");
        }

        #endregion
    }

}