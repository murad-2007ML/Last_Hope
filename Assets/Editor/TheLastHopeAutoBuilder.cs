#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TheLastHopeAutoBuilder
{
    const string SCENE_PATH     = "Assets/Scenes/MainScene.unity";
    const string FBX_ASSET_PATH = "Assets/Models/BasementScene.fbx";

    [MenuItem("Tools/The Last Hope/Build Complete Game Scene")]
    public static void BuildCompleteGameScene()
    {
        // ── 0. Ensure directories exist ──────────────────────────────────────
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Models");

        // ── 1. Locate / import BasementScene.fbx ─────────────────────────────
        string fbxPath = LocateAndImportFbx();

        // ── 2. Create a fresh scene ───────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 3. Instantiate the FBX model ──────────────────────────────────────
        GameObject basementRoot = null;
        if (!string.IsNullOrEmpty(fbxPath))
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset != null)
            {
                basementRoot = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                basementRoot.name = "BasementScene";
            }
        }

        // ── 4. Assign materials ───────────────────────────────────────────────
        if (basementRoot != null)
            AssignMaterials(basementRoot);

        // ── 4a. Lock lights on Exit_Door ─────────────────────────────────────
        var lockLights = CreateLockLightsOnDoor(basementRoot);

        // ── 4b. Pressure Valve ───────────────────────────────────────────────
        var pressureValve = CreatePressureValve();

        // ── 4c. BoxColliders on interactables ────────────────────────────────
        if (basementRoot != null) AddInteractableColliders(basementRoot);

        // ── 4d. Water_Plane → Ignore Raycast layer, no collider ──────────────
        if (basementRoot != null) SetupWaterLayer(basementRoot);

        // ── 5. Horror lighting ────────────────────────────────────────────────
        SetupLighting();

        // ── 6. URP Global Volume ──────────────────────────────────────────────
        var volume = CreateGlobalVolume();

        // ── 7. Player hierarchy ───────────────────────────────────────────────
        var (player, cam, flashlightGO, audioSrc) = CreatePlayer();

        // ── 8. Procedural audio singleton ─────────────────────────────────────
        var procAudioGO = new GameObject("ProceduralAudio");
        procAudioGO.AddComponent<ProceduralAudio>();

        // ── 9. Canvas & UI ────────────────────────────────────────────────────
        var uiRefs = BuildCanvas(cam);

        // ── 10. Game logic objects ─────────────────────────────────────────────
        var managers = CreateManagers(player, cam, volume, uiRefs, basementRoot, flashlightGO, audioSrc);

        // ── 11. Wire all SerializeField references ─────────────────────────────
        WireAllReferences(player, cam, flashlightGO, audioSrc, uiRefs, managers, basementRoot, volume, lockLights, pressureValve);

        // ── 12. Save scene ────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.Refresh();
        AddSceneToBuildSettings(SCENE_PATH);

        Debug.Log("[TheLastHope] MainScene built and saved to " + SCENE_PATH);
        EditorUtility.DisplayDialog("Build Complete",
            "MainScene.unity has been built successfully!\n\nPress Play to start the game.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FBX IMPORT
    // ─────────────────────────────────────────────────────────────────────────

    static string LocateAndImportFbx()
    {
        // Already imported
        if (File.Exists(Path.GetFullPath(FBX_ASSET_PATH)))
        {
            ConfigureFbxImporter(FBX_ASSET_PATH);
            return FBX_ASSET_PATH;
        }

        // Search inside Assets
        string[] guids = AssetDatabase.FindAssets("BasementScene t:Model");
        if (guids.Length > 0)
        {
            string found = AssetDatabase.GUIDToAssetPath(guids[0]);
            ConfigureFbxImporter(found);
            return found;
        }

        // Search project root and parent
        string[] searchPaths = {
            Application.dataPath,
            Directory.GetParent(Application.dataPath).FullName
        };
        foreach (string root in searchPaths)
        {
            var files = Directory.GetFiles(root, "BasementScene.fbx", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                string dest = Path.GetFullPath(FBX_ASSET_PATH);
                File.Copy(files[0], dest, true);
                AssetDatabase.Refresh();
                ConfigureFbxImporter(FBX_ASSET_PATH);
                return FBX_ASSET_PATH;
            }
        }

        Debug.LogWarning("[TheLastHope] BasementScene.fbx not found — building scene without model.");
        return null;
    }

    static void ConfigureFbxImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;
        importer.addCollider = true;
        importer.generateAnimations = ModelImporterGenerateAnimations.None;
        importer.importBlendShapes = false;
        importer.SaveAndReimport();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MATERIALS
    // ─────────────────────────────────────────────────────────────────────────

    static void AssignMaterials(GameObject root)
    {
        var matWetConcrete = CreateURPMaterial("Mat_WetConcrete", new Color(0.08f, 0.08f, 0.09f), 0.9f, 0.3f);
        var matRustedMetal  = CreateURPMaterial("Mat_RustedMetal",  new Color(0.18f, 0.07f, 0.04f), 0.85f, 0.5f);
        var matAgedWood     = CreateURPMaterial("Mat_AgedWood",     new Color(0.22f, 0.14f, 0.07f), 0.95f, 0.1f);
        var matWater        = CreateURPMaterial("Mat_Water",        new Color(0.03f, 0.06f, 0.12f, 0.55f), 0.0f, 0.98f, true);
        var matGhost        = CreateURPMaterial("Mat_Ghost",        new Color(0.7f, 0.75f, 0.9f, 0.4f), 0.6f, 0.2f, true);

        // Case-insensitive keyword → material map (most-specific first)
        var matMap = new List<(string kw, Material mat)>
        {
            ("wire_red",    CreateURPMaterial("Mat_WireRed",    new Color(0.8f,0.05f,0.05f), 0.7f, 0.6f)),
            ("wire_blue",   CreateURPMaterial("Mat_WireBlue",   new Color(0.05f,0.1f,0.8f),  0.7f, 0.6f)),
            ("wire_yellow", CreateURPMaterial("Mat_WireYellow", new Color(0.85f,0.75f,0.0f), 0.7f, 0.6f)),
            ("radio_screen",CreateURPMaterial("Mat_RadioScreen",new Color(0f,0.4f,0.0f,0.8f),0.1f, 0.9f, true)),
            ("radio_dial",  matRustedMetal),
            ("vintage_radio", matRustedMetal),
            ("murad",       matGhost),
            ("exit_door",   matAgedWood),
            ("door",        matAgedWood),
            ("keypad",      matRustedMetal),
            ("fuse",        matRustedMetal),
            ("table",       matAgedWood),
            ("clock",       matRustedMetal),
            ("sun_board",   matAgedWood),
            ("chalk",       matAgedWood),
            ("paper",       CreateURPMaterial("Mat_Paper", new Color(0.85f,0.8f,0.65f), 0.98f, 0.0f)),
            ("photo",       CreateURPMaterial("Mat_Photo", new Color(0.9f,0.85f,0.8f),  0.98f, 0.0f)),
            ("water",       matWater),
            ("radio",       matRustedMetal),
        };

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.gameObject.name.ToLower();
            if (mr.sharedMaterial != null && mr.sharedMaterial.name.StartsWith("M_")) continue;
            foreach (var (kw, mat) in matMap)
            {
                if (n.Contains(kw)) { mr.sharedMaterial = mat; break; }
            }
            if (IsDefault(mr.sharedMaterial))
                mr.sharedMaterial = matWetConcrete;
        }

        // Water: handled by SetupWaterLayer; Murad: no collider, no shadow
        DisableColliderOnObject(root, "murad");
        DisableShadowsOnObject(root,  "murad");
    }

    static bool IsDefault(Material m)
    {
        return m == null || m.name.Contains("Default") || m.name.Contains("default");
    }

    static Material CreateURPMaterial(string name, Color baseColor, float roughness = 0.8f,
        float metallic = 0f, bool transparent = false)
    {
        string shaderName = "Universal Render Pipeline/Lit";
        var shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", baseColor);
        mat.SetColor("_Color", baseColor);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", 1f - roughness);
        if (transparent)
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        return mat;
    }

    static void DisableColliderOnObject(GameObject root, string namePrefix)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains(namePrefix.ToLower()))
                foreach (var c in t.GetComponents<Collider>())
                    c.enabled = false;
    }

    static void DisableShadowsOnObject(GameObject root, string keyword)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains(keyword.ToLower()))
            {
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.shadowCastingMode = ShadowCastingMode.Off;
            }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LIGHTING
    // ─────────────────────────────────────────────────────────────────────────

    static void SetupLighting()
    {
        // Kill any existing directional lights
        foreach (var dl in UnityEngine.Object.FindObjectsOfType<Light>())
            if (dl.type == LightType.Directional)
                UnityEngine.Object.DestroyImmediate(dl.gameObject);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.015f, 0.015f, 0.02f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.015f);
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.08f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // URP GLOBAL VOLUME
    // ─────────────────────────────────────────────────────────────────────────

    static Volume CreateGlobalVolume()
    {
        var go = new GameObject("GlobalVolume");
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "HorrorProfile";

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.35f);
        vignette.smoothness.Override(0.4f);
        vignette.color.Override(new Color(0f, 0f, 0.02f));

        var chroma = profile.Add<ChromaticAberration>(true);
        chroma.intensity.Override(0.0f);

        var colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.postExposure.Override(-0.4f);
        colorAdj.saturation.Override(-25f);
        colorAdj.contrast.Override(15f);

        AssetDatabase.CreateAsset(profile, "Assets/Scenes/HorrorVolumeProfile.asset");
        vol.sharedProfile = profile;
        return vol;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER
    // ─────────────────────────────────────────────────────────────────────────

    static (GameObject player, Camera cam, GameObject flashlight, AudioSource audioSrc)
        CreatePlayer()
    {
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 1.0f, -1.5f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0, 0, 0);
        var fps = player.AddComponent<FirstPersonController>();

        // Camera
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(player.transform);
        camGO.transform.localPosition = new Vector3(0, 0.6f, 0); // eye height offset from player pivot
        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        cam.fieldOfView = 75f;
        camGO.AddComponent<AudioListener>();

        // Attach URP Camera component if available
        var urpCamData = camGO.AddComponent<UniversalAdditionalCameraData>();
        urpCamData.renderPostProcessing = true;
        urpCamData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        // Flashlight
        var flashlightGO = new GameObject("Flashlight");
        flashlightGO.transform.SetParent(camGO.transform);
        flashlightGO.transform.localPosition = new Vector3(0.15f, -0.1f, 0.05f);
        flashlightGO.transform.localRotation = Quaternion.identity;
        var spotLight = flashlightGO.AddComponent<Light>();
        spotLight.type = LightType.Spot;
        spotLight.intensity = 2.5f;
        spotLight.range = 18f;
        spotLight.spotAngle = 55f;
        spotLight.innerSpotAngle = 25f;
        spotLight.color = new Color(0.98f, 0.93f, 0.82f);
        spotLight.shadows = LightShadows.Soft;
        flashlightGO.AddComponent<Flashlight3D>();

        // Audio source for voice
        var audioSrc = player.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;
        audioSrc.volume = 0.9f;

        // Player interaction
        var pi = player.AddComponent<PlayerInteraction>();

        return (player, cam, flashlightGO, audioSrc);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI CANVAS
    // ─────────────────────────────────────────────────────────────────────────

    class UIRefs
    {
        public Canvas canvas;
        public GameObject subtitlePanel;
        public Text subtitleText;
        public Text objectiveText;
        public GameObject flashlightPromptPanel;
        public Text promptText;
        public Slider batterySlider;
        public Slider breathSlider;
        public Button choiceButtonA, choiceButtonB;
        public Text choiceTextA, choiceTextB;
        public GameObject waveformBar;
        public Text frequencyDisplay;
        public GameObject radioPanel;
        public GameObject keypadPanel;
        public Text keypadDisplay;
        public GameObject fusePanel;
        public Text statusText;
        public GameObject cluePanel;
        public Text clueText;
        public GameObject gameOverPanel;
        public Button restartButton;
        public Slider waterLevelSlider;
        public Image waterFillImage;
        public Text waterLevelText;
        public Text timeRemainingText;
        public Text elapsedTimerText;
        public Image warningFlashImage;
        public GameObject valveHoldUI;
        public Slider valveProgressSlider;
        public Text valveProgressText;
        public Image whiteFlash;
        public Image blackFade;
        public Text creditsText;
        public Text endingSubtitle;
    }

    static UIRefs BuildCanvas(Camera cam)
    {
        var refs = new UIRefs();

        // Root canvas
        var canvasGO = new GameObject("UI_Canvas");
        refs.canvas = canvasGO.AddComponent<Canvas>();
        refs.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        refs.canvas.sortingOrder = 10;
        var cs = canvasGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── HUD ──────────────────────────────────────────────────────────────

        // Crosshair
        var cross = CreatePanel(canvasGO, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(20, 20));
        var crossImg = cross.AddComponent<Image>();
        crossImg.color = new Color(1, 1, 1, 0.6f);

        // Interaction prompt
        var promptGO = CreateUIText(canvasGO, "PromptText", new Vector2(0.5f, 0.4f), new Vector2(0, 0), new Vector2(500, 40));
        refs.promptText = promptGO.GetComponent<Text>();
        refs.promptText.alignment = TextAnchor.MiddleCenter;
        refs.promptText.color = new Color(1, 0.9f, 0.7f, 0.9f);
        refs.promptText.fontSize = 20;

        // Battery slider (top-left)
        refs.batterySlider = CreateSlider(canvasGO, "BatterySlider",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(200, 24),
            new Color(0.9f, 0.75f, 0.1f));
        CreateLabel(canvasGO, "BatteryLabel", new Vector2(0, 1), new Vector2(100, -44), "BATTERY", 14);

        // Breath slider (top-right, hidden by default)
        refs.breathSlider = CreateSlider(canvasGO, "BreathSlider",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-220, -20), new Vector2(200, 24),
            new Color(0.3f, 0.9f, 0.5f));
        refs.breathSlider.gameObject.SetActive(false);
        CreateLabel(canvasGO, "BreathLabel", new Vector2(1, 1), new Vector2(-120, -44), "", 14);

        // ── Water Level HUD (bottom-left vertical bar) ───────────────────────
        var waterHudRoot = CreateDarkPanel(canvasGO, "WaterHUD",
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(44, 260));

        // Vertical fill bar
        refs.waterLevelSlider = CreateVerticalSlider(waterHudRoot, "WaterSlider",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 220),
            new Color(0.15f, 0.45f, 0.85f), out refs.waterFillImage);

        // "WATER  0%" label
        var wlTextGO = CreateUIText(canvasGO, "WaterLevelText",
            new Vector2(0, 0.5f), new Vector2(64, 50), new Vector2(130, 26));
        refs.waterLevelText = wlTextGO.GetComponent<Text>();
        refs.waterLevelText.text = "WATER  0%";
        refs.waterLevelText.fontSize = 15;
        refs.waterLevelText.alignment = TextAnchor.MiddleLeft;
        refs.waterLevelText.color = new Color(0.6f, 0.85f, 1f);

        // "~87s LEFT" countdown label
        var timeTextGO = CreateUIText(canvasGO, "TimeRemainingText",
            new Vector2(0, 0.5f), new Vector2(64, 24), new Vector2(140, 26));
        refs.timeRemainingText = timeTextGO.GetComponent<Text>();
        refs.timeRemainingText.text = "~87s LEFT";
        refs.timeRemainingText.fontSize = 15;
        refs.timeRemainingText.alignment = TextAnchor.MiddleLeft;
        refs.timeRemainingText.color = Color.white;

        // Elapsed timer (top-center, monospace stopwatch)
        var timerBg = CreateDarkPanel(canvasGO, "TimerBG",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(170, 38));
        var timerTextGO = CreateUIText(timerBg, "ElapsedTimer",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 32));
        refs.elapsedTimerText = timerTextGO.GetComponent<Text>();
        refs.elapsedTimerText.text = "00:00.00";
        refs.elapsedTimerText.fontSize = 22;
        refs.elapsedTimerText.alignment = TextAnchor.MiddleCenter;
        refs.elapsedTimerText.color = new Color(0.7f, 1f, 0.7f);
        refs.elapsedTimerText.fontStyle = FontStyle.Bold;

        // ── Valve Hold Progress UI (center-screen, below crosshair) ──────────
        refs.valveHoldUI = new GameObject("ValveHoldUI");
        refs.valveHoldUI.transform.SetParent(canvasGO.transform, false);
        var vhRT = refs.valveHoldUI.AddComponent<RectTransform>();
        vhRT.anchorMin = vhRT.anchorMax = vhRT.pivot = new Vector2(0.5f, 0.5f);
        vhRT.anchoredPosition = new Vector2(0, -60);
        vhRT.sizeDelta = new Vector2(240, 48);
        var vhBg = refs.valveHoldUI.AddComponent<Image>();
        vhBg.color = new Color(0.03f, 0.04f, 0.07f, 0.88f);

        refs.valveProgressSlider = CreateSlider(refs.valveHoldUI, "ValveSlider",
            new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 14),
            new Color(0.5f, 0.85f, 1f));

        var vpTextGO = CreateUIText(refs.valveHoldUI, "ValveProgressText",
            new Vector2(0.5f, 0.25f), Vector2.zero, new Vector2(230, 20));
        refs.valveProgressText = vpTextGO.GetComponent<Text>();
        refs.valveProgressText.text = "HOLD E — TURN VALVE";
        refs.valveProgressText.fontSize = 14;
        refs.valveProgressText.alignment = TextAnchor.MiddleCenter;
        refs.valveProgressText.color = new Color(0.6f, 0.9f, 1f);
        refs.valveHoldUI.SetActive(false);

        // Full-screen red warning flash (edge vignette overlay)
        var warnGO = CreatePanel(canvasGO, "WaterWarningFlash",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        refs.warningFlashImage = warnGO.AddComponent<Image>();
        refs.warningFlashImage.color = new Color(1f, 0f, 0f, 0f);
        refs.warningFlashImage.raycastTarget = false;

        // Subtitle panel (bottom center, always-on-top via SetAsLastSibling at runtime)
        refs.subtitlePanel = CreateDarkPanel(canvasGO, "SubtitlePanel",
            new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(0, 50), new Vector2(1200, 150));
        var subTextGO = CreateUIText(refs.subtitlePanel, "SubtitleText",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1160, 140));
        refs.subtitleText = subTextGO.GetComponent<Text>();
        refs.subtitleText.alignment  = TextAnchor.MiddleCenter;
        refs.subtitleText.color      = Color.white;
        refs.subtitleText.fontSize   = 28;
        refs.subtitleText.fontStyle  = FontStyle.Normal;
        refs.subtitleText.supportRichText = true;
        refs.subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        refs.subtitleText.verticalOverflow   = VerticalWrapMode.Overflow;

        // Objective text (top-center, below timer)
        var objGO = CreateUIText(canvasGO, "ObjectiveText",
            new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(700, 30));
        refs.objectiveText = objGO.GetComponent<Text>();
        refs.objectiveText.text      = "";
        refs.objectiveText.fontSize  = 18;
        refs.objectiveText.color     = new Color(0.85f, 0.85f, 0.5f);
        refs.objectiveText.alignment = TextAnchor.MiddleCenter;
        refs.objectiveText.fontStyle = FontStyle.Italic;

        // Flashlight prompt (center screen, shown at game start)
        refs.flashlightPromptPanel = CreateDarkPanel(canvasGO, "FlashlightPrompt",
            new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 60));
        var fpText = CreateUIText(refs.flashlightPromptPanel, "FlashlightPromptText",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 50));
        var fp = fpText.GetComponent<Text>();
        fp.text      = "[ RIGHT-CLICK ]  to turn on Flashlight";
        fp.fontSize  = 22;
        fp.color     = new Color(1f, 0.95f, 0.7f);
        fp.alignment = TextAnchor.MiddleCenter;

        // Choice buttons
        refs.choiceButtonA = CreateChoiceButton(canvasGO, "ChoiceA",
            new Vector2(0.5f, 0.15f), new Vector2(-230, 0), out refs.choiceTextA);
        refs.choiceButtonB = CreateChoiceButton(canvasGO, "ChoiceB",
            new Vector2(0.5f, 0.15f), new Vector2(230, 0), out refs.choiceTextB);
        refs.choiceButtonA.gameObject.SetActive(false);
        refs.choiceButtonB.gameObject.SetActive(false);

        // ── Radio Panel ──────────────────────────────────────────────────────
        refs.radioPanel = CreateDarkPanel(canvasGO, "RadioPanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(500, 350));
        refs.radioPanel.SetActive(false);

        CreateLabel(refs.radioPanel, "RadioTitle",
            new Vector2(0.5f, 0.9f), Vector2.zero, "RADIO TUNER", 24);
        var freqGO = CreateUIText(refs.radioPanel, "FrequencyDisplay",
            new Vector2(0.5f, 0.65f), Vector2.zero, new Vector2(300, 50));
        refs.frequencyDisplay = freqGO.GetComponent<Text>();
        refs.frequencyDisplay.alignment = TextAnchor.MiddleCenter;
        refs.frequencyDisplay.fontSize = 36;
        refs.frequencyDisplay.color = new Color(0.1f, 0.9f, 0.1f);
        refs.frequencyDisplay.text = "92.0 FM";

        // Green waveform bar
        var waveGO = CreatePanel(refs.radioPanel, "WaveformBar",
            new Vector2(0.5f, 0.4f), Vector2.zero, new Vector2(300, 20));
        var waveImg = waveGO.AddComponent<Image>();
        waveImg.color = new Color(0.1f, 0.9f, 0.1f);
        refs.waveformBar = waveGO;

        CreateLabel(refs.radioPanel, "RadioHelp",
            new Vector2(0.5f, 0.15f), Vector2.zero, "Scroll / A-D to tune  |  TAB to close", 16);

        // ── Keypad Panel ─────────────────────────────────────────────────────
        refs.keypadPanel = CreateDarkPanel(canvasGO, "KeypadPanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(360, 500));
        refs.keypadPanel.SetActive(false);

        CreateLabel(refs.keypadPanel, "KeypadTitle",
            new Vector2(0.5f, 0.92f), Vector2.zero, "ENTER CODE", 22);
        var kpdDisplayGO = CreateUIText(refs.keypadPanel, "KeypadDisplay",
            new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(280, 50));
        refs.keypadDisplay = kpdDisplayGO.GetComponent<Text>();
        refs.keypadDisplay.alignment = TextAnchor.MiddleCenter;
        refs.keypadDisplay.fontSize = 38;
        refs.keypadDisplay.color = new Color(0.1f, 0.9f, 0.1f);
        refs.keypadDisplay.text = "____";

        // 3x4 digit grid
        string[] digits = {"7","8","9","4","5","6","1","2","3","Clear","0","Enter"};
        for (int i = 0; i < digits.Length; i++)
        {
            int row = i / 3, col = i % 3;
            float x = -90f + col * 90f;
            float y = 190f - row * 70f;
            var d = digits[i];
            var btn = CreateSmallButton(refs.keypadPanel, "Key_" + d,
                new Vector2(0.5f, 0.5f), new Vector2(x, y - 110f), new Vector2(78, 58), d);
            // We wire onclick during WireAllReferences
        }

        // ── Fuse Box Panel ───────────────────────────────────────────────────
        refs.fusePanel = CreateDarkPanel(canvasGO, "FusePanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(500, 380));
        refs.fusePanel.SetActive(false);

        CreateLabel(refs.fusePanel, "FuseTitle",
            new Vector2(0.5f, 0.88f), Vector2.zero, "FUSE BOX — Cut a wire", 22);

        CreateWireButton(refs.fusePanel, "BtnRed",    new Vector2(0.5f, 0.65f), new Vector2(-130, 0), new Color(0.85f,0.1f,0.1f), "CUT RED");
        CreateWireButton(refs.fusePanel, "BtnBlue",   new Vector2(0.5f, 0.65f), new Vector2(0,    0), new Color(0.1f,0.2f,0.85f), "CUT BLUE");
        CreateWireButton(refs.fusePanel, "BtnYellow", new Vector2(0.5f, 0.65f), new Vector2(130,  0), new Color(0.9f,0.8f,0.0f),  "CUT YELLOW");

        var statusGO = CreateUIText(refs.fusePanel, "StatusText",
            new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(440, 60));
        refs.statusText = statusGO.GetComponent<Text>();
        refs.statusText.alignment = TextAnchor.MiddleCenter;
        refs.statusText.fontSize = 18;
        refs.statusText.color = new Color(1f, 0.8f, 0.3f);

        CreateSmallButton(refs.fusePanel, "CloseFuse",
            new Vector2(0.5f, 0.1f), Vector2.zero, new Vector2(160, 44), "CLOSE");

        // ── Clue Panel ───────────────────────────────────────────────────────
        refs.cluePanel = CreateDarkPanel(canvasGO, "CluePanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(600, 400));
        refs.cluePanel.SetActive(false);

        var clueTextGO = CreateUIText(refs.cluePanel, "ClueText",
            new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(560, 300));
        refs.clueText = clueTextGO.GetComponent<Text>();
        refs.clueText.alignment = TextAnchor.MiddleCenter;
        refs.clueText.fontSize = 20;
        refs.clueText.color = new Color(0.9f, 0.85f, 0.65f);
        refs.clueText.fontStyle = FontStyle.Italic;

        CreateSmallButton(refs.cluePanel, "CloseClue",
            new Vector2(0.5f, 0.1f), Vector2.zero, new Vector2(160, 44), "CLOSE");

        // ── Game Over Panel ───────────────────────────────────────────────────
        refs.gameOverPanel = CreateDarkPanel(canvasGO, "GameOverPanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(700, 400));
        refs.gameOverPanel.SetActive(false);

        CreateLabel(refs.gameOverPanel, "GameOverTitle",
            new Vector2(0.5f, 0.7f), Vector2.zero, "THE WATER ROSE TOO HIGH", 32);
        CreateLabel(refs.gameOverPanel, "GameOverSub",
            new Vector2(0.5f, 0.52f), Vector2.zero, "The radio short-circuited. No signal reached the surface.", 18);

        refs.restartButton = CreateSmallButton(refs.gameOverPanel, "RestartBtn",
            new Vector2(0.5f, 0.25f), Vector2.zero, new Vector2(200, 56), "TRY AGAIN");

        // ── Ending Overlays ───────────────────────────────────────────────────
        var whiteFlashGO = CreatePanel(canvasGO, "WhiteFlash",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        refs.whiteFlash = whiteFlashGO.AddComponent<Image>();
        refs.whiteFlash.color = new Color(1, 1, 1, 0);
        whiteFlashGO.SetActive(false);

        var blackFadeGO = CreatePanel(canvasGO, "BlackFade",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        refs.blackFade = blackFadeGO.AddComponent<Image>();
        refs.blackFade.color = new Color(0, 0, 0, 0);
        blackFadeGO.SetActive(false);

        var creditsGO = CreateUIText(canvasGO, "CreditsText",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 700));
        refs.creditsText = creditsGO.GetComponent<Text>();
        refs.creditsText.alignment = TextAnchor.MiddleCenter;
        refs.creditsText.fontSize = 22;
        refs.creditsText.color = Color.white;
        refs.creditsText.supportRichText = true;
        refs.creditsText.gameObject.SetActive(false);

        // Ending subtitle (reuse subtitle text ref)
        refs.endingSubtitle = refs.subtitleText;

        // Event system
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        return refs;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MANAGERS
    // ─────────────────────────────────────────────────────────────────────────

    class ManagerRefs
    {
        public RadioSystem radio;
        public DialogueManager dialogue;
        public HallucinationManager hallucination;
        public PanicAttack3D panic;
        public PuzzleManager3D puzzle;
        public WaterController3D water;
        public EndingSequence3D ending;
        public PlayerInteraction interaction;
        public Flashlight3D flashlight;
        public FirstPersonController fps;
    }

    static ManagerRefs CreateManagers(GameObject player, Camera _, Volume __,
        UIRefs _ui, GameObject basement, GameObject flashlightGO, AudioSource _audio)
    {
        var refs = new ManagerRefs();

        var managersGO = new GameObject("GameManagers");

        refs.dialogue     = managersGO.AddComponent<DialogueManager>();
        refs.radio        = managersGO.AddComponent<RadioSystem>();
        refs.hallucination = managersGO.AddComponent<HallucinationManager>();
        refs.panic        = managersGO.AddComponent<PanicAttack3D>();
        refs.puzzle       = managersGO.AddComponent<PuzzleManager3D>();
        refs.water        = managersGO.AddComponent<WaterController3D>();
        refs.ending       = managersGO.AddComponent<EndingSequence3D>();

        refs.fps           = player.GetComponent<FirstPersonController>();
        refs.flashlight    = flashlightGO.GetComponent<Flashlight3D>();
        refs.interaction   = player.GetComponent<PlayerInteraction>();

        return refs;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WIRE ALL REFERENCES
    // ─────────────────────────────────────────────────────────────────────────

    static void WireAllReferences(GameObject player, Camera cam, GameObject flashlightGO,
        AudioSource audioSrc, UIRefs ui, ManagerRefs mgr, GameObject basement, Volume volume,
        GameObject[] lockLights, GameObject pressureValve)
    {
        // Helper: sets a serialized object field by property path
        void Set(UnityEngine.Object target, string prop, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var sp = so.FindProperty(prop);
            if (sp != null) { sp.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
            else Debug.LogWarning($"[Wire] Property '{prop}' not found on {target.GetType().Name}");
        }
        // ── FirstPersonController ────────────────────────────────────────────
        Set(mgr.fps, "cameraTransform", cam.transform);

        // ── Flashlight3D ─────────────────────────────────────────────────────
        var spotLight = flashlightGO.GetComponentInChildren<Light>();
        Set(mgr.flashlight, "spotLight",             spotLight);
        Set(mgr.flashlight, "batterySlider",         ui.batterySlider);
        Set(mgr.flashlight, "flashlightPromptPanel", ui.flashlightPromptPanel);

        // ── PlayerInteraction ────────────────────────────────────────────────
        Set(mgr.interaction, "promptText", ui.promptText);
        Set(mgr.interaction, "playerCamera", cam);
        Set(mgr.interaction, "radioSystem", mgr.radio);
        Set(mgr.interaction, "puzzleManager", mgr.puzzle);

        // ── DialogueManager ──────────────────────────────────────────────────
        Set(mgr.dialogue, "subtitlePanel", ui.subtitlePanel);
        Set(mgr.dialogue, "subtitleText",  ui.subtitleText);
        Set(mgr.dialogue, "objectiveText", ui.objectiveText);
        Set(mgr.dialogue, "choiceButtonA", ui.choiceButtonA);
        Set(mgr.dialogue, "choiceButtonB", ui.choiceButtonB);
        Set(mgr.dialogue, "choiceTextA",   ui.choiceTextA);
        Set(mgr.dialogue, "choiceTextB",   ui.choiceTextB);
        Set(mgr.dialogue, "waveformBar",   ui.waveformBar);
        Set(mgr.dialogue, "voiceSource",   audioSrc);

        // ── RadioSystem ───────────────────────────────────────────────────────
        Set(mgr.radio, "frequencyDisplay", ui.frequencyDisplay);
        Set(mgr.radio, "radioPanel",       ui.radioPanel);
        Set(mgr.radio, "staticSource",     audioSrc);
        // Wire radio dial from basement model if present
        if (basement != null)
        {
            var dialT = FindChildByKeyword(basement, "dial");
            if (dialT != null) Set(mgr.radio, "radioDial", dialT);
        }

        // ── HallucinationManager ──────────────────────────────────────────────
        if (basement != null)
        {
            var muradT = FindChildByKeyword(basement, "murad");
            if (muradT != null) Set(mgr.hallucination, "muradCharacter", muradT.gameObject);
        }

        // ── PanicAttack3D ─────────────────────────────────────────────────────
        Set(mgr.panic, "urpVolume",       volume);
        Set(mgr.panic, "breathSlider",    ui.breathSlider);
        Set(mgr.panic, "cameraTransform", cam.transform);

        // ── PuzzleManager3D ──────────────────────────────────────────────────
        Set(mgr.puzzle, "fusePanelUI",      ui.fusePanel);
        Set(mgr.puzzle, "keypadPanelUI",    ui.keypadPanel);
        Set(mgr.puzzle, "clueInspectPanel", ui.cluePanel);
        Set(mgr.puzzle, "clueInspectText",  ui.clueText);
        Set(mgr.puzzle, "keypadDisplay",    ui.keypadDisplay);
        Set(mgr.puzzle, "statusText",       ui.statusText);
        Set(mgr.puzzle, "waterController",  mgr.water);
        Set(mgr.puzzle, "radioSystem",      mgr.radio);
        Set(mgr.puzzle, "panicSystem",      mgr.panic);
        Set(mgr.puzzle, "valveHoldUI",      ui.valveHoldUI);
        Set(mgr.puzzle, "valveProgressSlider", ui.valveProgressSlider);
        Set(mgr.puzzle, "valveProgressText",   ui.valveProgressText);
        if (lockLights != null && lockLights.Length == 3)
        {
            Set(mgr.puzzle, "lockLight1", lockLights[0]);
            Set(mgr.puzzle, "lockLight2", lockLights[1]);
            Set(mgr.puzzle, "lockLight3", lockLights[2]);
        }
        if (pressureValve != null)
        {
            Set(mgr.puzzle, "pressureValve",    pressureValve);
            var valveAudio = pressureValve.GetComponent<AudioSource>();
            if (valveAudio != null) Set(mgr.puzzle, "valveAudioSource", valveAudio);
        }
        if (basement != null)
        {
            SetMeshRef(mgr.puzzle, "wireRed",    basement, "wire_red");
            SetMeshRef(mgr.puzzle, "wireBlue",   basement, "wire_blue");
            SetMeshRef(mgr.puzzle, "wireYellow", basement, "wire_yellow");
        }

        // ── WaterController3D ─────────────────────────────────────────────────
        Set(mgr.water, "gameOverPanel",  ui.gameOverPanel);
        Set(mgr.water, "restartButton",  ui.restartButton);
        Set(mgr.water, "fpsController",     mgr.fps);
        Set(mgr.water, "waterLevelSlider",  ui.waterLevelSlider);
        Set(mgr.water, "waterFillImage",    ui.waterFillImage);
        Set(mgr.water, "waterLevelText",    ui.waterLevelText);
        Set(mgr.water, "timeRemainingText", ui.timeRemainingText);
        Set(mgr.water, "warningFlashImage", ui.warningFlashImage);
        Set(mgr.water, "elapsedTimerText",  ui.elapsedTimerText);
        if (basement != null)
        {
            var waterT = FindChildByKeyword(basement, "water");
            if (waterT != null) Set(mgr.water, "waterPlane", waterT);
            var radioT = FindChildByKeyword(basement, "vintage_radio");
            if (radioT != null) Set(mgr.water, "radioTransform", radioT);
        }

        // ── EndingSequence3D ──────────────────────────────────────────────────
        Set(mgr.ending, "playerCamera",        cam.transform);
        Set(mgr.ending, "hallucinationManager", mgr.hallucination);
        Set(mgr.ending, "waterController",      mgr.water);
        Set(mgr.ending, "fpsController",        mgr.fps);
        Set(mgr.ending, "whiteFlash",           ui.whiteFlash);
        Set(mgr.ending, "blackFade",            ui.blackFade);
        Set(mgr.ending, "creditsText",          ui.creditsText);
        Set(mgr.ending, "endingSubtitle",       ui.endingSubtitle);
        if (basement != null)
        {
            SetMeshRef(mgr.ending, "exitDoor",  basement, "exit_door");
            SetMeshRef(mgr.ending, "mainTable", basement, "main_table");
        }
    }

    static void SetMeshRef(UnityEngine.Object target, string prop, GameObject root, string keyword)
    {
        var t = FindChildByKeyword(root, keyword);
        if (t == null) { Debug.LogWarning($"[Wire] Mesh keyword '{keyword}' not found in model."); return; }
        var so = new SerializedObject(target);
        var sp = so.FindProperty(prop);
        if (sp != null) { sp.objectReferenceValue = t.gameObject; so.ApplyModifiedPropertiesWithoutUndo(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOCK LIGHTS & PRESSURE VALVE
    // ─────────────────────────────────────────────────────────────────────────

    static GameObject[] CreateLockLightsOnDoor(GameObject basementRoot)
    {
        Transform parent = basementRoot != null
            ? (FindChildByKeyword(basementRoot, "exit_door") ?? basementRoot.transform)
            : null;

        if (parent == null)
        {
            var fallback = new GameObject("LockLights_Root");
            fallback.transform.position = new Vector3(3f, 1.2f, -3f);
            parent = fallback.transform;
        }

        Vector3[] offsets = {
            new Vector3(0.12f, 0.60f, 0.06f),
            new Vector3(0.12f, 0.40f, 0.06f),
            new Vector3(0.12f, 0.20f, 0.06f)
        };

        var lights = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"LockLight_{i + 1}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offsets[i];
            go.transform.localScale    = Vector3.one * 0.055f;

            // Red emissive start material
            go.GetComponent<MeshRenderer>().sharedMaterial =
                CreateEmissiveMaterial($"Mat_LockRed_{i + 1}", new Color(1f, 0.04f, 0.04f));

            UnityEngine.Object.DestroyImmediate(go.GetComponent<SphereCollider>());
            lights[i] = go;
        }
        return lights;
    }

    static GameObject CreatePressureValve()
    {
        var root = new GameObject("Pressure_Valve");
        root.transform.position = new Vector3(3.2f, 1.1f, 0.5f);

        // Pipe segment (vertical)
        var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pipe.name = "Valve_Pipe";
        pipe.transform.SetParent(root.transform, false);
        pipe.transform.localPosition = Vector3.zero;
        pipe.transform.localScale    = new Vector3(0.07f, 0.55f, 0.07f);
        pipe.GetComponent<MeshRenderer>().sharedMaterial =
            CreateURPMaterial("Mat_ValvePipe", new Color(0.22f, 0.18f, 0.12f), 0.5f, 0.75f);
        UnityEngine.Object.DestroyImmediate(pipe.GetComponent<CapsuleCollider>());

        // Valve wheel (flat cylinder, perpendicular)
        var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = "Valve_Wheel";
        wheel.transform.SetParent(root.transform, false);
        wheel.transform.localPosition = Vector3.zero;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheel.transform.localScale    = new Vector3(0.28f, 0.035f, 0.28f);
        wheel.GetComponent<MeshRenderer>().sharedMaterial =
            CreateURPMaterial("Mat_ValveWheel", new Color(0.3f, 0.22f, 0.15f), 0.55f, 0.85f);
        UnityEngine.Object.DestroyImmediate(wheel.GetComponent<CapsuleCollider>());

        // Interaction collider on root
        var col    = root.AddComponent<SphereCollider>();
        col.radius = 0.32f;
        col.center = Vector3.zero;

        // 3D spatial AudioSource for clanging
        var audio = root.AddComponent<AudioSource>();
        audio.spatialBlend  = 1f;
        audio.minDistance   = 0.5f;
        audio.maxDistance   = 8f;
        audio.rolloffMode   = AudioRolloffMode.Logarithmic;
        audio.loop          = true;
        audio.playOnAwake   = false;
        audio.volume        = 0.85f;

        return root;
    }

    static Material CreateEmissiveMaterial(string name, Color emissiveColor)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat    = new Material(shader) { name = name };
        mat.SetColor("_BaseColor",     emissiveColor * 0.25f);
        mat.SetColor("_EmissionColor", emissiveColor * 2.5f);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return mat;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    static GameObject CreatePanel(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
        return go;
    }

    static GameObject CreateDarkPanel(GameObject parent, string name,
        Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
    {
        var go = CreatePanel(parent, name, anchor, offset, size);
        var rt = go.GetComponent<RectTransform>();
        rt.pivot = pivot == Vector2.zero ? new Vector2(0.5f, 0.5f) : pivot;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.03f, 0.03f, 0.05f, 0.92f);
        return go;
    }

    static GameObject CreateUIText(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = CreatePanel(parent, name, anchor, offset, size);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 20;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = true;
        return go;
    }

    static void CreateLabel(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, string text, int size = 20)
    {
        var go = CreateUIText(parent, name, anchor, offset, new Vector2(400, 36));
        var t = go.GetComponent<Text>();
        t.text = text;
        t.fontSize = size;
        t.color = new Color(0.75f, 0.75f, 0.75f);
    }

    static Slider CreateSlider(GameObject parent, string name,
        Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, Color fillColor)
    {
        var go = CreatePanel(parent, name, anchor, offset, size);
        var rt = go.GetComponent<RectTransform>();
        rt.pivot = pivot;

        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(go.transform, false);
        var faRT = fillAreaGO.AddComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero; faRT.anchorMax = Vector2.one;
        faRT.sizeDelta = new Vector2(-10, 0);
        faRT.anchoredPosition = Vector2.zero;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = fillColor;

        var slider = go.AddComponent<Slider>();
        slider.targetGraphic = fillImg;
        slider.fillRect = fillRT;
        slider.value = 1f;

        return slider;
    }

    static Slider CreateVerticalSlider(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, Vector2 size, Color fillColor, out Image fillImageOut)
    {
        var go = CreatePanel(parent, name, anchor, offset, size);

        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.06f, 0.06f, 0.08f, 0.9f);

        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(go.transform, false);
        var faRT = fillAreaGO.AddComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero; faRT.anchorMax = Vector2.one;
        faRT.sizeDelta = new Vector2(0, -5);
        faRT.anchoredPosition = Vector2.zero;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImageOut = fillImg;

        var slider = go.AddComponent<Slider>();
        slider.direction = Slider.Direction.BottomToTop;
        slider.targetGraphic = fillImg;
        slider.fillRect = fillRT;
        slider.value = 0f;

        return slider;
    }

    static Button CreateChoiceButton(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, out Text label)
    {
        var go = CreatePanel(parent, name, anchor, offset, new Vector2(420, 52));
        var img = go.AddComponent<Image>();
        img.color = new Color(0.08f, 0.08f, 0.12f, 0.9f);
        var btn = go.AddComponent<Button>();
        var cs = btn.colors;
        cs.highlightedColor = new Color(0.15f, 0.15f, 0.22f);
        cs.pressedColor = new Color(0.05f, 0.3f, 0.15f);
        btn.colors = cs;

        var textGO = CreateUIText(go, "Label",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 44));
        label = textGO.GetComponent<Text>();
        label.fontSize = 18;
        label.color = new Color(0.9f, 0.85f, 0.6f);
        label.fontStyle = FontStyle.Italic;

        return btn;
    }

    static Button CreateSmallButton(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, Vector2 size, string text)
    {
        var go = CreatePanel(parent, name, anchor, offset, size);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        var btn = go.AddComponent<Button>();

        var textGO = CreateUIText(go, "Label",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 10, size.y - 6));
        var t = textGO.GetComponent<Text>();
        t.text = text;
        t.fontSize = 18;
        t.color = new Color(0.9f, 0.85f, 0.6f);

        return btn;
    }

    static void CreateWireButton(GameObject parent, string name,
        Vector2 anchor, Vector2 offset, Color wireColor, string label)
    {
        var go = CreatePanel(parent, name, anchor, offset, new Vector2(120, 60));
        var img = go.AddComponent<Image>();
        img.color = new Color(wireColor.r * 0.3f, wireColor.g * 0.3f, wireColor.b * 0.3f, 0.95f);
        var btn = go.AddComponent<Button>();
        var cs = btn.colors;
        cs.normalColor = Color.white;
        cs.highlightedColor = wireColor;
        btn.colors = cs;

        var textGO = CreateUIText(go, "Label",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 50));
        var t = textGO.GetComponent<Text>();
        t.text = label;
        t.fontSize = 16;
        t.color = wireColor;
        t.fontStyle = FontStyle.Bold;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY
    // ─────────────────────────────────────────────────────────────────────────

    static Transform FindChildByName(GameObject root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Transform FindChildByKeyword(GameObject root, string keyword)
    {
        if (root == null) return null;
        keyword = keyword.ToLower();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains(keyword)) return t;
        return null;
    }

    static void AddInteractableColliders(GameObject root)
    {
        string[] keywords = { "door", "keypad", "fuse", "radio", "clock", "sun", "board",
                               "paper", "photo", "valve", "dial", "wire" };
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.gameObject.name.ToLower();
            bool interactable = false;
            foreach (string kw in keywords) if (n.Contains(kw)) { interactable = true; break; }
            if (!interactable) continue;
            if (mr.GetComponent<Collider>() == null)
            {
                var bc = mr.gameObject.AddComponent<BoxCollider>();
                bc.center = mr.localBounds.center;
                bc.size   = mr.localBounds.size;
            }
        }
    }

    static void SetupWaterLayer(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.ToLower().Contains("water")) continue;
            t.gameObject.layer = 2; // Ignore Raycast — lets Floating_Photo be clickable through water
            foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
        }
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool exists = scenes.Any(s => s.path == path);
        if (!exists)
        {
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
