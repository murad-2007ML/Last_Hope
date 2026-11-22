using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public enum PuzzleStage
{
    Stage0_Locked,
    Stage1_TuneBackupFrequency,
    Stage2_CutWires,
    Stage3_TurnPipeValve,
    Stage4_EnterDoorCode,
    Stage5_DoorUnlocked
}

public class PuzzleManager3D : MonoBehaviour
{
    public static PuzzleManager3D Instance { get; private set; }

    public PuzzleStage CurrentStage { get; private set; } = PuzzleStage.Stage0_Locked;
    public PuzzleStage PuzzleStage => CurrentStage;
    public int PuzzleStageInt => (int)CurrentStage;

    public bool AnyPanelOpen =>
        (fusePanelUI      != null && fusePanelUI.activeSelf)    ||
        (keypadPanelUI    != null && keypadPanelUI.activeSelf)  ||
        (clueInspectPanel != null && clueInspectPanel.activeSelf) ||
        (radioSystem      != null && radioSystem.PanelOpen);

    [Header("Puzzle Objects")]
    [SerializeField] GameObject wireRed;
    [SerializeField] GameObject wireBlue;
    [SerializeField] GameObject wireYellow;
    [SerializeField] WaterController3D waterController;

    [Header("Lock Lights")]
    [SerializeField] GameObject lockLight1;
    [SerializeField] GameObject lockLight2;
    [SerializeField] GameObject lockLight3;

    [Header("Pressure Valve")]
    [SerializeField] GameObject  pressureValve;
    [SerializeField] AudioSource valveAudioSource;
    [SerializeField] GameObject  valveHoldUI;
    [SerializeField] Slider      valveProgressSlider;
    [SerializeField] TextMeshProUGUI valveProgressText;

    [Header("Systems")]
    [SerializeField] RadioSystem   radioSystem;
    [SerializeField] PanicAttack3D panicSystem;

    [Header("UI Panels")]
    [SerializeField] GameObject fusePanelUI;
    [SerializeField] GameObject keypadPanelUI;
    [SerializeField] GameObject clueInspectPanel;
    [SerializeField] TextMeshProUGUI clueInspectText;
    [SerializeField] TextMeshProUGUI keypadDisplay;
    [SerializeField] TextMeshProUGUI statusText;

    [Header("Exit Door")]
    [SerializeField] Transform exitDoor;
    [SerializeField] UnityEvent onExitDoorOpened;
    [SerializeField] bool triggerEndingCutsceneAutomatically = false;

    // Runtime state - Wires
    int _wireCutStep;

    // Runtime state - Valves
    GameObject[] _valves;
    int _activeValveIndex;
    bool _valveHolding;
    bool _holdingCorrectValve;
    float _gaugeProgress;
    int _valveNotchesDone;
    bool _valveDone;

    // Runtime state - Keypad & Door
    string _keypadInput = "";
    const string CODE = "0715";
    bool _doorOpenStarted;

    Material _matRed, _matGreen;
    Material _keypadScreenMat;
    EndingSequence3D _ending;

    // Fuse box 3D door
    Transform _fuseBoxDoor;
    Quaternion _fuseBoxDoorOpenRot;
    Quaternion _fuseBoxDoorClosedRot;
    bool _fuseBoxDoorOpened;

    // Character voice audio clips
    AudioClip _muradWake, _rescuerIntro, _rescuerDoorOverride;

    // Cached UI Buttons
    Button _btnRed;
    Button _btnBlue;
    Button _btnYellow;
    Button _btnCloseFuse;
    Button _btnCloseClue;
    Button _btnCloseKeypad;

    void Awake()
    {
        Instance = this;
        _ending = FindAnyObjectByType<EndingSequence3D>();

        EnsureEventSystemInputModule();

        if (fusePanelUI      != null) fusePanelUI.SetActive(false);
        if (keypadPanelUI    != null) keypadPanelUI.SetActive(false);
        if (clueInspectPanel != null) clueInspectPanel.SetActive(false);
        if (valveHoldUI      != null) valveHoldUI.SetActive(false);

        AutoUpgradeTMP();
        WireButtons();
    }

    void AutoUpgradeTMP()
    {
        if (clueInspectText == null && clueInspectPanel != null)
        {
            clueInspectText = clueInspectPanel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (clueInspectText == null)
            {
                var legacy = clueInspectPanel.GetComponentInChildren<Text>(true);
                if (legacy != null) clueInspectText = DialogueManager.UpgradeTextToTMP(legacy);
            }
        }
        if (keypadDisplay == null && keypadPanelUI != null)
        {
            keypadDisplay = keypadPanelUI.GetComponentInChildren<TextMeshProUGUI>(true);
            if (keypadDisplay == null)
            {
                var legacy = keypadPanelUI.GetComponentInChildren<Text>(true);
                if (legacy != null) keypadDisplay = DialogueManager.UpgradeTextToTMP(legacy);
            }
        }
        if (valveProgressText == null && valveHoldUI != null)
        {
            valveProgressText = valveHoldUI.GetComponentInChildren<TextMeshProUGUI>(true);
            if (valveProgressText == null)
            {
                var legacy = valveHoldUI.GetComponentInChildren<Text>(true);
                if (legacy != null) valveProgressText = DialogueManager.UpgradeTextToTMP(legacy);
            }
        }
        if (statusText == null)
        {
            var go = GameObject.Find("StatusText");
            if (go != null)
            {
                statusText = go.GetComponent<TextMeshProUGUI>();
                if (statusText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) statusText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
    }

    void Start()
    {
        _muradWake          = Resources.Load<AudioClip>("Audio/Murad/murad_01_wake");
        _rescuerIntro       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_01_intro");
        _rescuerDoorOverride = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");
        Debug.Log($"[PuzzleManager] Clips — muradWake:{_muradWake != null} rescuerIntro:{_rescuerIntro != null} doorOverride:{_rescuerDoorOverride != null}");

        BuildLockLightMaterials();
        AutoFindReferences();
        SetupValves();
        SetupSweetSpotIndicator();
        SetupKeypadScreenMaterial();
        SetupFuseBoxDoor();

        SetObjective("Explore the basement — turn on your flashlight [RIGHT-CLICK]");
    }

    void AutoFindReferences()
    {
        if (lockLight1 == null) lockLight1 = GameObject.Find("LockLight_1");
        if (lockLight2 == null) lockLight2 = GameObject.Find("LockLight_2");
        if (lockLight3 == null) lockLight3 = GameObject.Find("LockLight_3");

        if (pressureValve == null) pressureValve = GameObject.Find("Pressure_Valve");
        if (valveAudioSource == null && pressureValve != null)
            valveAudioSource = pressureValve.GetComponent<AudioSource>();

        if (radioSystem == null) radioSystem = FindAnyObjectByType<RadioSystem>();
        if (panicSystem == null) panicSystem = FindAnyObjectByType<PanicAttack3D>();
        if (waterController == null) waterController = FindAnyObjectByType<WaterController3D>();

        if (exitDoor == null)
        {
            var allT = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in allT)
            {
                if (t.name.ToLower().Contains("exit_door"))
                {
                    exitDoor = t;
                    break;
                }
            }
        }
    }

    void SetupValves()
    {
        if (pressureValve == null) return;

        _valves = new GameObject[3];
        _valves[0] = pressureValve;

        var v2 = GameObject.Find("Pressure_Valve_2");
        if (v2 == null)
        {
            v2 = Instantiate(pressureValve, pressureValve.transform.position + new Vector3(0f, 0f, -2.3f), pressureValve.transform.rotation);
            v2.name = "Pressure_Valve_2";
        }
        _valves[1] = v2;

        var v3 = GameObject.Find("Pressure_Valve_3");
        if (v3 == null)
        {
            v3 = Instantiate(pressureValve, pressureValve.transform.position + new Vector3(0f, 0f, 2.3f), pressureValve.transform.rotation);
            v3.name = "Pressure_Valve_3";
        }
        _valves[2] = v3;

        _activeValveIndex = Random.Range(0, 3);

        for (int i = 0; i < 3; i++)
        {
            var v = _valves[i];
            if (v == null) continue;

            if (v.GetComponent<Collider>() == null)
            {
                var col = v.AddComponent<SphereCollider>();
                col.radius = 0.35f;
            }

            var src = v.GetComponent<AudioSource>();
            if (src == null) src = v.AddComponent<AudioSource>();
            src.clip = ProceduralAudio.GenerateClangClip();
            src.loop = true;
            src.spatialBlend = 1f;
            src.minDistance = 0.5f;
            src.maxDistance = 7f;
            src.playOnAwake = false;
            src.Stop();
        }
    }

    void SetupSweetSpotIndicator()
    {
        if (valveProgressSlider == null) return;
        Transform existing = valveProgressSlider.transform.Find("SweetSpotZone");
        if (existing != null) return;

        var zoneGO = new GameObject("SweetSpotZone", typeof(RectTransform), typeof(Image));
        zoneGO.transform.SetParent(valveProgressSlider.transform, false);
        var rt = zoneGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.70f, 0f);
        rt.anchorMax = new Vector2(0.85f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = zoneGO.GetComponent<Image>();
        img.color = new Color(0.2f, 0.95f, 0.35f, 0.55f);
        img.raycastTarget = false;
    }

    // ── Silent Gating Checker for PlayerInteraction ──────────────────────────

    public bool IsInteractableAllowed(string nameLower)
    {
        switch (CurrentStage)
        {
            case PuzzleStage.Stage0_Locked:
                // Pre-start: Radio is allowed to initiate Stage 1
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                return false;

            case PuzzleStage.Stage1_TuneBackupFrequency:
                // Vintage_Radio: Active from Stage1 onward.
                // Clue_Paper_1 (Base freq 102.0) & Clue_Paper_2 (Offset -3.6): Active in Stage1+.
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                if (nameLower.Contains("clue_paper_1")  || nameLower.Contains("clue_paper_2")) return true;
                return false;

            case PuzzleStage.Stage2_CutWires:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                if (nameLower.Contains("clue_paper_1")  || nameLower.Contains("clue_paper_2")) return true;
                if (nameLower.Contains("clue_paper_3")) return true;
                // Fuse_Box & Wall_Clock: Active in Stage2
                if (nameLower.Contains("fuse_box") || nameLower.Contains("fuse")) return true;
                if (nameLower.Contains("wall_clock") || nameLower.Contains("clock")) return true;
                return false;

            case PuzzleStage.Stage3_TurnPipeValve:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                // Pressure_Valves: Active in Stage3
                if (nameLower.Contains("pressure_valve") || nameLower.Contains("valve")) return true;
                return false;

            case PuzzleStage.Stage4_EnterDoorCode:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                // Door_Keypad, Chalk_Sun_Board, & Floating_Photo: Active in Stage4
                if (nameLower.Contains("door_keypad") || nameLower.Contains("keypad")) return true;
                if (nameLower.Contains("chalk_sun_board") || nameLower.Contains("sun")) return true;
                if (nameLower.Contains("floating_photo") || nameLower.Contains("photo")) return true;
                if (nameLower.Contains("wall_clock") || nameLower.Contains("clock")) return true;
                if (nameLower.Contains("clue_paper_1") || nameLower.Contains("clue_paper_2") || nameLower.Contains("clue_paper_3")) return true;
                return false;

            case PuzzleStage.Stage5_DoorUnlocked:
                // Door already unlocked
                return false;

            default:
                return false;
        }
    }

    // ── Opening sequence (called by Flashlight3D on first toggle) ────────────

    public void OnFirstFlashlightOn()
    {
        if (CurrentStage == PuzzleStage.Stage0_Locked)
        {
            CurrentStage = PuzzleStage.Stage1_TuneBackupFrequency;
            SetObjective("Find Note #1 & Note #2 on desks — calculate frequency and tune radio");
        }

        StartCoroutine(OpeningSequence());
    }

    IEnumerator OpeningSequence()
    {
        // 1. Wait until Murad's wake-up dialogue finishes completely so it is never skipped or cut off!
        while (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking)
        {
            yield return null;
        }

        // Murad's wake-up line was already triggered by WakeUpIntro at 0.6s.
        // If wake-up was skipped or not played, play Murad line first:
        if (!WakeUpIntro.IntroPlayed)
        {
            bool muradDone = false;
            DialogueManager.Instance?.SpeakMurad(
                "You're awake... My leg is trapped under this concrete. Turn on your flashlight—check the radio!",
                _muradWake, () => muradDone = true);
            while (!muradDone) yield return null;
        }

        // Natural pause (1.2 seconds) after Murad finishes speaking before radio emergency static starts
        yield return new WaitForSeconds(1.2f);

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.85f);
        yield return new WaitForSeconds(0.6f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.25f);

        bool done = false;
        DialogueManager.Instance?.PlayEntry("Stage1_Rescuer", () => done = true);
        while (!done) yield return null;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);
        SetObjective("Find Note #1 & Note #2 — calculate offset and tune radio (Counter signal drift!)");
    }

    // ── Stage transitions ─────────────────────────────────────────────────────

    public void OnRadioStage2Ready()
    {
        if (CurrentStage >= PuzzleStage.Stage2_CutWires) return;
        CurrentStage = PuzzleStage.Stage2_CutWires;
        SetObjective("Check the wall clock and Note #3 — discharge fuse box wires in exact order");
    }

    void CompleteWireStage()
    {
        CurrentStage = PuzzleStage.Stage3_TurnPipeValve;
        UnlockLight(lockLight1, 1);
        CloseFuseBox();

        // Start 3D pipe knocking sound ONLY on the active valve!
        if (_valves != null)
        {
            for (int i = 0; i < _valves.Length; i++)
            {
                if (_valves[i] == null) continue;
                var src = _valves[i].GetComponent<AudioSource>();
                if (src != null)
                {
                    if (i == _activeValveIndex) src.Play();
                    else src.Stop();
                }
            }
        }

        radioSystem?.OnWiresComplete();
        DialogueManager.Instance?.SetSubtitle("LOCK 1 OPEN — Listen for the wrench knocking and find the right valve!");
        SetObjective("Listen for the knocking valve — hold and release E in the GREEN sweet spot (70%-85%)");
    }

    void CompleteValveStage()
    {
        _valveDone   = true;
        _valveHolding = false;
        _holdingCorrectValve = false;
        if (valveHoldUI != null) valveHoldUI.SetActive(false);

        CurrentStage = PuzzleStage.Stage4_EnterDoorCode;
        UnlockLight(lockLight2, 2);

        // Stop all valve knocking sounds
        if (_valves != null)
        {
            foreach (var v in _valves)
            {
                if (v != null) v.GetComponent<AudioSource>()?.Stop();
            }
        }

        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.PlaySteamRelease();

        radioSystem?.OnValveComplete();
        SetKeypadScreenEmission(true);
        SetObjective("Combine the clues (sun board & clock) — deduce the 4-digit code and enter on keypad");
    }

    // ── Lock lights ───────────────────────────────────────────────────────────

    void BuildLockLightMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        _matRed = new Material(shader);
        _matRed.SetColor("_BaseColor",     new Color(0.6f, 0.04f, 0.04f));
        _matRed.SetColor("_EmissionColor", new Color(0.55f, 0.02f, 0.02f));
        _matRed.EnableKeyword("_EMISSION");

        _matGreen = new Material(shader);
        _matGreen.SetColor("_BaseColor",     new Color(0.04f, 0.6f, 0.08f));
        _matGreen.SetColor("_EmissionColor", new Color(0.02f, 0.55f, 0.05f));
        _matGreen.EnableKeyword("_EMISSION");

        SetLockLight(lockLight1, false);
        SetLockLight(lockLight2, false);
        SetLockLight(lockLight3, false);
    }

    void SetupKeypadScreenMaterial()
    {
        // Find the keypad 3D object and grab a unique instance of its material
        // so we can toggle the emission at runtime without affecting other objects.
        var allT = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in allT)
        {
            if (!t.name.ToLower().Contains("keypad")) continue;
            var mr = t.GetComponentInChildren<MeshRenderer>(true);
            if (mr == null) mr = t.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            // .material creates a unique instance
            _keypadScreenMat = mr.material;
            break;
        }
        // Start with emission off — keypad screen is dark until Stage 4
        SetKeypadScreenEmission(false);
    }

    void SetupFuseBoxDoor()
    {
        var go = GameObject.Find("Fuse_Box_Door");
        if (go == null)
        {
            var parent = GameObject.Find("Fuse_Box");
            if (parent != null)
            {
                foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.ToLower().Contains("door")) { go = t.gameObject; break; }
                }
        }
        }
        if (go == null)
        {
            // Last resort: search all transforms
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == "Fuse_Box_Door") { go = t.gameObject; break; }
            }
        }

        if (go == null)
        {
            Debug.LogWarning("[FuseBoxDoor] NOT FOUND in scene — door animation skipped.");
            return;
        }

        _fuseBoxDoor = go.transform;
        go.isStatic  = false;   // prevent static batching from locking the transform

        _fuseBoxDoorOpenRot   = _fuseBoxDoor.rotation;
        _fuseBoxDoorClosedRot = Quaternion.AngleAxis(90f, Vector3.up) * _fuseBoxDoorOpenRot;

        _fuseBoxDoor.rotation = _fuseBoxDoorClosedRot;
        Debug.Log($"[FuseBoxDoor] Found '{go.name}'. Open={_fuseBoxDoorOpenRot.eulerAngles} Closed={_fuseBoxDoorClosedRot.eulerAngles}");
    }

    IEnumerator AnimateFuseBoxDoor()
    {
        if (_fuseBoxDoor == null) yield break;

        float elapsed = 0f;
        const float duration = 0.65f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            _fuseBoxDoor.rotation = Quaternion.Slerp(_fuseBoxDoorClosedRot, _fuseBoxDoorOpenRot, t);
            yield return null;
        }

        _fuseBoxDoor.rotation = _fuseBoxDoorOpenRot;
    }

    void SetKeypadScreenEmission(bool on)
    {
        if (_keypadScreenMat == null) return;
        if (on)
        {
            _keypadScreenMat.EnableKeyword("_EMISSION");
            _keypadScreenMat.SetColor("_EmissionColor", new Color(0.7f, 0.04f, 0.04f));
        }
        else
        {
            _keypadScreenMat.SetColor("_EmissionColor", Color.black);
            _keypadScreenMat.DisableKeyword("_EMISSION");
        }
    }

    void SetLockLight(GameObject go, bool green)
    {
        if (go == null) return;
        var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            mr.material = green ? _matGreen : _matRed;
        }
        var lights = go.GetComponentsInChildren<Light>(true);
        foreach (var l in lights)
        {
            l.color = green ? Color.green : Color.red;
        }
    }

    void UnlockLight(GameObject go, int index, bool isFinalDoor = false)
    {
        SetLockLight(go, true);
        if (isFinalDoor)
        {
            if (ProceduralAudio.Instance != null)
                ProceduralAudio.Instance.PlayHeavyDoorUnlock();
            DialogueManager.Instance?.SetSubtitle("LOCK 3 RELEASED — HYDRAULIC DOOR OVERRIDE ENGAGED");
        }
        else
        {
            if (ProceduralAudio.Instance != null)
                ProceduralAudio.Instance.PlayLatchUnlock();
            DialogueManager.Instance?.SetSubtitle($"LOCK {index} RELEASED");
        }
    }

    // ── Clue inspection (Upgraded Riddles & Deduction) ─────────────────────────

    public void InspectChalkBoard()
    {
        if (CurrentStage < PuzzleStage.Stage4_EnterDoorCode) return;
        ShowClue("Scratched into the wall under the chalk sun drawing:\n\n\"Days above ground: 04\nDays trapped below: 03\n— Sum: [First Pair] —\"\n\n(04 + 03 = 07)");
    }

    public void InspectClock()
    {
        if (CurrentStage < PuzzleStage.Stage2_CutWires) return;
        ShowClue("The clock stopped.\n\n03 : 15\n\n(Hour hand is at 3 — an ODD number.\nMinute hand is stopped at 15.)");
    }

    public void InspectPhoto()
    {
        if (CurrentStage < PuzzleStage.Stage4_EnterDoorCode) return;
        ShowClue("[You inspect the waterlogged photo floating on the surface]\n\nFaded ink on the back:\n\n\"03 / ?? — Look at the stopped clock's minute hand for the day we were trapped.\"\n\n(Minute hand is at 15)");
    }

    public void InspectClue(int i)
    {
        if (i == 0 && CurrentStage < PuzzleStage.Stage1_TuneBackupFrequency) return;
        if (i == 1 && CurrentStage < PuzzleStage.Stage1_TuneBackupFrequency) return;
        if (i == 2 && CurrentStage < PuzzleStage.Stage2_CutWires) return;

        string[] notes = {
            "EMERGENCY RELAY LOG:\n\nBase Frequency = 102.0 MHz.\n\nApply the Sector B Receiver Offset written on Note #2.",
            "SECTOR B MAINTENANCE NOTE:\n\nReceiver Drift Offset = -3.6 MHz.\n\n(Target Relay Frequency = Base Frequency + Offset)",
            "MAG-LOCK BYPASS PROTOCOL:\n\nCheck wall clock.\nIf the hour is ODD, discharge in sequence:\n1. YELLOW (Ground)\n2. BLUE (Capacitor)\n3. RED (Mag-Lock Release)\n\nWARNING: Cutting RED before discharging Yellow & Blue causes an arc surge!"
        };
        if (i >= 0 && i < notes.Length) ShowClue(notes[i]);
    }

    void ShowClue(string text)
    {
        if (clueInspectPanel != null)
        {
            clueInspectPanel.SetActive(true);
            EnsureClueCloseButton();
        }
        if (clueInspectText  != null)
        {
            clueInspectText.text = text;
            clueInspectText.raycastTarget = false;
        }
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = true;
            FirstPersonController.Instance.MouseLocked = false;
        }
        WireButtons();
    }

    public void CloseClue()
    {
        if (clueInspectPanel != null) clueInspectPanel.SetActive(false);
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = false;
            FirstPersonController.Instance.MouseLocked = true;
        }
    }

    // ── Fuse Box (Stage 2: 3-Wire Discharge Yellow -> Blue -> Red) ───────────

    public void OpenFuseBox()
    {
        if (CurrentStage != PuzzleStage.Stage2_CutWires) return;

        if (!_fuseBoxDoorOpened)
        {
            _fuseBoxDoorOpened = true;
            StartCoroutine(OpenFuseBoxSequence());
        }
        else
        {
            ShowFusePanel();
        }
    }

    IEnumerator OpenFuseBoxSequence()
    {
        // Animate door open first
        yield return StartCoroutine(AnimateFuseBoxDoor());
        // Then show the modal
        ShowFusePanel();
    }

    void ShowFusePanel()
    {
        if (fusePanelUI != null)
        {
            fusePanelUI.SetActive(true);
            EnsureFuseCloseButton();
        }
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = true;
            FirstPersonController.Instance.MouseLocked = false;
        }
        RefreshFuseButtonVisibility();
        WireButtons();
    }

    public void CloseFuseBox()
    {
        if (fusePanelUI != null) fusePanelUI.SetActive(false);
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = false;
            FirstPersonController.Instance.MouseLocked = true;
        }
    }

    public void CutWire(string color)
    {
        if (CurrentStage != PuzzleStage.Stage2_CutWires) return;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlaySpark();

        string c = (color ?? "").ToLower().Trim();

        if (c == "yellow" && _wireCutStep == 0)
        {
            _wireCutStep = 1;
            if (wireYellow != null) wireYellow.SetActive(false);
            SetStatus("<color=#FFCC00>YELLOW (Ground) cut [1/3].</color> Next: Cut BLUE (Capacitor).");
            RefreshFuseButtonVisibility();
        }
        else if (c == "blue" && _wireCutStep == 1)
        {
            _wireCutStep = 2;
            if (wireBlue != null) wireBlue.SetActive(false);
            SetStatus("<color=#44AAFF>BLUE (Capacitor) cut [2/3].</color> Next: Cut RED (Mag-Lock Release).");
            RefreshFuseButtonVisibility();
        }
        else if (c == "red" && _wireCutStep == 2)
        {
            _wireCutStep = 3;
            if (wireRed != null) wireRed.SetActive(false);
            SetStatus("<color=#4CFF88>MAG-LOCK RELEASED! [3/3]</color>");
            CompleteWireStage();
        }
        else
        {
            SetStatus("<color=#FF4444>ARC SURGE! Wires cut in wrong order! Water surged +4%.</color>");
            if (waterController != null) waterController.AddWaterSurge(0.04f);
            ResetWires();
        }
    }

    void ResetWires()
    {
        _wireCutStep = 0;
        if (wireRed    != null) wireRed.SetActive(true);
        if (wireBlue   != null) wireBlue.SetActive(true);
        if (wireYellow != null) wireYellow.SetActive(true);
        RefreshFuseButtonVisibility();
    }

    void RefreshFuseButtonVisibility()
    {
        SetFuseBtnVisible("BtnYellow", _wireCutStep == 0);
        SetFuseBtnVisible("BtnBlue",   _wireCutStep < 2);
        SetFuseBtnVisible("BtnRed",    _wireCutStep < 3);

        if (wireYellow != null) wireYellow.SetActive(_wireCutStep == 0);
        if (wireBlue   != null) wireBlue.SetActive(_wireCutStep < 2);
        if (wireRed    != null) wireRed.SetActive(_wireCutStep < 3);
    }

    void SetFuseBtnVisible(string name, bool visible)
    {
        var t = FindChild(fusePanelUI?.transform, name);
        if (t != null) t.gameObject.SetActive(visible);
    }

    // ── Stage 3: 3-Valve Acoustic Triangulation & Sweet Spot Timing ──────────

    public void BeginValveHold(Transform valveTransform)
    {
        if (CurrentStage != PuzzleStage.Stage3_TurnPipeValve || _valveDone) return;

        GameObject activeValve = (_valves != null && _activeValveIndex < _valves.Length) ? _valves[_activeValveIndex] : pressureValve;
        bool isCorrect = (valveTransform != null && activeValve != null &&
                         (valveTransform == activeValve.transform || valveTransform.IsChildOf(activeValve.transform)));

        if (!isCorrect)
        {
            // Wrong silent valve turned! Steam blast + panic attack + water surge penalty
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlaySteamRelease();
            panicSystem?.TriggerPanicNow();
            if (waterController != null) waterController.AddWaterSurge(0.04f);
            SetStatus("WRONG VALVE! High pressure steam burst! Water surged +4%.");
            return;
        }

        _holdingCorrectValve = true;
        _valveHolding = true;
        if (valveHoldUI != null) valveHoldUI.SetActive(true);
    }

    public void EndValveHold()
    {
        if (_valveDone) return;

        if (_valveHolding && _holdingCorrectValve)
        {
            if (_gaugeProgress >= 0.70f && _gaugeProgress <= 0.85f)
            {
                // Sweet Spot success!
                _valveNotchesDone++;
                if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayLatchUnlock();

                if (_valveNotchesDone == 1)
                {
                    SetStatus("NOTCH 1/3 RELEASED & LOCKED! Release next pressure notch.");
                    _gaugeProgress = 0f;
                }
                else if (_valveNotchesDone == 2)
                {
                    SetStatus("NOTCH 2/3 LOCKED! PANIC ATTACK — Steady breath before final release!");
                    panicSystem?.TriggerPanicNow();
                    DialogueManager.Instance?.SpeakMurad("Hold on, steady your breath before the final valve release!");
                    _gaugeProgress = 0f;
                }
                else if (_valveNotchesDone >= 3)
                {
                    CompleteValveStage();
                }
            }
            else if (_gaugeProgress < 0.70f && _gaugeProgress > 0.05f)
            {
                SetStatus("RELEASED TOO EARLY (<70%)! Must release in GREEN zone (70% - 85%).");
                _gaugeProgress = 0f;
            }
        }

        _valveHolding = false;
        _holdingCorrectValve = false;
        if (valveHoldUI != null) valveHoldUI.SetActive(false);
    }

    // ── Keypad (Stage 4) ──────────────────────────────────────────────────────

    public void OpenKeypad()
    {
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode) return;

        if (keypadPanelUI != null)
        {
            keypadPanelUI.SetActive(true);
            EnsureKeypadCloseButton();
        }
        _keypadInput = "";
        RefreshKeypadDisplay();
        if (keypadDisplay != null)
            keypadDisplay.color = new Color(0.88f, 0.12f, 0.12f);
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = true;
            FirstPersonController.Instance.MouseLocked = false;
        }
        WireButtons();
    }

    public void CloseKeypad()
    {
        if (keypadPanelUI != null) keypadPanelUI.SetActive(false);
        if (keypadDisplay != null) keypadDisplay.color = Color.white;
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = false;
            FirstPersonController.Instance.MouseLocked = true;
        }
    }

    public void KeypadPress(string digit)
    {
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayKeypadBeep();
        if (digit == "Clear")        { _keypadInput = ""; }
        else if (digit == "Enter")   { CheckCode(); return; }
        else if (_keypadInput.Length < 4) _keypadInput += digit;
        RefreshKeypadDisplay();
    }

    void RefreshKeypadDisplay()
    {
        if (keypadDisplay != null)
        {
            keypadDisplay.text = _keypadInput.PadRight(4, '_');
            keypadDisplay.raycastTarget = false;
        }
    }

    void CheckCode()
    {
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode) return;

        if (_keypadInput == CODE)
        {
            CurrentStage = PuzzleStage.Stage5_DoorUnlocked;
            SetStatus("ACCESS GRANTED — 15-SECOND HYDRAULIC OVERRIDE INITIATED");

            if (keypadDisplay != null) keypadDisplay.color = new Color(0.1f, 0.9f, 0.2f);
            SetKeypadScreenEmission(false);
            CloseKeypad();

            UnlockLight(lockLight3, 3, isFinalDoor: true);

            DialogueManager.Instance?.SpeakRescuer(
                "Code accepted! Hydraulic override active—fifteen seconds till the door opens!",
                _rescuerDoorOverride);

            if (!_doorOpenStarted)
            {
                _doorOpenStarted = true;
                StartCoroutine(OpenExitDoorRoutine());
            }

            SetObjective("HYDRAULIC OVERRIDE IN PROGRESS — SURVIVE 15 SECONDS!");
        }
        else
        {
            SetStatus("ACCESS DENIED! Invalid cipher. Water surged +3%.");
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayBuzzer();
            if (waterController != null) waterController.AddWaterSurge(0.03f);
            _keypadInput = "";
            RefreshKeypadDisplay();
        }
    }

    IEnumerator OpenExitDoorRoutine()
    {
        float elapsed = 0f;
        float duration = 15.0f; // 15-second Hydraulic Door Override
        Quaternion start = (exitDoor != null) ? exitDoor.localRotation : Quaternion.identity;
        Quaternion end = start * Quaternion.Euler(0f, -90f, 0f);

        // Build the "countless doors" void behind the door (visible through the gap while it opens)
        DoorVoidEnding.Prepare(exitDoor);

        // Arm immediately — cinematic starts as soon as the code is accepted, no waiting.
        if (!triggerEndingCutsceneAutomatically) DoorVoidEnding.Arm();

        // Panic only if the cinematic hasn't already taken control.
        if (!DoorVoidEnding.IsCinematicActive) panicSystem?.TriggerPanicNow();

        float surgeTimer = 0f;
        float creakTimer = 0f;

        while (elapsed < duration)
        {
            // Cinematic has taken over — stop grinding the door.
            if (DoorVoidEnding.IsCinematicActive) break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            if (exitDoor != null)
                exitDoor.localRotation = Quaternion.Slerp(start, end, smooth);

            // Grinding / creaking audio
            creakTimer += Time.deltaTime;
            if (creakTimer >= 2.5f)
            {
                creakTimer = 0f;
                ProceduralAudio.Instance?.PlayDoorCreak();
            }

            // Rapid water surging during override
            surgeTimer += Time.deltaTime;
            if (surgeTimer >= 3.5f)
            {
                surgeTimer = 0f;
                waterController?.AddWaterSurge(0.02f);
            }

            yield return null;
        }

        // Only snap to fully-open if the cinematic hasn't already taken over (it will snap the door itself).
        if (!DoorVoidEnding.IsCinematicActive && exitDoor != null) exitDoor.localRotation = end;

        onExitDoorOpened?.Invoke();

        if (triggerEndingCutsceneAutomatically && _ending != null)
        {
            _ending.Trigger();
        }
    }

    // ── Close all panels (Escape) ─────────────────────────────────────────────

    public void CloseAllPanels()
    {
        CloseFuseBox();
        CloseClue();
        CloseKeypad();
        EndValveHold();
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = false;
            FirstPersonController.Instance.MouseLocked = true;
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        UpdatePanelDirectClicksAndShortcuts();

        if (!_valveHolding || !_holdingCorrectValve || CurrentStage != PuzzleStage.Stage3_TurnPipeValve) return;

        _gaugeProgress += Time.deltaTime * 0.32f; // ~3.1 seconds to fill from 0 to 1

        if (valveProgressSlider != null)
            valveProgressSlider.value = Mathf.Clamp01(_gaugeProgress);

        int pct = Mathf.RoundToInt(_gaugeProgress * 100f);

        if (_gaugeProgress > 0.85f)
        {
            // Pressure overload (>85%)! Vented
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlaySteamRelease();
            _gaugeProgress = 0f;
            SetStatus("PRESSURE OVERLOAD (>85%)! Pressure vented. Try notch again.");
            if (valveProgressText != null)
                valveProgressText.text = "<color=#FF4444>OVERLOAD (>85%)! VENTED</color>";
        }
        else if (_gaugeProgress >= 0.70f)
        {
            if (valveProgressText != null)
                valveProgressText.text = $"<color=#4CFF88>SWEET SPOT ({pct}%) — RELEASE [E] NOW! [NOTCH {_valveNotchesDone + 1}/3]</color>";
        }
        else
        {
            if (valveProgressText != null)
                valveProgressText.text = $"GAUGE: {pct}% [HOLD E — RELEASE IN GREEN ZONE (70%-85%)]";
        }

        // Rotate active valve wheel visually
        GameObject activeValve = (_valves != null && _activeValveIndex < _valves.Length) ? _valves[_activeValveIndex] : pressureValve;
        if (activeValve != null)
        {
            var wheel = FindChild(activeValve.transform, "Valve_Wheel");
            if (wheel != null) wheel.Rotate(Vector3.right, 90f * Time.deltaTime,Space.World);
        }
    }

    void UpdatePanelDirectClicksAndShortcuts()
    {
        // ── 1. Fuse Panel ───────────────────────────────────────────────────
        if (fusePanelUI != null && fusePanelUI.activeSelf)
        {
            // Mouse clicks are handled by the EventSystem via onClick listeners.
            // Only handle keyboard shortcuts here to avoid double-firing.
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.Y))
                CutWire("yellow");
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.B))
                CutWire("blue");
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3) || Input.GetKeyDown(KeyCode.R))
                CutWire("red");
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.E))
                CloseFuseBox();
            return;
        }

        // ── 2. Clue Panel ───────────────────────────────────────────────────
        if (clueInspectPanel != null && clueInspectPanel.activeSelf)
        {
            // Direct mouse click check
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mPos = Input.mousePosition;
                if (_btnCloseClue != null && _btnCloseClue.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(_btnCloseClue.GetComponent<RectTransform>(), mPos, null))
                {
                    CloseClue();
                    return;
                }
            }

            // Keyboard shortcuts
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.C) ||
                Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space))
            {
                CloseClue();
            }
            return;
        }

        if (keypadPanelUI != null && keypadPanelUI.activeSelf)
        {
            // Keyboard input for digits 0-9
            for (int i = 0; i <= 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    KeypadPress(i.ToString());
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                KeypadPress("Enter");
            }
            else if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            {
                KeypadPress("Clear");
            }
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.C))
            {
                CloseKeypad();
            }
            return;
        }
    }
    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        DialogueManager.Instance?.SetSubtitle(msg);
    }

    public void SetObjective(string text) => DialogueManager.Instance?.SetObjective(text);

    void EnsureEventSystemInputModule()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            var esObj = GameObject.Find("EventSystem");
            if (esObj != null) es = esObj.GetComponent<UnityEngine.EventSystems.EventSystem>();
        }
        if (es != null)
        {
            // Add InputSystemUIInputModule if Unity.InputSystem is present in runtime
            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null && es.GetComponent(inputModuleType) == null)
            {
                es.gameObject.AddComponent(inputModuleType);
            }
        }
    }

    void EnsureBackdrop(GameObject panel, UnityEngine.Events.UnityAction onClose)
    {
        if (panel == null) return;
        var existing = FindChild(panel.transform, "ClickBackdrop");
        if (existing != null)
        {
            var existBtn = existing.GetComponent<Button>();
            if (existBtn != null) { existBtn.onClick.RemoveAllListeners(); existBtn.onClick.AddListener(onClose); }
            existing.SetAsFirstSibling();
            return;
        }

        var go = new GameObject("ClickBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel.transform, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = new Color(0, 0, 0, 0);
        cols.highlightedColor = new Color(0, 0, 0, 0);
        cols.pressedColor     = new Color(0, 0, 0, 0);
        cols.selectedColor    = new Color(0, 0, 0, 0);
        btn.colors = cols;
        btn.onClick.AddListener(onClose);
    }

    void EnsureFuseCloseButton()
    {
        if (fusePanelUI == null) return;
        EnsureBackdrop(fusePanelUI, CloseFuseBox);
        var btnT = FindChild(fusePanelUI.transform, "CloseFuse");
        if (btnT == null)
        {
            _btnCloseFuse = CreatePanelButton(fusePanelUI.transform, "CloseFuse", " CLOSE (ESC)",
                new Vector2(0.5f, 0.1f), Vector2.zero, new Vector2(180, 44), CloseFuseBox);
        }
        else
        {
            _btnCloseFuse = btnT.GetComponent<Button>();
            if (_btnCloseFuse != null)
            {
                FixButtonRaycasts(_btnCloseFuse);
                _btnCloseFuse.onClick.RemoveAllListeners();
                _btnCloseFuse.onClick.AddListener(CloseFuseBox);
            }
            var txt = btnT.GetComponentInChildren<Text>(true);
            if (txt != null) { txt.text = " CLOSE (ESC)"; txt.raycastTarget = false; }
            var tmp = btnT.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) { tmp.text = " CLOSE (ESC)"; tmp.raycastTarget = false; }
            btnT.gameObject.SetActive(true);
        }
    }

    void EnsureClueCloseButton()
    {
        if (clueInspectPanel == null) return;
        EnsureBackdrop(clueInspectPanel, CloseClue);
        var btnT = FindChild(clueInspectPanel.transform, "CloseClue");
        if (btnT == null)
        {
            _btnCloseClue = CreatePanelButton(clueInspectPanel.transform, "CloseClue", " CLOSE (ESC)",
                new Vector2(0.5f, 0.1f), Vector2.zero, new Vector2(180, 44), CloseClue);
        }
        else
        {
            _btnCloseClue = btnT.GetComponent<Button>();
            if (_btnCloseClue != null)
            {
                FixButtonRaycasts(_btnCloseClue);
                _btnCloseClue.onClick.RemoveAllListeners();
                _btnCloseClue.onClick.AddListener(CloseClue);
            }
            var txt = btnT.GetComponentInChildren<Text>(true);
            if (txt != null) { txt.text = " CLOSE (ESC)"; txt.raycastTarget = false; }
            var tmp = btnT.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) { tmp.text = " CLOSE (ESC)"; tmp.raycastTarget = false; }
            btnT.gameObject.SetActive(true);
        }
    }

    void EnsureKeypadCloseButton()
    {
        if (keypadPanelUI == null) return;
        EnsureBackdrop(keypadPanelUI, CloseKeypad);
        var btnT = FindChild(keypadPanelUI.transform, "CloseKeypad");
        if (btnT == null)
        {
            _btnCloseKeypad = CreatePanelButton(keypadPanelUI.transform, "CloseKeypad", " CLOSE (ESC)",
                new Vector2(0.5f, 0.08f), Vector2.zero, new Vector2(200, 40), CloseKeypad);
        }
        else
        {
            _btnCloseKeypad = btnT.GetComponent<Button>();
            if (_btnCloseKeypad != null)
            {
                FixButtonRaycasts(_btnCloseKeypad);
                _btnCloseKeypad.onClick.RemoveAllListeners();
                _btnCloseKeypad.onClick.AddListener(CloseKeypad);
            }
            btnT.gameObject.SetActive(true);
        }
    }

    void FixButtonRaycasts(Button btn)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.raycastTarget = true;
        foreach (var t in btn.GetComponentsInChildren<Text>(true)) t.raycastTarget = false;
        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) tmp.raycastTarget = false;
    }

    Button CreatePanelButton(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.2f, 0.26f, 0.95f);
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(0.18f, 0.2f, 0.26f, 0.95f);
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f, 1f);
        colors.pressedColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(action);

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        tmp.raycastTarget = false;

        return btn;
    }

    void WireButtons()
    {
        EnsureKeypadCloseButton();
        EnsureFuseCloseButton();
        EnsureClueCloseButton();

        // Keypad
        if (keypadPanelUI != null)
        {
            foreach (string d in new[]{"7","8","9","4","5","6","1","2","3","Clear","0","Enter"})
            {
                var btn = FindChild(keypadPanelUI.transform, "Key_" + d)?.GetComponent<Button>();
                if (btn == null) continue;
                FixButtonRaycasts(btn);
                btn.onClick.RemoveAllListeners();
                string cap = d;
                btn.onClick.AddListener(() => KeypadPress(cap));
            }

            _btnCloseKeypad = FindChild(keypadPanelUI.transform, "CloseKeypad")?.GetComponent<Button>();
            if (_btnCloseKeypad != null)
            {
                FixButtonRaycasts(_btnCloseKeypad);
                _btnCloseKeypad.onClick.RemoveAllListeners();
                _btnCloseKeypad.onClick.AddListener(CloseKeypad);
            }
        }

        // Fuse
        if (fusePanelUI != null)
        {
            _btnRed = WireBtn(fusePanelUI, "BtnRed", () => CutWire("red"));
            _btnBlue = WireBtn(fusePanelUI, "BtnBlue", () => CutWire("blue"));
            _btnYellow = WireBtn(fusePanelUI, "BtnYellow", () => CutWire("yellow"));
            _btnCloseFuse = WireBtn(fusePanelUI, "CloseFuse", CloseFuseBox);
        }

        // Clue close
        if (clueInspectPanel != null)
        {
            _btnCloseClue = WireBtn(clueInspectPanel, "CloseClue", CloseClue);
        }
    }

    Button WireBtn(GameObject panel, string childName, UnityEngine.Events.UnityAction action)
    {
        var btn = FindChild(panel?.transform, childName)?.GetComponent<Button>();
        if (btn == null) return null;
        FixButtonRaycasts(btn);
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
        return btn;
    }

    static Transform FindChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
