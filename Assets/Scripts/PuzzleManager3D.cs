using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
    [SerializeField] Text        valveProgressText;

    [Header("Systems")]
    [SerializeField] RadioSystem   radioSystem;
    [SerializeField] PanicAttack3D panicSystem;

    [Header("UI Panels")]
    [SerializeField] GameObject fusePanelUI;
    [SerializeField] GameObject keypadPanelUI;
    [SerializeField] GameObject clueInspectPanel;
    [SerializeField] Text       clueInspectText;
    [SerializeField] Text       keypadDisplay;
    [SerializeField] Text       statusText;

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
    EndingSequence3D _ending;

    // Character voice audio clips
    AudioClip _muradWake, _muradWater, _muradValve, _muradEnd;
    AudioClip _rescuerIntro;

    void Awake()
    {
        Instance = this;
        _ending = FindObjectOfType<EndingSequence3D>();

        if (fusePanelUI      != null) fusePanelUI.SetActive(false);
        if (keypadPanelUI    != null) keypadPanelUI.SetActive(false);
        if (clueInspectPanel != null) clueInspectPanel.SetActive(false);
        if (valveHoldUI      != null) valveHoldUI.SetActive(false);

        WireButtons();
    }

    void Start()
    {
        _muradWake    = Resources.Load<AudioClip>("Audio/Murad/murad_01_wake");
        _muradWater   = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
        _muradValve   = Resources.Load<AudioClip>("Audio/Murad/murad_03_valve");
        _muradEnd     = Resources.Load<AudioClip>("Audio/Murad/murad_04_end");
        _rescuerIntro = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_01_intro");
        Debug.Log($"[PuzzleManager] Murad clips — wake:{_muradWake != null} water:{_muradWater != null} valve:{_muradValve != null} end:{_muradEnd != null}");

        BuildLockLightMaterials();
        AutoFindReferences();
        SetupValves();
        SetupSweetSpotIndicator();

        SetObjective("Explore the basement — turn on your flashlight [LEFT CLICK]");
    }

    void AutoFindReferences()
    {
        if (lockLight1 == null) lockLight1 = GameObject.Find("LockLight_1");
        if (lockLight2 == null) lockLight2 = GameObject.Find("LockLight_2");
        if (lockLight3 == null) lockLight3 = GameObject.Find("LockLight_3");

        if (pressureValve == null) pressureValve = GameObject.Find("Pressure_Valve");
        if (valveAudioSource == null && pressureValve != null)
            valveAudioSource = pressureValve.GetComponent<AudioSource>();

        if (radioSystem == null) radioSystem = FindFirstObjectByType<RadioSystem>();
        if (panicSystem == null) panicSystem = FindFirstObjectByType<PanicAttack3D>();
        if (waterController == null) waterController = FindFirstObjectByType<WaterController3D>();

        if (exitDoor == null)
        {
            var allT = FindObjectsByType<Transform>(FindObjectsSortMode.None);
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
        yield return new WaitForSeconds(1.0f);

        bool done = false;
        DialogueManager.Instance?.SpeakMurad(
            "You're finally awake... My leg is pinned under this concrete, I can't move. Check the radio on the table!",
            _muradWake, () => done = true);
        while (!done) yield return null;

        yield return new WaitForSeconds(0.6f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.9f);
        yield return new WaitForSeconds(0.6f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.25f);

        done = false;
        DialogueManager.Instance?.SpeakRescuer(
            "Hello?! Can anyone hear me? Signal is dying... Check Note #1 and Note #2, apply the offset, and tune the radio!",
            _rescuerIntro, () => done = true);
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
        DialogueManager.Instance?.SpeakMurad(
            "I can hear the pipes from here. That knocking sound... listen closely, find which pipe it comes from!",
            _muradValve);
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
        SetObjective("Combine the clues (sun board & photo) — deduce the 4-digit code and enter on keypad");
    }

    // ── Lock lights ───────────────────────────────────────────────────────────

    void BuildLockLightMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        _matRed = new Material(shader);
        _matRed.SetColor("_BaseColor",     new Color(0.8f, 0.05f, 0.05f));
        _matRed.SetColor("_EmissionColor", new Color(3f, 0.1f, 0.1f));
        _matRed.EnableKeyword("_EMISSION");

        _matGreen = new Material(shader);
        _matGreen.SetColor("_BaseColor",     new Color(0.05f, 0.8f, 0.1f));
        _matGreen.SetColor("_EmissionColor", new Color(0.1f, 3f, 0.2f));
        _matGreen.EnableKeyword("_EMISSION");

        SetLockLight(lockLight1, false);
        SetLockLight(lockLight2, false);
        SetLockLight(lockLight3, false);
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
        if (clueInspectPanel != null) clueInspectPanel.SetActive(true);
        if (clueInspectText  != null) clueInspectText.text = text;
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = false;
    }

    public void CloseClue()
    {
        if (clueInspectPanel != null) clueInspectPanel.SetActive(false);
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;
    }

    // ── Fuse Box (Stage 2: 3-Wire Discharge Yellow -> Blue -> Red) ───────────

    public void OpenFuseBox()
    {
        if (CurrentStage != PuzzleStage.Stage2_CutWires) return;

        if (fusePanelUI != null) fusePanelUI.SetActive(true);
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = false;
        RefreshFuseButtonVisibility();
    }

    public void CloseFuseBox()
    {
        if (fusePanelUI != null) fusePanelUI.SetActive(false);
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;
    }

    public void CutWire(string color)
    {
        if (CurrentStage != PuzzleStage.Stage2_CutWires) return;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlaySpark();

        switch (color.ToLower())
        {
            case "yellow" when _wireCutStep == 0:
                if (wireYellow != null) wireYellow.SetActive(false);
                _wireCutStep = 1;
                SetStatus("Yellow (Ground) cut. Next: cut BLUE (Capacitor).");
                RefreshFuseButtonVisibility();
                break;

            case "blue" when _wireCutStep == 1:
                if (wireBlue != null) wireBlue.SetActive(false);
                _wireCutStep = 2;
                SetStatus("Blue (Capacitor) cut. Next: cut RED (Mag-Lock Release).");
                RefreshFuseButtonVisibility();
                break;

            case "red" when _wireCutStep == 2:
                if (wireRed != null) wireRed.SetActive(false);
                _wireCutStep = 3;
                CompleteWireStage();
                break;

            default:
                SetStatus("ARC SURGE! Wires cut in wrong order! Water surged +4%.");
                if (waterController != null) waterController.AddWaterSurge(0.04f);
                ResetWires();
                break;
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
        SetFuseBtnVisible("BtnRed",    wireRed    == null || wireRed.activeSelf);
        SetFuseBtnVisible("BtnBlue",   wireBlue   == null || wireBlue.activeSelf);
        SetFuseBtnVisible("BtnYellow", wireYellow == null || wireYellow.activeSelf);
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

        if (keypadPanelUI != null) keypadPanelUI.SetActive(true);
        _keypadInput = "";
        RefreshKeypadDisplay();
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = false;
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
        if (keypadDisplay != null) keypadDisplay.text = _keypadInput.PadRight(4, '_');
    }

    void CheckCode()
    {
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode) return;

        if (_keypadInput == CODE)
        {
            CurrentStage = PuzzleStage.Stage5_DoorUnlocked;
            SetStatus("ACCESS GRANTED — 15-SECOND HYDRAULIC OVERRIDE INITIATED");

            if (keypadPanelUI != null) keypadPanelUI.SetActive(false);
            if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;

            UnlockLight(lockLight3, 3, isFinalDoor: true);

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

        // Final panic attack during hydraulic grind
        panicSystem?.TriggerPanicNow();

        DialogueManager.Instance?.SpeakMurad(
            "Go... don't look back. I'll find another way out. I promise.",
            _muradEnd);

        float surgeTimer = 0f;
        float creakTimer = 0f;

        while (elapsed < duration)
        {
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

        if (exitDoor != null) exitDoor.localRotation = end;

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
        if (keypadPanelUI != null) keypadPanelUI.SetActive(false);
        EndValveHold();
        if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
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
            if (wheel != null) wheel.Rotate(Vector3.forward, 90f * Time.deltaTime);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        DialogueManager.Instance?.SetSubtitle(msg);
    }

    public void SetObjective(string text) => DialogueManager.Instance?.SetObjective(text);

    void WireButtons()
    {
        // Keypad
        if (keypadPanelUI != null)
        {
            foreach (string d in new[]{"7","8","9","4","5","6","1","2","3","Clear","0","Enter"})
            {
                var btn = FindChild(keypadPanelUI.transform, "Key_" + d)?.GetComponent<Button>();
                if (btn == null) continue;
                btn.onClick.RemoveAllListeners();
                string cap = d;
                btn.onClick.AddListener(() => KeypadPress(cap));
            }
        }
        // Fuse
        if (fusePanelUI != null)
        {
            WireBtn(fusePanelUI, "BtnRed",    () => CutWire("red"));
            WireBtn(fusePanelUI, "BtnBlue",   () => CutWire("blue"));
            WireBtn(fusePanelUI, "BtnYellow", () => CutWire("yellow"));
            WireBtn(fusePanelUI, "CloseFuse", CloseFuseBox);
        }
        // Clue close
        if (clueInspectPanel != null)
            WireBtn(clueInspectPanel, "CloseClue", CloseClue);
    }

    void WireBtn(GameObject panel, string childName, UnityEngine.Events.UnityAction action)
    {
        var btn = FindChild(panel?.transform, childName)?.GetComponent<Button>();
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    static Transform FindChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
