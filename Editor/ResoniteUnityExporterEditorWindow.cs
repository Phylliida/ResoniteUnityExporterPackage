using UnityEngine;
using UnityEditor;
using ResoniteBridgeLib;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using ResoniteUnityExporterShared;
using UnityEngine.Animations;
using System.Text;
using ResoniteUnityExporter.Converters;


#if RUE_HAS_AVATAR_VRCSDK
using VRC.SDK3.Avatars.Components;
#endif
#if RUE_HAS_VRCSDK
using VRC.Core;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
#endif



namespace ResoniteUnityExporter {
   
    public class ResoniteUnityExporterEditorWindow : EditorWindow
	{
        bool ranAnyRuns = false;
        bool setupAvatarCreator = true;
        bool finalizeAvatarCreator = true;
        bool renameSlots = true;
        bool setupIK = true;
        bool sendColliders = true;
        bool sendLights = true;
        bool sendingAvatar = true;
        bool makePackage = false;
        bool includeAssetVariantsInPackage = true;

        float nearClip = 0f;
        float prevNearClip = 0f;
        Transform parentObject;
        string exportSlotName;
        UnityEngine.Object rootPipelineManager;
        Dictionary<string, string> materialMappings = new Dictionary<string, string>();
        const string LOADING_SERVER_LABEL = "Loading Info...";
        static ServerInfo_U2Res LOADING_SERVER_INFO = new ServerInfo_U2Res()
        {
            allowedToCreateInWorld = false,
            label = LOADING_SERVER_LABEL,
            worldName = "...",
        };
        static ServerInfo_U2Res serverInfo = new ServerInfo_U2Res()
        {
            allowedToCreateInWorld = false,
            label = LOADING_SERVER_LABEL,
            worldName = "...",
        };

        const string STANDALONE_LABEL = "Standalone";


        public static int TotalTransferObjectCount = 0;
        public static int PrevCurTransferObjectCount = 0;
        public static int CurTransferObjectCount = 0;
        public static double CurEstimatedMillisForEach = 0;
        public static string DebugProgressString = "";
        public static string DebugProgressStringDetail = "";
        public System.Diagnostics.Stopwatch curIterElapsed = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch elapsedTimeSending = new System.Diagnostics.Stopwatch();

        // gui styles
        int selectedTab;
        private Texture2D headViewTex;
        private bool foundHead = false;

        static List<System.Collections.IEnumerator> CoroutinesInProgress = new List<System.Collections.IEnumerator>();

        static ResoniteUnityExporterEditorWindow()
        {
            EditorApplication.update += ExecuteCoroutine;
            EditorApplication.quitting += EditorApplication_quitting;
        }


        private static void EditorApplication_quitting()
        {
            // we need to clean this up manually
            if (bridgeClient != null)
            {
                serverInfo = LOADING_SERVER_INFO;
                bridgeClient.Dispose();
                bridgeClient = null;
            }
        }

        static int iters = 0;
        // lets us run coroutines in editor, modified from https://discussions.unity.com/t/coroutine-in-editor/4970/6
        private static void ExecuteCoroutine()
        {
            if (CoroutinesInProgress.Count <= 0)
            {
                return;
            }

            for(int i = CoroutinesInProgress.Count-1; i >= 0; i--)
            {
                if(!CoroutinesInProgress[i].MoveNext())
                {
                    CoroutinesInProgress.RemoveAt(i);
                }
            }
            iters += 1;
        }

        public static ResoniteBridgeClient bridgeClient;

        static int windowWidth = 560;
        static int windowHeight = 680;

		// Add menu item named "My Custom Window" to the Window menu
		[MenuItem("ResoniteUnityExporter/Open Resonite Unity Exporter")]
		public static void ShowWindow()
		{
			// Get existing open window or if none, make a new one
            var window = EditorWindow.GetWindow(typeof(ResoniteUnityExporterEditorWindow));
            window.minSize = new UnityEngine.Vector2(windowWidth, windowHeight); // Minimum size
            window.maxSize = new UnityEngine.Vector2(windowWidth, windowHeight); // Maximum size (same as min for fixed size)
        }


        // stuff for recreating connection on reload of scripts
        void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            titleContent = new GUIContent("Resonite Unity Exporter");
            minSize = new UnityEngine.Vector2(windowWidth, windowHeight);
            maxSize = new UnityEngine.Vector2(windowWidth, windowHeight);
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
        }
        public void OnBeforeAssemblyReload()
        {
			// on script reload we need to remember to Dispose it manually
			// otherwise unity will hang
			if (bridgeClient != null)
			{
                serverInfo = LOADING_SERVER_INFO;
                bridgeClient.Dispose();
				
				bridgeClient = null;
            }
            
            // stop all running coroutines
            CoroutinesInProgress.Clear();
        }
        public void OnAfterAssemblyReload()
        {

        }

        float pollFrequencyInMilliseconds = 2000;
        System.Diagnostics.Stopwatch pollStopwatch = new System.Diagnostics.Stopwatch();
        void Update()
        {
            Repaint();

            if (CoroutinesInProgress.Count != 0)
            {
                return;
            }

            if (bridgeClient != null && bridgeClient.NumActiveConnections() > 0)
            {
                if (!pollStopwatch.IsRunning)
                {
                    pollStopwatch.Restart();
                }
                if (pollStopwatch.ElapsedMilliseconds > pollFrequencyInMilliseconds)
                {
                    // don't show status for poll coroutine
                    debugCoroutine = false;
                    CoroutinesInProgress.Add(GetServerInfo());
                    pollStopwatch.Stop();
                }
            }

        }


        bool debugCoroutine = true;

        IEnumerator<object> GetServerInfo()
        {
            if (bridgeClient != null)
            {
                OutputHolder<object> outputInfo = new ResoniteUnityExporter.OutputHolder<object>();
                var en = HierarchyLookup.Call<ServerInfo_U2Res, int>(bridgeClient, "GetServerInfo", 0, outputInfo);
                var exceptCatcher = new ExceptionSafeIterator(en);
                while(exceptCatcher.MoveNext())
                {
                    yield return exceptCatcher.Current;
                }
                if (exceptCatcher.ExceptionOccurred)
                {
                    //System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture((System.Exception)exceptCatcher.CaughtException).Throw();
                    // ignore exceptions here so as to not spam
                    ResoniteUnityExporterEditorWindow.serverInfo = LOADING_SERVER_INFO;
                }
                else
                {
                    ServerInfo_U2Res serverInfo = outputInfo.value != null
                        ? (ServerInfo_U2Res)outputInfo.value
                        : LOADING_SERVER_INFO; // we get null if disconnected
                    ResoniteUnityExporterEditorWindow.serverInfo = serverInfo;
                }
            }
        }

        // uses scene camera to capture from a given position+rot+scale
        public Texture2D CaptureFromPosition(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Vector3 scale, float nearClip, int width = 1920, int height = 1080)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return null;

            // Store original transform
            UnityEngine.Vector3 originalPos = sceneView.camera.transform.position;
            UnityEngine.Quaternion originalRot = sceneView.camera.transform.rotation;
            UnityEngine.Vector3 originalScale = sceneView.camera.transform.localScale;
            float originalNearClip = sceneView.camera.nearClipPlane;

            // Move camera
            sceneView.camera.transform.position = position;
            sceneView.camera.transform.rotation = rotation;
            sceneView.camera.transform.localScale = scale;
            sceneView.camera.nearClipPlane = nearClip;

            // Setup render texture
            RenderTexture rt = new RenderTexture(width, height, 24);
            RenderTexture prev = RenderTexture.active;
            sceneView.camera.targetTexture = rt;
            RenderTexture.active = rt;

            // Render and read
            sceneView.camera.Render();
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenshot.Apply();
        
            // Restore camera
            sceneView.camera.transform.position = originalPos;
            sceneView.camera.transform.rotation = originalRot;
            sceneView.camera.transform.localScale = originalScale;
            sceneView.camera.targetTexture = null;
            sceneView.camera.nearClipPlane = originalNearClip;
            RenderTexture.active = prev;

            // Cleanup
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return screenshot;
        }



        Texture2D CaptureViewFromHead(int width, int height, float nearClip, out bool foundHead)
        {
            if (ResoniteTransferUtils.TryGetHeadPosition(parentObject, out foundHead, out Vector3 headPosition, out GameObject headObject))
            {
                return CaptureFromPosition(
                    headPosition,
                    headObject == null ? Quaternion.identity : headObject.transform.rotation,
                    headObject == null ? new UnityEngine.Vector3(1,1,1) : headObject.transform.lossyScale, 
                    nearClip,
                    width, height);
            }
            // no head found, just do origin
            else
            {
                return CaptureFromPosition(
                    new UnityEngine.Vector3(0,0,0),
                    UnityEngine.Quaternion.identity,
                    new UnityEngine.Vector3(1,1,1),
                    nearClip,
                    width, height);
            }
        }

        void RegisterConverters(ResoniteTransferManager transferManager, bool sendColliders, bool sendLights)
		{
            // mesh renderers
            transferManager.RegisterConverter<SkinnedMeshRenderer>(SkinnedMeshRendererConverter.ConvertSkinnedMeshRenderer);
            transferManager.RegisterConverter<MeshRenderer>(MeshRendererConverter.ConvertMeshRenderer);
            // convert the avatar (when sending avatar), this does stuff like IK and avatar importer
#if RUE_HAS_VRCSDK
            transferManager.RegisterConverter<PipelineManager>(PipelineManagerConverter.ConvertPipelineManager);
            // dynamic bone chains and dynamic bone colliders
            transferManager.RegisterConverter<VRCPhysBoneCollider>(PhysBoneColliderConverter.ConvertPhysBoneCollider);
            transferManager.RegisterConverter<VRCPhysBone>(PhysBoneConverter.ConvertPhysBone);
            // constraints
            transferManager.RegisterConverter<VRCPositionConstraint>(ConstraintConverter.ConvertVRCPositionConstraint);
            transferManager.RegisterConverter<VRCRotationConstraint>(ConstraintConverter.ConvertVRCRotationConstraint);
            transferManager.RegisterConverter<VRCScaleConstraint>(ConstraintConverter.ConvertVRCScaleConstraint);
            transferManager.RegisterConverter<VRCLookAtConstraint>(ConstraintConverter.ConvertVRCLookAtConstraint);
            transferManager.RegisterConverter<VRCParentConstraint>(ConstraintConverter.ConvertVRCParentConstraint);
            transferManager.RegisterConverter<VRCAimConstraint>(ConstraintConverter.ConvertVRCAimConstraint);
#endif
            transferManager.RegisterConverter<PositionConstraint>(ConstraintConverter.ConvertPositionConstraint);
            transferManager.RegisterConverter<RotationConstraint>(ConstraintConverter.ConvertRotationConstraint);
            transferManager.RegisterConverter<ScaleConstraint>(ConstraintConverter.ConvertScaleConstraint);
            transferManager.RegisterConverter<LookAtConstraint>(ConstraintConverter.ConvertLookAtConstraint);
            transferManager.RegisterConverter<ParentConstraint>(ConstraintConverter.ConvertParentConstraint);
            transferManager.RegisterConverter<AimConstraint>(ConstraintConverter.ConvertAimConstraint);

            // colliders
            if (sendColliders)
            {
                transferManager.RegisterConverter<SphereCollider>(ColliderConverter.ConvertSphereCollider);
                transferManager.RegisterConverter<BoxCollider>(ColliderConverter.ConvertBoxCollider);
                transferManager.RegisterConverter<CapsuleCollider>(ColliderConverter.ConvertCapsuleCollider);
                transferManager.RegisterConverter<MeshCollider>(ColliderConverter.ConvertMeshCollider);
            }

            // light
            if (sendLights)
            {
                transferManager.RegisterConverter<Light>(LightConverter.ConvertLight);
            }

            transferManager.RegisterConverter<LODGroup>(LODGroupConverter.ConvertLODGroup);
        }

        void DrawTextureCenter(Texture2D texture, Rect containerRect)
        {
            float aspectRatio = (float)texture.width / texture.height;
            float width = containerRect.width;
            float height = width / aspectRatio;

            // Adjust if too tall
            if (height > containerRect.height)
            {
                height = containerRect.height;
                width = height * aspectRatio;
            }

            // Calculate centered position
            float x = containerRect.x + (containerRect.width - width) * 0.5f;
            float y = containerRect.y + (containerRect.height - height) * 0.5f;

            Rect drawRect = new Rect(x, y, width, height);
            EditorGUI.DrawPreviewTexture(drawRect, texture);
        }
        string serverFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "UnityResoniteImporter",
                        "IPCConnections",
                        "Servers"
                    );
        string channelName = "UnityResoniteImporter";

        Texture2D titleTexture;
        void DrawTitle()
        {
            if (titleTexture == null)
            {
                titleTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ResoniteUnityExporter/ResoniteUnityImporterLogo.png");
            }
            // Display the title image at original size and centered
            if (titleTexture != null)
            {
                float imageWidth = titleTexture.width;
                float imageHeight = titleTexture.height;

                // Calculate centered position
                float xPos = (position.width - imageWidth) * 0.5f;

                // Create rect for the image
                Rect imageRect = new Rect(xPos, 10, imageWidth, imageHeight);

                // Reserve space in the layout
                GUILayoutUtility.GetRect(position.width, imageHeight + 20); // +20 for padding

                // Draw the texture
                GUI.DrawTexture(imageRect, titleTexture, ScaleMode.StretchToFill);
            }
        }

        void DrawConnectedStatus()
        {
            if (bridgeClient.NumActiveConnections() == 0)
            {
                serverInfo = LOADING_SERVER_INFO;
            }

            if (bridgeClient.publisher.NumActiveConnections() > 0 && bridgeClient.subscriber.NumActiveConnections() > 0)
            {
                string connectedString = "Connected to Resonite (" + serverInfo.label + ")";
                if (serverInfo.allowedToCreateInWorld)
                {
                    GUI.color = Color.green;
                    string postfix = serverInfo.worldName == "Local" ? " (not recommended, make a world)" : "";
                    GUILayout.Label(connectedString + ": Allowed to create objects in " + serverInfo.worldName + postfix);
                }
                else if (serverInfo.label == LOADING_SERVER_LABEL)
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label(connectedString);
                }
                else
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label(connectedString + ": No permissions in current world (ask host?)");
                }
            }
            else if (bridgeClient.publisher.NumActiveConnections() > 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("Connecting to Resonite (1/2 - need sub - please wait)");
            }
            else if (bridgeClient.subscriber.NumActiveConnections() > 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("Connecting to Resonite (1/2 - need pub - please wait)");
            }
            else
            {
                GUI.color = Color.red;
                GUILayout.Label("Not connected to Resonite");
            }
            GUI.color = Color.white;

            EditorGUILayout.Space(5);
        }

        void DrawSettings()
        {
            sendColliders = EditorGUILayout.ToggleLeft("Send colliders (Not recommend for avatar, it'll auto-add them)", sendColliders);
            sendLights = EditorGUILayout.ToggleLeft("Send lights", sendLights);

            EditorGUI.BeginDisabledGroup(!sendingAvatar);
            if (!sendingAvatar)
            {
                EditorGUILayout.ToggleLeft("Setup Inverse Kinematics (Recommended)", false);
                EditorGUILayout.ToggleLeft("Auto-rename slots (Recommended)", false);
                EditorGUILayout.ToggleLeft("Setup Avatar Creator (Recommended)", false);
                EditorGUILayout.ToggleLeft("Finalize Avatar Creator (Recommended)", false);
            }
            else
            {
                setupIK = EditorGUILayout.ToggleLeft("Setup Inverse Kinematics (Recommended)", setupIK);
                renameSlots = EditorGUILayout.ToggleLeft("Auto-rename slots (Recommended to help Resonite IK)", renameSlots);
                setupAvatarCreator = EditorGUILayout.ToggleLeft("Setup Avatar Creator (Recommended)", setupAvatarCreator);
                EditorGUI.BeginDisabledGroup(!setupAvatarCreator);
                finalizeAvatarCreator = EditorGUILayout.ToggleLeft("Finalize Avatar Creator (Optional)", finalizeAvatarCreator && setupAvatarCreator && serverInfo.label != STANDALONE_LABEL);
                EditorGUI.EndDisabledGroup();
            }
            EditorGUI.EndDisabledGroup();
            if (serverInfo.label == STANDALONE_LABEL)
            {
                EditorGUILayout.ToggleLeft("Make Resonite Package (Required for Standalone)", true);
            }
            else
            {
                makePackage = EditorGUILayout.ToggleLeft("Make Resonite Package", makePackage);
            }
            bool makingPackage = makePackage || serverInfo.label == STANDALONE_LABEL;
            EditorGUI.BeginDisabledGroup(!makingPackage);
            if (!makingPackage)
            {
                EditorGUILayout.ToggleLeft("Include Asset Variants in Package", false);
            }
            else
            {
                includeAssetVariantsInPackage = EditorGUILayout.ToggleLeft("Include Asset Variants in Package", includeAssetVariantsInPackage);
            }
            EditorGUI.EndDisabledGroup();
        }


        Vector3 prevHeadPos = Vector3.zero;
        void DrawViewPreview()
        {
            EditorGUI.BeginDisabledGroup(!sendingAvatar);

            ResoniteTransferUtils.TryGetHeadPosition(parentObject, out foundHead, out Vector3 headPosition, out GameObject headObject);

            if (prevNearClip != nearClip || headViewTex == null
                || (!foundHead && sendingAvatar) // this is incase they modify hierarchy to have a head
                || (headPosition != prevHeadPos)) // this is if they change the head pos value
            {
                headViewTex = CaptureViewFromHead(256, 128, nearClip, out foundHead);
                prevNearClip = nearClip;
            }
            GUILayout.Label(foundHead ? "Found head" : "Could not find any object with 'head' in name (not case sensitive)");
            nearClip = EditorGUILayout.Slider("Near clip", nearClip, 0.001f, 0.5f); // min is 0, max is 1
            GUILayout.Label("Make near clip as small as possible, yet large enough so nothing is in the way");
            // Draw texture
            Rect rect = EditorGUILayout.GetControlRect(false, headViewTex.height);
            DrawTextureCenter(headViewTex, rect);
            GUIStyle centeredStyle = new GUIStyle(GUI.skin.label);
            centeredStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.Label("View preview", centeredStyle);
            EditorGUI.EndDisabledGroup();
        }

        public static string FormatTimeSpan(TimeSpan obj)
        {
            StringBuilder sb = new StringBuilder();
            if (obj.Days != 0)
            {
                sb.Append(obj.Days);
                sb.Append(" days ");
            }
            if (obj.Hours != 0)
            {
                sb.Append(obj.Hours);
                sb.Append(" hours ");
            }
            if (obj.Minutes != 0 || sb.Length != 0)
            {
                sb.Append(obj.Minutes);
                sb.Append(" minutes ");
            }
            if (obj.Seconds != 0 || sb.Length != 0)
            {
                sb.Append(obj.Seconds);
                sb.Append(" seconds ");
            }
            return sb.ToString();
        }
        void DrawViewPreviewAndSubmit()
        {
            // include this in the submit page so they know to deal with it
            if (sendingAvatar)
            {
                DrawViewPreview();
            }

            // for debugging
            /*
            if (GUILayout.Button("Restart connection"))
            {
                bridgeClient.Dispose();
                bridgeClient = null;
                bridgeClient = new ResoniteBridgeClient(channelName, serverFolder, (string message) => { UnityEngine.Debug.Log(message); });
            }
            */
            


            EditorGUILayout.Space(5);

            bool ready = bridgeClient.NumActiveConnections() > 0;

            bool hasAvatarDescriptor = true;

#if RUE_HAS_AVATAR_VRCSDK
            if (sendingAvatar)
            {
                if (parentObject == null)
                {
                    hasAvatarDescriptor = false;
                }
                else
                {
                    hasAvatarDescriptor = parentObject?.GetComponent<VRCAvatarDescriptor>() != null;
                }
            }
#endif

            EditorGUI.BeginDisabledGroup(!ready || 
                (debugCoroutine && CoroutinesInProgress.Count != 0) || 
                !serverInfo.allowedToCreateInWorld 
                || multipleAvatarsSelected 
                || Application.isPlaying
                || !hasAvatarDescriptor);

            string exportLabel = "Export to Resonite";
            if (Application.isPlaying)
            {
                exportLabel += " (only works when not in play mode)";
            }
            if (!hasAvatarDescriptor)
            {
                exportLabel += " (please attach a VRCAvatarDescriptor to root object)";
            }

            if (GUILayout.Button(exportLabel))
            {
                if (exportSlotName == null || exportSlotName == "")
                {
                    exportSlotName = "Untitled";
                }
                ResoniteTransferManager transferManager = new ResoniteTransferManager();
                RegisterConverters(transferManager, sendColliders, sendLights);
                DebugProgressString = "";
                
                System.Collections.IEnumerator coroutine = transferManager.ConvertObjectAndChildren(exportSlotName, parentObject, bridgeClient, new ResoniteTransferSettings()
                {
                    setupAvatarCreator = setupAvatarCreator,
                    setupIK = setupIK,
                    nearClip = nearClip,
                    makeAvatar = sendingAvatar,
                    renameSlots = renameSlots,
                    pressCreateAvatar = finalizeAvatarCreator,
                    materialMappings = GetMaterialMappings(),
                    makePackage = makePackage || serverInfo.label == STANDALONE_LABEL, // force package for standalone
                    includeAssetVariantsInPackage = includeAssetVariantsInPackage,
                });

                PrevCurTransferObjectCount = -1;
                CoroutinesInProgress.Add(coroutine);
                iters = 0;
                debugCoroutine = true;
                ranAnyRuns = true;
                elapsedTimeSending.Restart();
                CurEstimatedMillisForEach = 0;
                // First, mirror the hierarchy into resonite
            }
            EditorGUI.EndDisabledGroup();
            // totalTime * fractionCompleted = curTime
            // totalTime = curTime / fractionCompleted
            if (PrevCurTransferObjectCount != CurTransferObjectCount)
            {
                curIterElapsed.Restart();
                PrevCurTransferObjectCount = CurTransferObjectCount;
            }
            string estimatedTimeLeftStr = " ";
            if (CurTransferObjectCount > 0)
            {
                double estimatedMillisForEach = (elapsedTimeSending.ElapsedMilliseconds - curIterElapsed.ElapsedMilliseconds) / (CurTransferObjectCount);
                // only adjust if we are slower than average
                if (curIterElapsed.ElapsedMilliseconds > estimatedMillisForEach)
                {
                    // do average over this and prev by adding one item to set
                    estimatedMillisForEach = (estimatedMillisForEach * CurTransferObjectCount + curIterElapsed.ElapsedMilliseconds) / (CurTransferObjectCount + 1);
                }
                if (CurEstimatedMillisForEach == 0)
                {
                    CurEstimatedMillisForEach = estimatedMillisForEach;
                }
                else
                {
                    // do a slow weighted average so it's not so jumpy
                    double weighting = 0.995;
                    CurEstimatedMillisForEach = weighting * CurEstimatedMillisForEach + (1-weighting) * estimatedMillisForEach;
                }
                double totalEstimatedMillis = CurEstimatedMillisForEach * TotalTransferObjectCount;
                double totalEstimatedMillisLeft = Math.Max(0, totalEstimatedMillis - elapsedTimeSending.ElapsedMilliseconds);
                
                estimatedTimeLeftStr = FormatTimeSpan(TimeSpan.FromMilliseconds(totalEstimatedMillisLeft));
            }

            string progressLabel = CurTransferObjectCount + "/" + TotalTransferObjectCount + " estimated time left " + estimatedTimeLeftStr + "with iters " + iters + " (in progress, please wait)";
            if (CoroutinesInProgress.Count == 0 || !debugCoroutine)
            {
                DebugProgressString = "";
                DebugProgressStringDetail = "";
                progressLabel = ranAnyRuns ? "Done" : "";
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(progressLabel);
                GUILayout.Label(DebugProgressString);
                GUILayout.Label(DebugProgressStringDetail);
            }
        }

        Material[] GetAllMaterials()
        {
            GameObject[] gameObjects = (parentObject == null)
             ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
             // otherwise, just do the given object as root
             : new GameObject[] { parentObject.gameObject };

            Dictionary<int, Material> uniqueMats = new Dictionary<int, Material>();
            return gameObjects.SelectMany(x => x.GetComponentsInChildren<SkinnedMeshRenderer>())
                .SelectMany(skinnedMeshRenderer =>
                    skinnedMeshRenderer.sharedMaterials
                    .Where(material => material != null)
                )
                .Unique(mat => mat.GetInstanceID())
                .ToArray();
        }

        string GetMaterialName(Material material)
        {
            if (material.shader == null)
            {
                return null;
            }
            if (material.shader.name.Contains("Poiyomi"))
            {
                // sometimes weird hidden with uuid, just group them all together
                return "Poiyomi";
            }
            else
            {
                return material.shader.name;
            }
        }

        UnityEngine.Vector2 materialScrollPosition;
        List<string> resoniteMaterials = new List<string>
        {
            MaterialNames_U2Res.XIEXE_TOON_MAT,
            MaterialNames_U2Res.PBS_METALLIC_MAT,
            MaterialNames_U2Res.PBS_SPECULAR_MAT,
            MaterialNames_U2Res.UNLIT_MAT,
        };

        Dictionary<string, string> shaderDefaults = new Dictionary<string, string>()
        {
            {"Standard", MaterialNames_U2Res.PBS_METALLIC_MAT },
            {"Legacy Shaders/Diffuse", MaterialNames_U2Res.PBS_METALLIC_MAT },
            {"Legacy Shaders/Diffuse Detail", MaterialNames_U2Res.PBS_METALLIC_MAT },
            {"Standard (Specular setup)", MaterialNames_U2Res.PBS_SPECULAR_MAT },
            {"Poiyomi", MaterialNames_U2Res.XIEXE_TOON_MAT },
            {"Unlit/Color", MaterialNames_U2Res.UNLIT_MAT },
            {"Unlit/Texture", MaterialNames_U2Res.UNLIT_MAT },
            {"Unlit/Transparent", MaterialNames_U2Res.UNLIT_MAT },
            {"Unlit/Transparent Cutout", MaterialNames_U2Res.UNLIT_MAT },
        };

        string GetShaderDefault(string shaderName)
        {
            if (shaderDefaults.TryGetValue(shaderName, out string shaderDefault))
            {
                return shaderDefault;
            }  
            else
            {
                return MaterialNames_U2Res.PBS_METALLIC_MAT;
            }
        }

        string GetMaterialMappedName(Material material)
        {
            if (material.shader == null)
            {
                return MaterialNames_U2Res.PBS_METALLIC_MAT;
            }
            string materialName = GetMaterialName(material);
            if (materialMappings.TryGetValue(materialName, out string mappedName))
            {
                return mappedName;
            }
            return GetShaderDefault(materialName);
        }

        Dictionary<int, string> GetMaterialMappings()
        {
            Dictionary<int, string> mappings = new Dictionary<int, string>();
            foreach (Material mat in GetAllMaterials())
            {
                mappings.Add(mat.GetInstanceID(), GetMaterialMappedName(mat));
            }
            return mappings;
        }

        void DrawMaterialMappings()
        {
            materialScrollPosition = EditorGUILayout.BeginScrollView(
                materialScrollPosition,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

            GUILayout.Label("Resonite does not have custom shaders");
            GUILayout.Label("Select a material mapping:");

            List<string> shaderNames = GetAllMaterials()
                .Select(GetMaterialName)
                .Unique(x => x)
                .Where(x => x != null)
                .ToList();
            // so consistent layout
            shaderNames.Sort();

            string[] defaults = shaderNames.Select(GetShaderDefault).ToArray();

            // Content inside scroll view
            string[] resoniteMaterialLabels = resoniteMaterials.ToArray();
            foreach (string shaderName in shaderNames) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(shaderName, GUILayout.Width(200));
                EditorGUILayout.LabelField("-->", GUILayout.Width(30));
                string resoniteMaterial = MaterialNames_U2Res.PBS_METALLIC_MAT;
                bool hasValue = materialMappings.TryGetValue(shaderName, out resoniteMaterial);
                int selectedMaterialIndex = resoniteMaterials.IndexOf(resoniteMaterial);
                string prefsKey = "U2R_MaterialMapping" + shaderName;
                if (selectedMaterialIndex == -1 || !hasValue)
                {
                    selectedMaterialIndex = EditorPrefs.GetInt(prefsKey, resoniteMaterials.IndexOf(GetShaderDefault(shaderName)));
                    materialMappings.Add(shaderName, resoniteMaterials[selectedMaterialIndex]);
                }
                int newMaterialIndex = EditorGUILayout.Popup(selectedMaterialIndex, resoniteMaterialLabels);
                if (newMaterialIndex != selectedMaterialIndex)
                {
                    resoniteMaterial = resoniteMaterialLabels[newMaterialIndex];
                    if (materialMappings.ContainsKey(shaderName))
                    {
                        materialMappings.Remove(shaderName);
                    }
                    materialMappings.Add(shaderName, resoniteMaterial);
                    EditorPrefs.SetInt(prefsKey, newMaterialIndex);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        bool multipleAvatarsSelected = false;

        void OnGUI()
        {
            if (bridgeClient == null)
            {
                serverInfo = LOADING_SERVER_INFO;
                bridgeClient = new ResoniteBridgeClient(channelName, serverFolder, (string message) => { 
                    // uncomment this for debugging info about connections
                    //Debug.Log(message);
                });

            }

#if RUE_HAS_VRCSDK
            UnityEngine.Object[] curPipelines = GameObject.FindObjectsOfType<VRC.Core.PipelineManager>();
            // autodetect pipeline and set name, this happens when scene load
            if (curPipelines.Length == 1)
            {
                if (rootPipelineManager != curPipelines[0])
                {
                    rootPipelineManager = curPipelines[0];
                    if (rootPipelineManager != null)
                    {
                        exportSlotName = rootPipelineManager.name;
                    }
                }
            }
            else
            {
                rootPipelineManager = null;
            }
#endif

            DrawTitle();

            DrawConnectedStatus();
            
            exportSlotName = EditorGUILayout.TextField("Avatar/World Name", exportSlotName);
            EditorGUILayout.BeginHorizontal();
            parentObject = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Select the object to export", "Select the object to export"),
                parentObject,
                typeof(Transform),
                true
            );
            if (GUILayout.Button("Export whole scene"))
            {
                parentObject = null;
            }
            EditorGUILayout.EndHorizontal();
            string labelText = parentObject == null ? "No object selected, sending all objects in scene" : "Sending object " + parentObject.name;
            if (ResoniteTransferUtils.GetAvatarDescriptors(parentObject).Length > 1 && sendingAvatar)
            {
                GUI.color = Color.red;
                labelText = "More than one avatar in " + (parentObject == null ? "scene" : "selected hierarchy or children") + " please select only one";
                multipleAvatarsSelected = true;
            }
            else
            {
                multipleAvatarsSelected = false;
            }
            GUILayout.Label(labelText);
            GUI.color = Color.white;
            bool prevSendingAvatar = EditorPrefs.GetBool("R2USendingAvatar", sendingAvatar);
            sendingAvatar = prevSendingAvatar;
            sendingAvatar = EditorGUILayout.ToggleLeft("Sending Avatar", sendingAvatar);
            sendingAvatar = !EditorGUILayout.ToggleLeft("Sending World", !sendingAvatar);
            if (sendingAvatar != prevSendingAvatar)
            {
                EditorPrefs.SetBool("R2USendingAvatar", sendingAvatar);
                if (sendingAvatar)
                {
                    sendColliders = false;
                    sendLights = false;
                }
                else
                {
                    sendColliders = true;
                    sendLights = true;
                }
            }

            if (sendingAvatar && parentObject == null && ResoniteTransferUtils.GetAvatarDescriptors(parentObject).Length == 1)
            {
                parentObject = ResoniteTransferUtils.GetAvatarDescriptors(parentObject)[0].transform;
            } // bees wow

            const string SUBMIT_TITLE = "Transfer";
            const string SETTINGS_TITLE = "Settings";
            const string MATERIAL_MAPPINGS_TITLE = "Materials";
            string[] tabTitles = new string[]
            {
                SUBMIT_TITLE,
                SETTINGS_TITLE,
                MATERIAL_MAPPINGS_TITLE,
            };

            selectedTab = GUILayout.Toolbar(selectedTab, tabTitles);

            switch (tabTitles[selectedTab])
            {
                case SUBMIT_TITLE:
                    DrawViewPreviewAndSubmit();
                    break;
                case SETTINGS_TITLE:
                    DrawSettings();
                    break;
                case MATERIAL_MAPPINGS_TITLE:
                    DrawMaterialMappings();
                    break;
            }



        }
    }
}