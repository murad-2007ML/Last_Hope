// "Countless doors" ending for Can You Hear Me? / The Last Hope.
// Builds, at runtime, an endless void behind the exit door: a glass catwalk glowing with falling code,
// hundreds of floating doors (some swinging open by themselves), sagging cable bundles, Matrix-style code rain,
// green fog, a glitching "MISSION ACCOMPLISHED" HUD and the rescuer losing the signal. Then fades to credits.
//
// Hooked from PuzzleManager3D:  DoorVoidEnding.Prepare(exitDoor) when the hydraulic override starts,
//                               DoorVoidEnding.Arm() once the door is fully open.
// Editor / dev builds: press F9 in Play mode to jump straight into the ending (for testing).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class DoorVoidEnding : MonoBehaviour
{
    static DoorVoidEnding _inst;

    [Header("Tuning")]
    public float catwalkLength = 140f;
    public int doorCount = 400;
    public float secondsBeforeCredits = 38f;
    [Tooltip("When the door is fully open, take control and fly the camera out, looking left, right and up")]
    public bool autoCinematic = true;
    public float cinematicSpeed = 1f;
    public bool showOnScreenText = false;   // MISSION ACCOMPLISHED / timer / SURVIVED
    public bool showRescuerLine = false;    // rescuer subtitle
    public string rescuerLine = "...Is that...? Oh my god, they are everywhere... there must be thousands of them. Just pick one and run!";

    Transform _door;
    Vector3 _origin, _fwd, _right;          // catwalk start (floor level), direction into the void
    bool _built, _armed, _entered;
    Transform _root;
    readonly List<Transform> _swingDoors = new List<Transform>();
    readonly List<Vector4> _swingParams = new List<Vector4>();
    readonly List<Transform> _driftDoors = new List<Transform>();
    Material _codeMat, _glassMat;
    Text _hud, _hudR, _hudC;
    Image _fade;
    float _enterTime;
    float _buildTime;
    Quaternion _doorStartRot;

    // ------------------------------------------------------------------ public API
    public static void Prepare(Transform exitDoor)
    {
        if (exitDoor == null) return;
        if (_inst == null) _inst = new GameObject("DoorVoidEnding").AddComponent<DoorVoidEnding>();
        _inst.Build(exitDoor);
    }

    public static void Arm()
    {
        if (_inst == null) return;
        _inst._armed = true;
        if (_inst.autoCinematic) _inst.StartCoroutine(_inst.Cinematic());
    }

    bool _cinematic, _cinematicDone, _postFxAdded;
    float _creakTimer;
    AudioClip _clipVoidLine;

    // True while the camera-controlled cinematic is running (checked by WaterController3D etc.).
    public static bool IsCinematicActive => _inst != null && _inst._cinematic && !_inst._cinematicDone;

    // Camera move (in catwalk space: x = right, y = up, z = forward into the void). Times in seconds.
    static readonly (float t, Vector3 p, Vector3 d)[] Shot =
    {
        (0f,    new Vector3(0f,     1.6f,  -2.2f), new Vector3(0f,   -0.02f, 1f)),    // inside the room, facing the open door
        (2.2f,  new Vector3(0f,     1.6f,  -0.2f), new Vector3(0f,    0f,    1f)),    // through the doorway
        (4.2f,  new Vector3(-0.05f, 1.6f,   2.0f), new Vector3(-1f,   0.15f, 0.6f)),  // look left
        (6.8f,  new Vector3(0.05f,  1.6f,   4.2f), new Vector3(1f,    0.1f,  0.6f)),  // look right
        (9.0f,  new Vector3(0f,     1.6f,   6.2f), new Vector3(0.1f,  0.6f,  1f)),    // look up
        (11.0f, new Vector3(-0.05f, 1.6f,   8.5f), new Vector3(0.3f, -0.55f, 1f)),    // look down into the void
        (13.5f, new Vector3(0f,     1.6f,  11.5f), new Vector3(0f,   -0.05f, 1f)),    // forward into the dark
        (15.5f, new Vector3(0f,     1.6f,  13.5f), new Vector3(0f,   -0.05f, 1f)),
    };

    IEnumerator Cinematic()
    {
        if (_cinematic) yield break;
        _cinematic = true;
        var cam = Camera.main;
        if (cam == null) yield break;

        var fps = FirstPersonController.Instance;
        if (fps != null)
        {
            fps.enabled = false;
            var cc = fps.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }

        // Freeze all gameplay systems so nothing fights the camera or interrupts the cinematic.
        FreezeGameplaySystems();

        // Camera setup: post-processing must be on before the fade-in reveals the void.
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 400f);
        var camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (camData != null) camData.renderPostProcessing = true;

        EnsureHud();
        AddPostFx();

        yield return Fade(0f, 1f, 0.6f);
        // Snap the exit door fully open while the screen is black so it's already open when we fade in.
        if (_door != null) _door.localRotation = _doorStartRot * Quaternion.Euler(0f, -90f, 0f);
        Transform camT = cam.transform;
        Vector3 W(Vector3 local) => _root.TransformPoint(local);
        Quaternion Look(Vector3 localDir) => Quaternion.LookRotation(_root.TransformDirection(localDir.normalized), Vector3.up);
        camT.position = W(Shot[0].p); camT.rotation = Look(Shot[0].d);
        yield return Fade(1f, 0f, 0.8f);

        float end = Shot[Shot.Length - 1].t, t = 0f;
        while (t < end)
        {
            t += Time.deltaTime * cinematicSpeed;
            int i = 0;
            while (i < Shot.Length - 2 && t > Shot[i + 1].t) i++;
            float u = Mathf.Clamp01(Mathf.InverseLerp(Shot[i].t, Shot[i + 1].t, t));
            Vector3 pos = CatmullRom(Shot[Mathf.Max(i - 1, 0)].p, Shot[i].p, Shot[i + 1].p, Shot[Mathf.Min(i + 2, Shot.Length - 1)].p, u);
            pos.y += Mathf.Sin(t * 6.5f) * 0.012f;
            camT.position = W(pos);
            camT.rotation = Quaternion.Slerp(Look(Shot[i].d), Look(Shot[i + 1].d), Mathf.SmoothStep(0f, 1f, u));
            yield return null;
        }
        _cinematicDone = true;
    }

    // Disables every gameplay system that could interrupt or visually fight the cinematic.
    // Also hides the gameplay HUD; DoorVoidEnding's own overlay canvas (sortingOrder 50) is unaffected.
    void FreezeGameplaySystems()
    {
        // Stop panic/blackout and clear vignette/chroma/sway
        FindAnyObjectByType<PanicAttack3D>()?.ForceEnd();

        // Stop water rising, suppress camera-shake, hide water HUD
        if (WaterController3D.Instance != null)
        {
            WaterController3D.Instance.Rising = false;
            WaterController3D.Instance.SetCinematicMode(true);
            WaterController3D.Instance.SetHUDVisible(false);
        }

        // Stop any active voice line and hide subtitles / objective bar
        DialogueManager.Instance?.StopAndHide();
        DialogueManager.Instance?.SetHUDVisible(false);

        // Disable radio so its Update loop doesn't re-trigger dialogue
        var radio = FindAnyObjectByType<RadioSystem>();
        if (radio != null) radio.enabled = false;

        // Stop interaction and clear the prompt
        FindAnyObjectByType<PlayerInteraction>()?.StopInteraction();

        // Turn off spotlight and disable flashlight Update
        Flashlight3D.Instance?.SetCinematicState();
        Flashlight3D.Instance?.SetHUDVisible(false);

        // Best-effort hide for objects that may exist in the scene
        GameObject.Find("Crosshair")?.SetActive(false);
        GameObject.Find("BreathSlider")?.SetActive(false);
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    IEnumerator Fade(float a, float b, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (_fade != null) _fade.color = new Color(0, 0, 0, Mathf.Lerp(a, b, t / dur));
            yield return null;
        }
        if (_fade != null) _fade.color = new Color(0, 0, 0, b);
    }

    void EnsureHud() { if (_fade == null) BuildHud(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallDebugHotkey()
    {
        var go = new GameObject("DoorVoidEnding_DebugHotkey");
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<DebugHotkey>();
    }

    class DebugHotkey : MonoBehaviour
    {
        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F9)) return;
            var doorGo = GameObject.Find("Exit_Door");
            if (doorGo == null) { Debug.LogWarning("[DoorVoidEnding] Exit_Door not found"); return; }
            Prepare(doorGo.transform);
            doorGo.transform.localRotation *= Quaternion.Euler(0f, -90f, 0f);
            Arm();
            _inst.TeleportPlayerToDoorway();
        }
    }
#endif

    void Start()
    {
        _clipVoidLine = Resources.Load<AudioClip>("Audio/Murad/murad_04_end");
    }

    // ------------------------------------------------------------------ build
    void Build(Transform exitDoor)
    {
        if (_built) return;
        _built = true;
        _buildTime = Time.time;   // animation clock resets to 0 when the void is built
        _door = exitDoor;
        _doorStartRot = exitDoor.localRotation;

        // Doorway frame of reference (works whatever axis conversion the FBX used)
        var dr = exitDoor.GetComponentInChildren<Renderer>();
        Bounds db = dr != null ? dr.bounds : new Bounds(exitDoor.position, Vector3.one);
        var floorGo = GameObject.Find("Basement_Floor");
        Vector3 roomC = floorGo != null && floorGo.GetComponent<Renderer>() != null
            ? floorGo.GetComponent<Renderer>().bounds.center : exitDoor.root.position;
        Vector3 doorC = db.center;
        _fwd = doorC - roomC; _fwd.y = 0f; _fwd.Normalize();
        _right = Vector3.Cross(Vector3.up, _fwd);
        _origin = new Vector3(doorC.x, db.min.y, doorC.z) + _fwd * 0.18f;

        _root = new GameObject("DoorVoid").transform;
        _root.position = _origin;
        _root.rotation = Quaternion.LookRotation(_fwd, Vector3.up);

        var codeTex = MakeCodeTexture(512, 1024, 7);
        _codeMat = MakeMat("Void_DataMist", Color.black, 0f, 0f, emissionTex: codeTex,
                           emission: new Color(0.12f, 0.4f, 0.42f) * 0.09f, additive: true);   // barely visible
        _codeMat.SetTextureScale("_BaseMap", new Vector2(10, 4));
        var metal = MakeMat("Void_Metal", new Color(0.08f, 0.08f, 0.08f), 0.85f, 0.5f);
        var cable = MakeMat("Void_Cable", new Color(0.02f, 0.025f, 0.025f), 0.2f, 0.5f);

        BuildCatwalk(metal, cable);
        BuildDoors();
        BuildCodeWalls();
        BuildLights();
    }

    // ------------------------------------------------------------------ catwalk
    void BuildCatwalk(Material metal, Material cable)
    {
        var cw = new GameObject("Catwalk").transform;
        cw.SetParent(_root, false);
        float w = 2.2f, L = catwalkLength;
        // industrial deck: steel-mesh grating (see-through) alternating with concrete slabs
        var grate = MakeMat("Void_SteelGrating", new Color(0.16f, 0.15f, 0.14f), 0.85f, 0.45f);
        var gt = MakeGratingTexture(256, 16, 3);
        grate.SetTexture("_BaseMap", gt); grate.SetTextureScale("_BaseMap", new Vector2(6, 6));
        grate.SetFloat("_AlphaClip", 1f); grate.SetFloat("_Cutoff", 0.5f); grate.EnableKeyword("_ALPHATEST_ON");
        grate.SetFloat("_Cull", 0f); grate.renderQueue = (int)RenderQueue.AlphaTest;
        var conc = MakeMat("Void_Concrete", Color.white, 0f, 0.15f);
        conc.SetTexture("_BaseMap", MakeNoiseTexture(256, new Color(0.12f, 0.12f, 0.115f), new Color(0.3f, 0.3f, 0.29f)));
        var pipeMat = MakeMat("Void_PipeSteel", new Color(0.12f, 0.12f, 0.12f), 0.9f, 0.6f);
        for (float z = 0, k = 0; z < L; z += 2f, k++)
        {
            bool concrete = (int)k % 3 == 0;
            Box(cw, concrete ? "Concrete" : "Grating", new Vector3(0, concrete ? -0.03f : -0.015f, z + 1f),
                new Vector3(w - 0.12f, concrete ? 0.06f : 0.03f, 1.96f), concrete ? conc : grate, false);
            Box(cw, "CrossBeam", new Vector3(0, -0.07f, z), new Vector3(w, 0.08f, 0.08f), metal, false);
        }
        foreach (float s in new[] { -1f, 1f })
        {
            Box(cw, "EdgeBeam", new Vector3(s * w / 2, -0.08f, L / 2), new Vector3(0.1f, 0.16f, L), metal, false);
            Pipe(cw, "Rail", new Vector3(s * w / 2, 1.05f, 0), new Vector3(s * w / 2, 1.05f, L), 0.024f, pipeMat);
            Pipe(cw, "MidRail", new Vector3(s * w / 2, 0.55f, 0), new Vector3(s * w / 2, 0.55f, L), 0.016f, pipeMat);
            for (float z = 0; z <= L; z += 3f)
                Pipe(cw, "Post", new Vector3(s * w / 2, 0f, z), new Vector3(s * w / 2, 1.07f, z), 0.022f, pipeMat);
            // under-truss
            for (float z = 0; z < L; z += 6f)
                Box(cw, "Truss", new Vector3(s * w / 2, -0.55f, z + 3f), new Vector3(0.06f, 0.06f, 6.2f), metal, false)
                    .localRotation = Quaternion.Euler(z % 12f == 0 ? 9f : -9f, 0, 0);
        }
        // physics: floor + side walls + end wall, so the player stays on the walkway
        AddBlocker(cw, new Vector3(0, -0.1f, L / 2 - 0.6f), new Vector3(w, 0.2f, L + 1.2f));   // reaches back into the doorway
        AddBlocker(cw, new Vector3(-w / 2 - 0.05f, 1.5f, L / 2), new Vector3(0.1f, 3f, L));
        AddBlocker(cw, new Vector3(w / 2 + 0.05f, 1.5f, L / 2), new Vector3(0.1f, 3f, L));
        AddBlocker(cw, new Vector3(0, 1.5f, L + 0.2f), new Vector3(w, 3f, 0.2f));
        Box(cw, "Threshold", new Vector3(0, -0.015f, -0.15f), new Vector3(1.1f, 0.03f, 0.4f), metal, false);

        // cable bundles sagging along both sides + some running off into the dark
        var rnd = new System.Random(3);
        foreach (float s in new[] { -1f, 1f })
            for (int k = 0; k < 4; k++)
                for (float z = 0; z < L; z += 9f)
                    Cable(cw, new Vector3(s * (w / 2 + 0.08f + k * 0.05f), -0.1f - k * 0.04f, z),
                          new Vector3(s * (w / 2 + 0.08f + k * 0.05f), -0.1f - k * 0.04f, z + 9f),
                          0.5f + (float)rnd.NextDouble() * 0.6f, 0.025f, cable);
        for (int i = 0; i < 26; i++)
        {
            float z = (float)rnd.NextDouble() * L, s = rnd.Next(2) == 0 ? -1 : 1;
            Vector3 a = new Vector3(s * w / 2, -0.15f, z);
            Vector3 b = new Vector3(s * (8f + (float)rnd.NextDouble() * 14f), -2f + (float)rnd.NextDouble() * 9f,
                                    z + (float)rnd.NextDouble() * 20f - 10f);
            Cable(cw, a, b, 1.5f + (float)rnd.NextDouble() * 3f, 0.03f, cable);
        }
    }

    // ------------------------------------------------------------------ floating doors
    void BuildDoors()
    {
        var palette = new[] {
            new Color(0.80f, 0.79f, 0.74f), new Color(0.70f, 0.66f, 0.56f), new Color(0.55f, 0.47f, 0.36f),
            new Color(0.34f, 0.24f, 0.16f), new Color(0.20f, 0.17f, 0.14f), new Color(0.48f, 0.5f, 0.47f) };
        var glintMat = MakeMat("Void_LightGlint", Color.black, 0f, 0f, emission: new Color(1f, 0.88f, 0.7f) * 0.25f);

        // Detailed doors (Assets/Resources/VoidDoors, made in Blender by build_void_doors.py): 4 styles, 5 worn colorways.
        // Falls back to simple block doors if those files are missing.
        Mesh[] leaves; Mesh frame; Material[] mats;
        var vd = Resources.Load<GameObject>("VoidDoors/VoidDoors");
        var nrm = Resources.Load<Texture2D>("VoidDoors/T_VoidDoor_Normal");
        var ms = Resources.Load<Texture2D>("VoidDoors/T_VoidDoor_MetalSmooth");
        string[] ways = { "White", "Cream", "Green", "Wood", "Dark" };
        if (vd != null)
        {
            Mesh Find(string n) { var t = vd.transform.Find(n); return t != null ? t.GetComponent<MeshFilter>().sharedMesh : null; }
            var found = new List<Mesh>();
            foreach (var n in new[] { "VoidDoor_A", "VoidDoor_B", "VoidDoor_C", "VoidDoor_D", "VoidDoor_E", "VoidDoor_F" })
            { var mm = Find(n); if (mm != null) found.Add(mm); }
            leaves = found.Count > 0 ? found.ToArray() : null;
            frame = Find("VoidDoor_Frame");
            var list = new List<Material>();
            foreach (var w in ways)
            {
                var alb = Resources.Load<Texture2D>("VoidDoors/T_VoidDoor_Albedo_" + w);
                if (alb == null) continue;
                var m = MakeMat("Void_Door_" + w, Color.white, 0f, 1f);
                m.SetTexture("_BaseMap", alb);
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Metallic", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                list.Add(m);
            }
            mats = list.ToArray();
        }
        else { leaves = null; frame = null; mats = null; }
        if (leaves == null || frame == null || System.Array.IndexOf(leaves, null) >= 0 || mats == null || mats.Length == 0)
        {
            Debug.LogWarning("[DoorVoidEnding] Resources/VoidDoors missing - using simple doors.");
            leaves = new[] { BuildDoorLeafMesh() }; frame = BuildDoorFrameMesh();
            mats = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++) mats[i] = MakeMat("Void_Door_" + i, palette[i], 0f, 0.25f);
        }
        // hinge = left edge of the leaf mesh, whatever axis conversion the FBX got
        float hingeX = leaves[0].bounds.min.x;

        var groups = new List<CombineInstance>[mats.Length];
        for (int i = 0; i < groups.Length; i++) groups[i] = new List<CombineInstance>();
        var rnd = new System.Random(11);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        const int near = 170;   // extra doors right next to the walkway so the detail reads

        for (int i = 0; i < doorCount + near; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float z = R(-10f, catwalkLength + 30f);
            float x = side * R(4.5f, 26f);
            float y = R(-22f, 26f);
            if (i % 9 == 0) { x = R(-20f, 20f); y = R(-30f, -12f); }      // some far below
            if (i % 13 == 0) { x = R(-20f, 20f); y = R(16f, 34f); }       // some above
            bool isNear = i >= doorCount;
            if (isNear) { z = R(1f, 75f); x = side * R(2.4f, 7f); y = R(-3f, 9f); }
            float scale = isNear ? R(1f, 1.2f) : R(0.9f, 1.4f);
            // mostly facing the catwalk, a bit chaotic
            Quaternion rot = Quaternion.LookRotation(new Vector3(-x, 0, R(-6f, 6f)).normalized + Vector3.up * R(-0.15f, 0.15f))
                             * Quaternion.Euler(R(-12f, 12f), isNear ? R(-15f, 15f) : R(-30f, 30f), R(-8f, 8f));
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one * scale);

            int c = rnd.Next(mats.Length);
            Mesh leaf = leaves[rnd.Next(leaves.Length)];
            bool ajar = i % 19 == 0 && Mathf.Abs(x) < 18f;
            bool animated = (i % 23 == 0 && Mathf.Abs(x) < 16f) || ajar;
            groups[c].Add(new CombineInstance { mesh = frame, transform = m });
            if (animated)
            {
                // separate object: dark doorway + leaf that swings open/closed by itself
                var holder = new GameObject("SwingDoor").transform;
                holder.SetParent(_root, false);
                holder.localPosition = new Vector3(x, y, z); holder.localRotation = rot; holder.localScale = Vector3.one * scale;
                Box(holder, "Glint", new Vector3(0, 1.03f, -0.075f), new Vector3(0.9f, 1.98f, 0.01f), glintMat, false);   // behind the leaf
                var hinge = new GameObject("Hinge").transform;
                hinge.SetParent(holder, false); hinge.localPosition = new Vector3(hingeX, 0, 0);
                var leafGo = MeshObj(hinge, "Leaf", leaf, mats[c]);
                leafGo.transform.localPosition = new Vector3(-hingeX, 0, 0);
                _swingDoors.Add(hinge);
                // x = speed, y = phase, z = swing angle, w = fixed ajar angle (0 = swings)
                _swingParams.Add(new Vector4(R(0.15f, 0.5f), R(0f, 6.28f), R(25f, 95f), ajar && i % 23 != 0 ? R(10f, 28f) : 0f));
            }
            else if (i % 17 == 0)
            {
                var d = new GameObject("DriftDoor").transform;           // slowly drifting / rotating ones
                d.SetParent(_root, false);
                d.localPosition = new Vector3(x, y, z); d.localRotation = rot; d.localScale = Vector3.one * scale;
                MeshObj(d, "Leaf", leaf, mats[c]);
                _driftDoors.Add(d);
            }
            else groups[c].Add(new CombineInstance { mesh = leaf, transform = m });
        }
        for (int i = 0; i < groups.Length; i++) CombineInto("Doors_" + i, groups[i], mats[i]);
    }

    void CombineInto(string name, List<CombineInstance> list, Material mat)
    {
        if (list.Count == 0) return;
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(list.ToArray(), true, true);
        mesh.RecalculateBounds();
        var go = MeshObj(_root, name, mesh, mat);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // 4-panel door leaf (origin bottom-center, 0.96 x 2.04)
    static Mesh BuildDoorLeafMesh()
    {
        var parts = new List<(Vector3 c, Vector3 s)>
        {
            (new Vector3(-0.42f, 1.02f, 0), new Vector3(0.12f, 2.04f, 0.05f)), (new Vector3(0.42f, 1.02f, 0), new Vector3(0.12f, 2.04f, 0.05f)),
            (new Vector3(0, 1.97f, 0), new Vector3(0.72f, 0.14f, 0.05f)), (new Vector3(0, 1.0f, 0), new Vector3(0.72f, 0.18f, 0.05f)),
            (new Vector3(0, 0.12f, 0), new Vector3(0.72f, 0.24f, 0.05f)), (new Vector3(0, 1.48f, 0), new Vector3(0.08f, 0.82f, 0.05f)),
            (new Vector3(0, 0.56f, 0), new Vector3(0.08f, 0.70f, 0.05f)),
        };
        foreach (var (cx, cy, h) in new[] { (-0.2f, 1.48f, 0.82f), (0.2f, 1.48f, 0.82f), (-0.2f, 0.56f, 0.7f), (0.2f, 0.56f, 0.7f) })
        {
            parts.Add((new Vector3(cx, cy, 0), new Vector3(0.32f, h, 0.025f)));          // recessed panel
            parts.Add((new Vector3(cx, cy, 0), new Vector3(0.24f, h - 0.1f, 0.04f)));    // raised field
        }
        parts.Add((new Vector3(0.36f, 1.0f, -0.045f), new Vector3(0.03f, 0.03f, 0.06f)));   // knob
        parts.Add((new Vector3(0.36f, 1.0f, 0.045f), new Vector3(0.03f, 0.03f, 0.06f)));
        return BoxesMesh("DoorLeaf", parts);
    }

    static Mesh BuildDoorFrameMesh()
    {
        return BoxesMesh("DoorFrame", new List<(Vector3, Vector3)>
        {
            (new Vector3(-0.54f, 1.08f, 0.0f), new Vector3(0.1f, 2.16f, 0.12f)),
            (new Vector3(0.54f, 1.08f, 0.0f), new Vector3(0.1f, 2.16f, 0.12f)),
            (new Vector3(0, 2.13f, 0.0f), new Vector3(1.18f, 0.1f, 0.12f)),
        });
    }

    static Mesh _cube;
    static Mesh CubeMesh()
    {
        if (_cube == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        return _cube;
    }

    static Mesh BoxesMesh(string name, List<(Vector3 c, Vector3 s)> parts)
    {
        var ci = new CombineInstance[parts.Count];
        for (int i = 0; i < parts.Count; i++)
            ci[i] = new CombineInstance { mesh = CubeMesh(), transform = Matrix4x4.TRS(parts[i].c, Quaternion.identity, parts[i].s) };
        var m = new Mesh { name = name };
        m.CombineMeshes(ci, true, true);
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ code rain walls
    void BuildCodeWalls()
    {
        float L = catwalkLength + 80f;
        var tmpQ = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var q = tmpQ.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmpQ);
        void Wall(string n, Vector3 p, Quaternion r, Vector3 s)
        {
            var go = MeshObj(_root, n, q, _codeMat);
            go.transform.localPosition = p; go.transform.localRotation = r; go.transform.localScale = s;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        Wall("CodeLeft", new Vector3(-48f, 0, L / 2 - 20f), Quaternion.Euler(0, -90, 0), new Vector3(L, 110f, 1));
        Wall("CodeRight", new Vector3(48f, 0, L / 2 - 20f), Quaternion.Euler(0, 90, 0), new Vector3(L, 110f, 1));
        Wall("CodeFar", new Vector3(0, 0, L - 20f), Quaternion.identity, new Vector3(70f, 110f, 1));
        Wall("CodeDeep", new Vector3(0, -45f, L / 2 - 20f), Quaternion.Euler(90, 0, 0), new Vector3(70f, L, 1));

        // dark wall around the doorway so the basement doesn't look like an open floating box from outside
        var back = MakeMat("Void_BackWall", new Color(0.008f, 0.009f, 0.01f), 0.3f, 0.2f);
        Box(_root, "BackWallL", new Vector3(-20.7f, 8f, -0.12f), new Vector3(40f, 40f, 0.1f), back, false);
        Box(_root, "BackWallR", new Vector3(20.7f, 8f, -0.12f), new Vector3(40f, 40f, 0.1f), back, false);
        Box(_root, "BackWallTop", new Vector3(0f, 16.65f, -0.12f), new Vector3(1.4f, 28.7f, 0.1f), back, false);
        Box(_root, "BackWallBottom", new Vector3(0f, -6.0f, -0.12f), new Vector3(1.4f, 12f, 0.1f), back, false);
    }

    void BuildLights()
    {
        for (float z = 4f; z < catwalkLength; z += 18f)
        {
            var l = new GameObject("VoidLight").AddComponent<Light>();
            l.transform.SetParent(_root, false);
            l.transform.localPosition = new Vector3(0, 3.2f, z);
            l.type = LightType.Point; l.range = 12f; l.intensity = 1.6f; l.color = new Color(0.85f, 0.88f, 0.92f);
            l.shadows = LightShadows.None;
        }
        // big soft lights out among the floating doors so they read as doors, not silhouettes
        var spots = new[] { new Vector3(-12, 6, 20), new Vector3(12, -4, 35), new Vector3(-14, 10, 55),
                            new Vector3(13, 2, 75), new Vector3(-10, -6, 95), new Vector3(11, 8, 115) };
        foreach (var p in spots)
        {
            var l = new GameObject("VoidDoorLight").AddComponent<Light>();
            l.transform.SetParent(_root, false); l.transform.localPosition = p;
            l.type = LightType.Point; l.range = 20f; l.intensity = 3.5f; l.color = new Color(0.78f, 0.84f, 0.92f);
            l.shadows = LightShadows.None;
        }
        var sky = new GameObject("VoidSkyLight").AddComponent<Light>();
        sky.transform.SetParent(_root, false); sky.transform.localRotation = Quaternion.Euler(55, 20, 0);
        sky.type = LightType.Directional; sky.intensity = 0.25f; sky.color = new Color(0.75f, 0.82f, 0.9f);
        sky.shadows = LightShadows.None; sky.enabled = false;
        _skyLight = sky;
    }
    Light _skyLight;

    // ------------------------------------------------------------------ runtime
    void Update()
    {
        if (!_built) return;
        float t = Time.time - _buildTime;   // seconds since the code was solved; animation starts at 0

        // Distant door creaks — subtle ambient audio for the void.
        if (_entered)
        {
            _creakTimer -= Time.deltaTime;
            if (_creakTimer <= 0f)
            {
                _creakTimer = Random.Range(5f, 14f);
                ProceduralAudio.Instance?.PlayDoorCreak();
            }
        }
        // code rain scrolls downward; catwalk glass code scrolls along the walkway
        if (_codeMat != null) _codeMat.SetTextureOffset("_BaseMap", new Vector2(0, t * 0.08f));
        if (_glassMat != null) _glassMat.SetTextureOffset("_BaseMap", new Vector2(0, -t * 0.25f));
        for (int i = 0; i < _swingDoors.Count; i++)
        {
            var p = _swingParams[i];
            float open = p.w > 0f ? p.w : Mathf.SmoothStep(0, 1, 0.5f + 0.5f * Mathf.Sin(t * p.x + p.y)) * p.z;
            _swingDoors[i].localRotation = Quaternion.Euler(0, -open, 0);
        }
        for (int i = 0; i < _driftDoors.Count; i++)
        {
            var d = _driftDoors[i];
            d.localPosition += new Vector3(0, Mathf.Sin(t * 0.3f + i) * 0.004f, 0);
            d.Rotate(new Vector3(0.6f, 1.3f, 0.4f) * Time.deltaTime * (i % 2 == 0 ? 1 : -1), Space.Self);
        }

        if (_armed && !_entered)
        {
            var cam = Camera.main;
            if (cam != null && Vector3.Dot(cam.transform.position - _origin, _fwd) > 0.35f) StartCoroutine(EnterVoid());
        }
        if (_hud != null) Glitch();
    }

    IEnumerator EnterVoid()
    {
        _entered = true;
        _enterTime = Time.time;
        var water = WaterController3D.Instance; if (water != null) water.Rising = false;
        ProceduralAudio.Instance?.SetStaticAmount(0.25f);
        ProceduralAudio.Instance?.StopHeartbeat();
        ProceduralAudio.Instance?.StartVoidAmbience();

        if (_skyLight != null) _skyLight.enabled = true;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.03f, 0.034f, 0.04f);
        RenderSettings.fogDensity = 0.026f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.065f);
        var cam = Camera.main;
        if (cam != null)
        {
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 400f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
        }

        EnsureHud();
        AddPostFx();
        yield return new WaitForSeconds(1.6f);
        ProceduralAudio.Instance?.PlayRadioBlip();

        // Murad's haunting line from the void
        if (_clipVoidLine != null)
        {
            DialogueManager.Instance?.SetHUDVisible(true);
            bool lineDone = false;
            DialogueManager.Instance?.SpeakMurad(
                "You think you left Murad behind? Look at all these doors... Everyone has their own trial, and yours has only just begun.",
                _clipVoidLine, () => lineDone = true);
            float lineTimeout = Time.time + 12f;
            while (!lineDone && Time.time < lineTimeout) yield return null;
        }

        if (showRescuerLine) DialogueManager.Instance?.SpeakRescuer(rescuerLine);

        // signal slowly dies
        float t0 = Time.time;
        while (Time.time - t0 < secondsBeforeCredits && !(autoCinematic && _cinematicDone))
        {
            float k = (Time.time - t0) / secondsBeforeCredits;
            ProceduralAudio.Instance?.SetStaticAmount(Mathf.Lerp(0.25f, 0.9f, k));
            RenderSettings.fogDensity = Mathf.Lerp(0.026f, 0.05f, k);
            yield return null;
        }

        // fade to black, then the game's own credits (mental-health message)
        float f = 0;
        while (f < 1f) { f += Time.deltaTime / 3f; if (_fade != null) _fade.color = new Color(0, 0, 0, f); yield return null; }
        ProceduralAudio.Instance?.SetStaticAmount(0f);
        ProceduralAudio.Instance?.StopVoidAmbience();
        var ending = FindAnyObjectByType<EndingSequence3D>();
        if (ending != null) ending.PlayCreditsOnly();
    }

    public void TeleportPlayerToDoorway()
    {
        var fps = FirstPersonController.Instance;
        if (fps == null) return;
        var cc = fps.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        fps.transform.position = _origin - _fwd * 1.2f + Vector3.up * 1.0f;
        fps.transform.rotation = Quaternion.LookRotation(_fwd, Vector3.up);
        if (cc != null) cc.enabled = true;
    }

    // ------------------------------------------------------------------ HUD (mission accomplished / timer / survived)
    void BuildHud()
    {
        var canvasGo = new GameObject("VoidHUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform Rect(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
        Image Panel(string n, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject(n, typeof(Image)); Rect(go, anchor, pos, size);
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false; return im;
        }
        Text Label(string n, string txt, Vector2 anchor, Vector2 pos, Vector2 size, int fs, Color c, FontStyle st = FontStyle.Bold)
        {
            var go = new GameObject(n, typeof(Text)); Rect(go, anchor, pos, size);
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = fs; t.fontStyle = st; t.color = c;
            t.alignment = TextAnchor.MiddleCenter; t.text = txt; t.raycastTarget = false; return t;
        }
        var top = new Vector2(0.5f, 1f);
        if (showOnScreenText)
        {
        _hudR = Label("GlitchR", "MISSION ACCOMPLISHED", top, new Vector2(-3, -70), new Vector2(1400, 90), 64, new Color(1f, 0.2f, 0.2f, 0f));
        _hudC = Label("GlitchC", "MISSION ACCOMPLISHED", top, new Vector2(3, -70), new Vector2(1400, 90), 64, new Color(0.2f, 0.9f, 1f, 0f));
        _hud = Label("MissionText", "MISSION ACCOMPLISHED", top, new Vector2(0, -70), new Vector2(1400, 90), 64, new Color(0.55f, 0.65f, 0.7f));
        Panel("TimerBox", top, new Vector2(0, -132), new Vector2(190, 46), new Color(0.01f, 0.06f, 0.035f, 0.85f));
        _timer = Label("Timer", "00:00.00", top, new Vector2(0, -132), new Vector2(190, 46), 32, new Color(0.3f, 0.85f, 0.5f));
        var tr = new Vector2(1f, 1f);
        Panel("SurvivedBanner", tr, new Vector2(-190, -70), new Vector2(340, 64), new Color(0.78f, 0.6f, 0.12f, 0.92f));
        Label("Survived", "SURVIVED", tr, new Vector2(-205, -70), new Vector2(300, 64), 44, new Color(0.08f, 0.06f, 0.02f));
        Label("SurvivedChevron", "\u00BB", tr, new Vector2(-45, -72), new Vector2(40, 64), 44, new Color(0.08f, 0.06f, 0.02f, 0.6f));
        }
        var fadeGo = new GameObject("Fade", typeof(Image));
        fadeGo.transform.SetParent(canvasGo.transform, false);
        _fade = fadeGo.GetComponent<Image>(); _fade.color = new Color(0, 0, 0, 0); _fade.raycastTarget = false;
        var frt = _fade.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
    }
    Text _timer;

    void Glitch()   // rare, subtle signal glitch on the title
    {
        bool burst = Random.value < 0.03f;
        float a = burst ? 0.45f : 0f;
        _hudR.color = new Color(1f, 0.2f, 0.2f, a); _hudC.color = new Color(0.2f, 0.9f, 1f, a);
        _hud.color = new Color(0.55f, 0.65f, 0.7f, Random.value < 0.015f ? 0.35f : 1f);
    }

    // film grain, desaturation, vignette for the reveal
    void AddPostFx()
    {
        if (_postFxAdded) return;
        _postFxAdded = true;
        var go = new GameObject("VoidPostFX");
        var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 100f;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var grain = prof.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(0.45f);
        var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(-40f); ca.contrast.Override(15f);
        var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.38f); vg.smoothness.Override(0.5f);
        vol.sharedProfile = prof;
        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }
    }

    // ------------------------------------------------------------------ helpers
    static Mesh _cyl;
    void Pipe(Transform parent, string n, Vector3 a, Vector3 b, float r, Material mat)
    {
        if (_cyl == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cyl = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
        }
        var go = MeshObj(parent, n, _cyl, mat);
        Vector3 d = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        go.transform.localScale = new Vector3(r * 2f, d.magnitude * 0.5f, r * 2f);   // Unity cylinder is 2 units tall
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static Texture2D MakeGratingTexture(int n, int cell, int bar)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool solid = x % cell < bar || y % cell < bar || x % (cell * 8) < bar * 2 || y % (cell * 8) < bar * 2;
                byte g = (byte)(150 + (x * 7 + y * 13) % 40);
                px[y * n + x] = solid ? new Color32(g, g, g, 255) : new Color32(0, 0, 0, 0);
            }
        tex.SetPixels32(px); tex.Apply(true); return tex;
    }

    static Texture2D MakeNoiseTexture(int n, Color a, Color b)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = Mathf.PerlinNoise(x * 0.04f, y * 0.04f) * 0.6f + Mathf.PerlinNoise(x * 0.2f + 31f, y * 0.2f) * 0.4f;
                px[y * n + x] = Color.Lerp(a, b, v);
            }
        tex.SetPixels(px); tex.Apply(true); return tex;
    }

    Transform Box(Transform parent, string n, Vector3 p, Vector3 s, Material mat, bool collider)
    {
        var go = MeshObj(parent, n, CubeMesh(), mat);
        go.transform.localPosition = p; go.transform.localScale = s;
        if (collider) go.AddComponent<BoxCollider>();
        return go.transform;
    }

    void AddBlocker(Transform parent, Vector3 p, Vector3 s)
    {
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = p;
        var bc = go.AddComponent<BoxCollider>(); bc.size = s;
    }

    static GameObject MeshObj(Transform parent, string n, Mesh mesh, Material mat)
    {
        var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    void Cable(Transform parent, Vector3 a, Vector3 b, float sag, float width, Material mat)
    {
        var go = new GameObject("Cable");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.sharedMaterial = mat; lr.widthMultiplier = width;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        const int N = 14; lr.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float t = i / (N - 1f);
            lr.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (4f * sag * t * (1 - t)));
        }
    }

    static Material MakeMat(string name, Color baseCol, float metallic, float smooth, Texture2D emissionTex = null,
                            Color? emission = null, bool transparent = false, bool additive = false)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = name };
        m.SetColor("_BaseColor", baseCol);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (emissionTex != null) { m.SetTexture("_BaseMap", emissionTex); m.SetTexture("_EmissionMap", emissionTex); }
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (transparent || additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }

    // Matrix-style glyph columns (random 5x7 pixel glyphs, bright heads, fading tails)
    static Texture2D MakeCodeTexture(int w, int h, int seed)
    {
        var rnd = new System.Random(seed);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        const int cw = 16, ch = 22;
        int cols = w / cw, rows = h / ch;
        for (int c = 0; c < cols; c++)
        {
            int streaks = 1 + rnd.Next(3);
            var bright = new float[rows];
            for (int s = 0; s < streaks; s++)
            {
                int head = rnd.Next(rows), len = 6 + rnd.Next(rows / 2);
                for (int k = 0; k < len; k++)
                {
                    int r = (head + k) % rows;               // tail fades going up the texture (scroll moves it down)
                    bright[r] = Mathf.Max(bright[r], k == 0 ? 1.4f : Mathf.Pow(1f - k / (float)len, 1.6f));
                }
            }
            for (int r = 0; r < rows; r++)
            {
                if (bright[r] <= 0.02f) continue;
                int glyph = rnd.Next();
                for (int gy = 0; gy < 7; gy++)
                    for (int gx = 0; gx < 5; gx++)
                    {
                        if (((glyph >> ((gy * 5 + gx) % 31)) & 1) == 0 && !(gx == 2 && gy % 3 == 0)) continue;
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                            {
                                int x = c * cw + 3 + gx * 2 + sx, y = r * ch + 4 + gy * 2 + sy;
                                if (x >= w || y >= h) continue;
                                float b = bright[r];
                                byte g = (byte)Mathf.Clamp(255 * b, 0, 255);
                                byte rr = (byte)Mathf.Clamp(255 * (b - 1f) * 2.5f + 20 * b, 0, 255);   // white-hot heads
                                px[y * w + x] = new Color32(rr, g, (byte)(g * 0.55f + rr * 0.5f), (byte)Mathf.Clamp(255 * b, 0, 255));
                            }
                    }
            }
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}
