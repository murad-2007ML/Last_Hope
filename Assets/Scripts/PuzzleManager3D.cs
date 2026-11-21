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
    [SerializeField] float       valveHoldRequired = 6f; // 6.0 seconds across 3 notches
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

    // Runtime state
    int   _wireCutStep;
    bool  _valveHolding;
    float _valveHoldTimer;
    float _notchFloorTimer;
    bool  _valveDone;
    bool  _stage3PanicTriggered;
    string _keypadInput = "";
    const string CODE = "0715";

    Material _matRed, _matGreen;
    EndingSequence3D _ending;
    bool _doorOpenStarted;

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

        if (valveAudioSource != null)
        {
            valveAudioSource.clip         = ProceduralAudio.GenerateClangClip();
            valveAudioSource.loop         = true;
            valveAudioSource.spatialBlend = 1f;
            valveAudioSource.minDistance  = 0.5f;
            valveAudioSource.maxDistance  = 8f;
            valveAudioSource.playOnAwake  = false;
            valveAudioSource.Stop(); // Silent until Stage 3 starts!
        }

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

    // ── Silent Gating Checker for PlayerInteraction ──────────────────────────

    public bool IsInteractableAllowed(string nameLower)
    {
        switch (CurrentStage)
        {
            case PuzzleStage.Stage0_Locked:
                // Pre-start: nothing is interactable until flashlight is turned on
                return false;

            case PuzzleStage.Stage1_TuneBackupFrequency:
                // Vintage_Radio: Active from Stage1 onward.
                // Clue_Paper_1 (98.4 FM): Active in Stage1+.
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                if (nameLower.Contains("clue_paper_1")) return true;
                return false;

            case PuzzleStage.Stage2_CutWires:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                if (nameLower.Contains("clue_paper_1")) return true;
                if (nameLower.Contains("clue_paper_2")) return true;
                // Fuse_Box & Wall_Clock: Active ONLY in Stage2
                if (nameLower.Contains("fuse_box") || nameLower.Contains("fuse")) return true;
                if (nameLower.Contains("wall_clock") || nameLower.Contains("clock")) return true;
                return false;

            case PuzzleStage.Stage3_TurnPipeValve:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                // Pressure_Valve: Active ONLY in Stage3
                if (nameLower.Contains("pressure_valve") || nameLower.Contains("valve")) return true;
                return false;

            case PuzzleStage.Stage4_EnterDoorCode:
                // Vintage_Radio active from Stage1 onward
                if (nameLower.Contains("vintage_radio") || nameLower.Contains("radio")) return true;
                // Door_Keypad, Chalk_Sun_Board, & Floating_Photo: Active ONLY in Stage4
                if (nameLower.Contains("door_keypad") || nameLower.Contains("keypad")) return true;
                if (nameLower.Contains("chalk_sun_board") || nameLower.Contains("sun")) return true;
                if (nameLower.Contains("floating_photo") || nameLower.Contains("photo")) return true;
                if (nameLower.Contains("clue_paper_3")) return true;
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
        // Immediately transition to Stage 1 so radio is interactable at once!
        if (CurrentStage == PuzzleStage.Stage0_Locked)
        {
            CurrentStage = PuzzleStage.Stage1_TuneBackupFrequency;
            SetObjective("Find the note on the desk — tune radio to 98.4 FM");
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
            "Hello?! Can anyone hear me? Signal is dying... Check the Sector B note on the far desk and tune the radio to 98.4 FM!",
            _rescuerIntro, () => done = true);
        while (!done) yield return null;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);
        SetObjective("Find the note on the desk — tune radio to 98.4 FM");
    }

    // ── Stage transitions ─────────────────────────────────────────────────────

    public void OnRadioStage2Ready()
    {
        if (CurrentStage >= PuzzleStage.Stage2_CutWires) return;
        CurrentStage = PuzzleStage.Stage2_CutWires;
        SetObjective("Check the wall clock — open fuse box and cut YELLOW then BLUE");
    }

    void CompleteWireStage()
    {
        CurrentStage = PuzzleStage.Stage3_TurnPipeValve;
        UnlockLight(lockLight1, 1);
        CloseFuseBox();

        // Start 3D pipe clanking sound when Stage 3 begins
        if (valveAudioSource != null && !valveAudioSource.isPlaying)
            valveAudioSource.Play();

        radioSystem?.OnWiresComplete();
        DialogueManager.Instance?.SpeakMurad(
            "I can hear the pipes from here. That sound... it's coming from behind the wall.",
            _muradValve);
        SetObjective("Locate the clanging pipe and turn its valve (Hold E)");
    }

    void CompleteValveStage()
    {
        _valveDone  = true;
        CurrentStage = PuzzleStage.Stage4_EnterDoorCode;
        UnlockLight(lockLight2, 2);

        // Stop 3D pipe clanking sound
        if (valveAudioSource != null && valveAudioSource.isPlaying)
            valveAudioSource.Stop();

        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.PlaySteamRelease();

        radioSystem?.OnValveComplete();
        panicSystem?.TriggerPanicNow();
        SetObjective("Combine the clues (sun board & photo) — enter code on keypad");
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
            DialogueManager.Instance?.SetSubtitle("LOCK 3 RELEASED — DOOR UNLOCKED");
        }
        else
        {
            if (ProceduralAudio.Instance != null)
                ProceduralAudio.Instance.PlayLatchUnlock();
            DialogueManager.Instance?.SetSubtitle($"LOCK {index} RELEASED");
        }
    }

    // ── Clue inspection ───────────────────────────────────────────────────────

    public void InspectChalkBoard()
    {
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode) return;
        ShowClue("Scratched onto the board in chalk:\n\n[ 07 ]\n\nA child's handwriting. Yours.\n\"The sun has two rays.\"");
    }

    public void InspectClock()
    {
        if (CurrentStage != PuzzleStage.Stage2_CutWires) return;
        ShowClue("The clock stopped.\n\n03 : 15\n\n\"Before 04:00 — cut YELLOW, then BLUE. Never Red.\"");
    }

    public void InspectPhoto()
    {
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode) return;
        ShowClue("[You flip the photo over]\n\nFaded ink on the back:\n\n\" — 15 — \"\n\n\"Combine with the sun board.\"");
    }

    public void InspectClue(int i)
    {
        if (i == 0 && CurrentStage < PuzzleStage.Stage1_TuneBackupFrequency) return;
        if (i == 1 && CurrentStage < PuzzleStage.Stage2_CutWires) return;
        if (i > 1 && CurrentStage < PuzzleStage.Stage4_EnterDoorCode) return;

        string[] notes = {
            "Sector B Emergency Protocol\n\nBACKUP FREQUENCY:  98.4 FM\n\n\"If primary signal lost, retune immediately.\"",
            "Maintenance Circuit Diagram:\n\n\"Lock 1 bypass requires Yellow wire then Blue wire.\nRed wire triggers emergency high-voltage surge.\"",
            "Child's drawing — a sun with numbers along each ray:\n\n  0  —  7  —  1  —  5\n\n(rays point clockwise)"
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

    // ── Fuse Box ──────────────────────────────────────────────────────────────

    public void OpenFuseBox()
    {
        // Silent gating
        if (CurrentStage != PuzzleStage.Stage2_CutWires)
            return;

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
                SetStatus("Yellow wire cut. Now cut BLUE.");
                RefreshFuseButtonVisibility();
                break;

            case "blue" when _wireCutStep == 1:
                if (wireBlue != null) wireBlue.SetActive(false);
                CompleteWireStage();
                break;

            default:
                SetStatus("WRONG ORDER! High voltage surge. Wires reset.");
                if (waterController != null) waterController.AddSpeedPercent(5f);
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

    // ── Pressure Valve (6.0s Hold across 3 notches) ──────────────────────────

    public void BeginValveHold()
    {
        if (CurrentStage != PuzzleStage.Stage3_TurnPipeValve || _valveDone) return;
        _valveHolding = true;
        if (valveHoldUI != null) valveHoldUI.SetActive(true);
    }

    public void EndValveHold()
    {
        if (_valveDone) return;
        _valveHolding = false;
        // Drop back to last reached notch
        _valveHoldTimer = _notchFloorTimer;
        float progress = Mathf.Clamp01(_valveHoldTimer / valveHoldRequired);
        if (valveProgressSlider != null) valveProgressSlider.value = progress;
        if (valveHoldUI != null) valveHoldUI.SetActive(false);
    }

    // ── Keypad ────────────────────────────────────────────────────────────────

    public void OpenKeypad()
    {
        // Silent gating
        if (CurrentStage != PuzzleStage.Stage4_EnterDoorCode)
            return;

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
            SetStatus("ACCESS GRANTED");

            // 1. Close the keypad UI and re-lock the cursor
            if (keypadPanelUI != null) keypadPanelUI.SetActive(false);
            if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;

            // 2. Turn LockLight_3 Green and play heavy door-unlock sound
            UnlockLight(lockLight3, 3, isFinalDoor: true);

            // 3. Smoothly rotate Exit_Door open around its hinge (90 degrees over 2.5s) via coroutine
            // 4. Invokes onExitDoorOpened and checks triggerEndingCutsceneAutomatically
            if (!_doorOpenStarted)
            {
                _doorOpenStarted = true;
                StartCoroutine(OpenExitDoorRoutine());
            }

            DialogueManager.Instance?.SpeakMurad(
                "Go... don't look back. I'll find another way out. I promise.",
                _muradEnd);

            SetObjective("The exit door is unlocked — escape the basement");
        }
        else
        {
            SetStatus("ACCESS DENIED");
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayKeypadBeep();
            _keypadInput = "";
            RefreshKeypadDisplay();
        }
    }

    IEnumerator OpenExitDoorRoutine()
    {
        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.PlayDoorCreak();

        if (exitDoor != null)
        {
            float elapsed = 0f;
            float duration = 2.5f; // 90 degrees over 2.5 seconds
            Quaternion start = exitDoor.localRotation;
            Quaternion end = start * Quaternion.Euler(0f, -90f, 0f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                exitDoor.localRotation = Quaternion.Slerp(start, end, smooth);
                yield return null;
            }
            exitDoor.localRotation = end;
        }

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
        if (!_valveHolding || _valveDone || CurrentStage != PuzzleStage.Stage3_TurnPipeValve) return;

        _valveHoldTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(_valveHoldTimer / valveHoldRequired);

        // Update notch floor (100% -> 66% -> 33% -> 0%)
        if (progress >= 0.666f)
            _notchFloorTimer = valveHoldRequired * 0.666f;
        else if (progress >= 0.333f)
            _notchFloorTimer = valveHoldRequired * 0.333f;

        // At 50% progress (3.0s), trigger claustrophobic Panic Attack
        if (progress >= 0.5f && !_stage3PanicTriggered)
        {
            _stage3PanicTriggered = true;
            panicSystem?.TriggerPanicNow();
        }

        int pressurePct = Mathf.RoundToInt((1f - progress) * 100f);
        int notch = progress < 0.333f ? 1 : (progress < 0.666f ? 2 : 3);

        if (valveProgressSlider != null) valveProgressSlider.value = progress;
        if (valveProgressText   != null)
            valveProgressText.text = $"RELEASING PRESSURE... {pressurePct}%  [NOTCH {notch}/3]";

        // Rotate valve wheel visually
        if (pressureValve != null)
        {
            var wheel = FindChild(pressureValve.transform, "Valve_Wheel");
            if (wheel != null) wheel.Rotate(Vector3.forward, 90f * Time.deltaTime);
        }

        if (_valveHoldTimer >= valveHoldRequired)
        {
            _valveHolding = false;
            if (valveHoldUI != null) valveHoldUI.SetActive(false);
            CompleteValveStage();
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
