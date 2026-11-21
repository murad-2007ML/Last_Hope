using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PuzzleManager3D : MonoBehaviour
{
    public static PuzzleManager3D Instance { get; private set; }

    // 0=pre-start, 1=find98.4, 2=wires, 3=valve, 4=code, 5=done
    public int PuzzleStage { get; private set; } = 0;

    public bool AnyPanelOpen =>
        (fusePanelUI    != null && fusePanelUI.activeSelf)    ||
        (keypadPanelUI  != null && keypadPanelUI.activeSelf)  ||
        (clueInspectPanel != null && clueInspectPanel.activeSelf);

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
    [SerializeField] float       valveHoldRequired = 3f;
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

    // Runtime state
    int  _wireCutStep;
    bool _valveHolding;
    float _valveHoldTimer;
    bool _valveDone;
    string _keypadInput = "";
    const string CODE = "0715";

    Material _matRed, _matGreen;

    EndingSequence3D _ending;

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
        BuildLockLightMaterials();
        if (valveAudioSource != null)
        {
            valveAudioSource.clip         = ProceduralAudio.GenerateClangClip();
            valveAudioSource.loop         = true;
            valveAudioSource.spatialBlend = 1f;
            valveAudioSource.minDistance  = 0.5f;
            valveAudioSource.maxDistance  = 8f;
            valveAudioSource.playOnAwake  = false;
        }
    }

    // ── Opening sequence (called by Flashlight3D on first toggle) ────────────

    public void OnFirstFlashlightOn()
    {
        StartCoroutine(OpeningSequence());
    }

    IEnumerator OpeningSequence()
    {
        yield return new WaitForSeconds(1.2f);
        SetObjective("Listen to Murad...");

        bool done = false;
        DialogueManager.Instance?.SpeakMurad(
            "You're finally awake... My leg is pinned under this concrete, I can't move. Check the radio on the table!",
            null, () => done = true);
        while (!done) yield return null;

        yield return new WaitForSeconds(0.8f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.9f);
        yield return new WaitForSeconds(0.7f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.25f);

        done = false;
        DialogueManager.Instance?.SpeakRescuer(
            "Hello?! Can anyone hear me? Signal is dying... Check the Sector B note on the far desk and tune the radio to 98.4 FM!",
            null, () => done = true);
        while (!done) yield return null;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(1f);
        yield return new WaitForSeconds(0.5f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);

        PuzzleStage = 1;
        SetObjective("Find the note on the desk — tune radio to 98.4 FM");
    }

    // ── Stage transitions ─────────────────────────────────────────────────────

    public void OnRadioStage2Ready()
    {
        if (PuzzleStage >= 2) return;
        PuzzleStage = 2;
        SetObjective("Open the fuse box — cut YELLOW wire first, then BLUE");
    }

    void CompleteWireStage()
    {
        PuzzleStage = 3;
        UnlockLight(lockLight1, 1);
        CloseFuseBox();
        if (valveAudioSource != null) valveAudioSource.Play();
        radioSystem?.OnWiresComplete();
        SetObjective("Find the clanging pipe and turn its valve");
    }

    void CompleteValveStage()
    {
        _valveDone  = true;
        PuzzleStage = 4;
        UnlockLight(lockLight2, 2);
        if (valveAudioSource != null) valveAudioSource.Stop();
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlaySteamRelease();
        radioSystem?.OnValveComplete();
        panicSystem?.TriggerPanicNow();
        SetObjective("Find the sun board and the floating photo — enter the code on the keypad");
    }

    // ── Lock lights ───────────────────────────────────────────────────────────

    void BuildLockLightMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        _matRed = new Material(shader);
        _matRed.SetColor("_BaseColor",     new Color(0.4f, 0.02f, 0.02f));
        _matRed.SetColor("_EmissionColor", new Color(2f, 0.05f, 0.05f));
        _matRed.EnableKeyword("_EMISSION");

        _matGreen = new Material(shader);
        _matGreen.SetColor("_BaseColor",     new Color(0.02f, 0.4f, 0.05f));
        _matGreen.SetColor("_EmissionColor", new Color(0.05f, 2f, 0.1f));
        _matGreen.EnableKeyword("_EMISSION");

        SetLockLight(lockLight1, false);
        SetLockLight(lockLight2, false);
        SetLockLight(lockLight3, false);
    }

    void SetLockLight(GameObject go, bool green)
    {
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.material = green ? _matGreen : _matRed;
    }

    void UnlockLight(GameObject go, int index)
    {
        SetLockLight(go, true);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayLatchUnlock();
        DialogueManager.Instance?.SetSubtitle($"LOCK {index} RELEASED");
    }

    // ── Clue inspection ───────────────────────────────────────────────────────

    public void InspectChalkBoard()
    {
        ShowClue("Scratched onto the board in chalk:\n\n[ 07 ]\n\nA child's handwriting. Yours.\n\"The sun has two rays.\"");
    }

    public void InspectClock()
    {
        ShowClue("The clock stopped.\n\n03 : 15\n\n\"Before 04:00 — cut YELLOW, then BLUE. Never Red.\"");
    }

    public void InspectPhoto()
    {
        ShowClue("[You flip the photo over]\n\nFaded ink on the back:\n\n\" — 15 — \"\n\n\"Combine with the sun board.\"");
    }

    public void InspectClue(int i)
    {
        string[] notes = {
            "Sector B Emergency Protocol\n\nBACKUP FREQUENCY:  98.4 FM\n\n\"If primary signal lost, retune immediately.\"",
            "Torn newspaper: \"LOCAL BOY RESCUED FROM COLLAPSED BUILDING\"\nDate: July 2015",
            "Child's drawing — a sun, numbers along each ray:\n\n  0  —  7  —  1  —  5\n\n(rays point clockwise)"
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
        if (PuzzleStage < 2)
        {
            DialogueManager.Instance?.SetSubtitle("The fuse box is sealed. I need to find out what to do first.");
            return;
        }
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
        if (PuzzleStage != 2) return;
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
                SetStatus("WRONG ORDER! Pumps accelerate. Wires reset.");
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

    // ── Pressure Valve ────────────────────────────────────────────────────────

    public void BeginValveHold()
    {
        if (PuzzleStage != 3 || _valveDone) return;
        _valveHolding = true;
        if (valveHoldUI != null) valveHoldUI.SetActive(true);
    }

    public void EndValveHold()
    {
        if (_valveDone) return;
        _valveHolding   = false;
        _valveHoldTimer = 0f;
        if (valveProgressSlider != null) valveProgressSlider.value = 0f;
        if (valveProgressText   != null) valveProgressText.text    = "HOLD E — TURN VALVE";
        if (valveHoldUI         != null) valveHoldUI.SetActive(false);
    }

    // ── Keypad ────────────────────────────────────────────────────────────────

    public void OpenKeypad()
    {
        if (PuzzleStage < 4)
        {
            DialogueManager.Instance?.SetSubtitle("The keypad shows no power. I need to unlock it first.");
            return;
        }
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
        if (_keypadInput == CODE)
        {
            PuzzleStage = 5;
            UnlockLight(lockLight3, 3);
            if (keypadPanelUI != null) keypadPanelUI.SetActive(false);
            if (FirstPersonController.Instance != null) FirstPersonController.Instance.MouseLocked = true;
            _ending?.Trigger();
        }
        else
        {
            SetStatus("ACCESS DENIED");
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayKeypadBeep();
            _keypadInput = "";
            RefreshKeypadDisplay();
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
        if (!_valveHolding || _valveDone) return;

        _valveHoldTimer += Time.deltaTime;
        float pct = Mathf.Clamp01(_valveHoldTimer / valveHoldRequired);

        if (valveProgressSlider != null) valveProgressSlider.value = pct;
        if (valveProgressText   != null)
            valveProgressText.text = $"TURNING...  {Mathf.RoundToInt(pct * 100f)}%";

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

    void SetObjective(string text) => DialogueManager.Instance?.SetObjective(text);

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
