using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class RadioSystem : MonoBehaviour
{
    [Header("Frequency & Hardware")]
    [SerializeField] float minFreq = 88.0f;
    [SerializeField] float maxFreq = 108.0f;
    [SerializeField] Text frequencyDisplay;
    [SerializeField] Transform radioDial;
    [SerializeField] AudioSource staticSource;
    [SerializeField] GameObject radioPanel;

    [Header("Tuning Controls")]
    [SerializeField] float tuneSpeed = 3.5f;
    [SerializeField] float fineTuneMultiplier = 0.25f;
    [SerializeField] float stepAmount = 0.1f;
    [SerializeField] float wheelStep = 0.2f;

    [Header("Optional Manual UI Overrides")]
    [SerializeField] Slider manualFreqSlider;
    [SerializeField] Button manualMinusButton;
    [SerializeField] Button manualPlusButton;
    [SerializeField] Button closeButton;
    [SerializeField] Slider manualLockSlider;
    [SerializeField] Text manualLockStatusText;

    const float FREQ_BROKEN = 104.5f;
    const float FREQ_BACKUP = 98.4f;
    const float TUNE_THRESH = 0.25f;
    const float LOCK_TIME_REQUIRED = 4.0f;

    float _freq = 92.0f;
    bool _panelOpen;
    bool _brokenPlayed;
    bool _backupPlayed;
    float _lockTimer;
    bool _lockChimePlayed;
    bool _isUpdatingSlider;
    float _lastStepClickTime;

    AudioClip _clipBroken, _clipBackup, _clipValveGuide, _clipCodeGuide;

    Slider _freqSlider;
    Button _btnMinus;
    Button _btnPlus;
    Button _btnClose;
    Slider _lockSlider;
    Text _lockStatusText;
    Text _instructionsText;

    public bool PanelOpen => _panelOpen;
    public float CurrentFrequency => _freq;

    void Start()
    {
        _clipBroken = null;
        _clipBackup = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        _clipValveGuide = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        _clipCodeGuide = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");

        BindUIReferences();
        SyncSliderRangeAndValue();
        UpdateDisplay();
        UpdateDial();
    }

    public void OpenPanel()
    {
        if (PuzzleManager3D.Instance != null &&
            PuzzleManager3D.Instance.CurrentStage == PuzzleStage.Stage0_Locked)
        {
            PuzzleManager3D.Instance.OnFirstFlashlightOn();
        }

        _panelOpen = true;
        if (radioPanel != null) radioPanel.SetActive(true);

        BindUIReferences();
        EnsureRadioBackdrop();
        SyncSliderRangeAndValue();

        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateDisplay();
        UpdateDial();
        UpdateStatic();
    }

    public void ClosePanel()
    {
        _panelOpen = false;
        if (radioPanel != null) radioPanel.SetActive(false);

        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!_panelOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ClosePanel();
            return;
        }

        HandleTuningInputs();
        HandleLockOnMechanic();
        UpdateStatic();
        CheckFreq();
    }

    // ── Tuning Inputs: A/D, Left/Right, Shift Fine-Tune, Mouse Wheel ────────

    void HandleTuningInputs()
    {
        float inputAxis = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) inputAxis -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) inputAxis += 1f;

        bool fineTune = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speedMultiplier = fineTune ? fineTuneMultiplier : 1f;

        if (Mathf.Abs(inputAxis) > 0.01f)
        {
            float nextFreq = _freq + inputAxis * tuneSpeed * speedMultiplier * Time.deltaTime;
            SetFrequency(nextFreq, playClickOnStep: true);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float step = fineTune ? stepAmount : wheelStep;
            float nextFreq = _freq + Mathf.Sign(scroll) * step;
            SetFrequency(nextFreq, playClickOnStep: true);
        }
    }

    public void StepFrequencyDown() => SetFrequency(_freq - stepAmount, playClickOnStep: true);
    public void StepFrequencyUp()   => SetFrequency(_freq + stepAmount, playClickOnStep: true);

    void OnSliderValueChanged(float value)
    {
        if (_isUpdatingSlider) return;
        SetFrequency(value, playClickOnStep: true);
    }

    void SetFrequency(float rawFreq, bool playClickOnStep)
    {
        float rounded = Mathf.Round(Mathf.Clamp(rawFreq, minFreq, maxFreq) * 10f) / 10f;
        if (Mathf.Approximately(rounded, _freq)) return;

        _freq = rounded;
        SyncSliderValueOnly();
        UpdateDisplay();
        UpdateDial();

        if (playClickOnStep && Time.unscaledTime - _lastStepClickTime >= 0.08f)
        {
            _lastStepClickTime = Time.unscaledTime;
            ProceduralAudio.Instance?.PlayDialClick();
        }
    }

    void SyncSliderRangeAndValue()
    {
        if (_freqSlider == null) return;
        _isUpdatingSlider = true;
        _freqSlider.minValue = minFreq;
        _freqSlider.maxValue = maxFreq;
        _freqSlider.wholeNumbers = false;
        _freqSlider.value = _freq;
        _isUpdatingSlider = false;
    }

    void SyncSliderValueOnly()
    {
        if (_freqSlider == null) return;
        _isUpdatingSlider = true;
        _freqSlider.value = _freq;
        _isUpdatingSlider = false;
    }

    // ── Lock-on mechanic (Stage 1 + Note #1 & Note #2 required) ────────────

    void HandleLockOnMechanic()
    {
        if (_backupPlayed)
        {
            if (_lockStatusText != null)
                _lockStatusText.text = "<color=#4CFF88>SIGNAL LOCKED: 98.4 FM — BACKUP FREQUENCY ACQUIRED</color>";
            if (_lockSlider != null)
                _lockSlider.value = 1f;
            return;
        }

        if (PuzzleManager3D.Instance == null ||
            PuzzleManager3D.Instance.CurrentStage != PuzzleStage.Stage1_TuneBackupFrequency)
        {
            if (_lockStatusText != null) _lockStatusText.text = "";
            if (_lockSlider != null) _lockSlider.value = 0f;
            return;
        }

        float diff = Mathf.Abs(_freq - FREQ_BACKUP);
        bool nearBackup = diff <= TUNE_THRESH;

        // Note #1 və Note #2 oxunmayıbsa 98.4 FM-də heç bir lock-on / proses başlamasın
        if (nearBackup && !PuzzleManager3D.Instance.CanTuneBackupFrequency)
        {
            _lockTimer = 0f;
            _lockChimePlayed = false;
            if (_lockSlider != null) _lockSlider.value = 0f;
            if (_lockStatusText != null)
                _lockStatusText.text = "<color=#FFAA44> Read Note #1 & Note #2 first to decode signal!</color>";
            return;
        }

        if (nearBackup)
        {
            if (_lockTimer == 0f && !_lockChimePlayed)
            {
                _lockChimePlayed = true;
                ProceduralAudio.Instance?.PlaySignalLockChime();
            }

            float drift = Mathf.Sin(Time.time * 3.5f) * 0.25f * Time.deltaTime;
            _freq = Mathf.Clamp(Mathf.Round((_freq + drift) * 10f) / 10f, minFreq, maxFreq);
            SyncSliderValueOnly();
            UpdateDial();
            UpdateDisplay();

            _lockTimer += Time.deltaTime;
            float pct = Mathf.Clamp01(_lockTimer / LOCK_TIME_REQUIRED);

            if (_lockStatusText != null)
                _lockStatusText.text = $"<color=#FFE066>SIGNAL LOCKING...</color>  {Mathf.RoundToInt(pct * 100f)}%  (Keep near 98.4 FM)";
            if (_lockSlider != null)
                _lockSlider.value = pct;

            if (_lockTimer >= LOCK_TIME_REQUIRED)
            {
                _backupPlayed = true;
                _freq = FREQ_BACKUP;
                SyncSliderValueOnly();
                UpdateDial();
                UpdateDisplay();

                if (_lockStatusText != null)
                    _lockStatusText.text = "<color=#4CFF88>SIGNAL LOCKED: 98.4 FM — BACKUP FREQUENCY ACQUIRED</color>";
                if (_lockSlider != null)
                    _lockSlider.value = 1f;

                ProceduralAudio.Instance?.PlaySignalLockedConfirmation();
                PuzzleManager3D.Instance?.OnRadioStage2Ready();
                StartCoroutine(PlayBackup());
            }
        }
        else
        {
            if (_lockTimer > 0f)
            {
                _lockTimer = Mathf.Max(0f, _lockTimer - Time.deltaTime * 0.8f);
                float pct = Mathf.Clamp01(_lockTimer / LOCK_TIME_REQUIRED);
                if (_lockStatusText != null)
                    _lockStatusText.text = "<color=#FF8866>SIGNAL DRIFTED OUT! Return to 98.4 FM</color>";
                if (_lockSlider != null)
                    _lockSlider.value = pct;
            }
            else
            {
                _lockChimePlayed = false;
                if (_lockStatusText != null)
                    _lockStatusText.text = "";
                if (_lockSlider != null)
                    _lockSlider.value = 0f;
            }
        }
    }

    void CheckFreq()
    {
        if (!_brokenPlayed
            && PuzzleManager3D.Instance != null
            && PuzzleManager3D.Instance.CurrentStage >= PuzzleStage.Stage1_TuneBackupFrequency
            && PuzzleManager3D.Instance.CanTuneBackupFrequency
            && Mathf.Abs(_freq - FREQ_BROKEN) < TUNE_THRESH)
        {
            _brokenPlayed = true;
            StartCoroutine(PlayBroken());
        }
    }

    public void OnWiresComplete() => StartCoroutine(PlayValveGuide());
    public void OnValveComplete() => StartCoroutine(PlayCodeGuide());

    // ── Dialogue routines ─────────────────────────────────────────────────────

    IEnumerator PlayBroken()
    {
        ProceduralAudio.Instance?.SetStaticAmount(0.7f);
        yield return new WaitForSeconds(0.5f);
        ProceduralAudio.Instance?.SetStaticAmount(0.2f);
        DialogueManager.Instance?.SpeakRescuer(
            "Signal is dying... Can you hear me? Check the Sector B emergency note and tune to 98.4 FM!",
            _clipBroken);
    }

    IEnumerator PlayBackup()
    {
        ProceduralAudio.Instance?.SetStaticAmount(0f);
        yield return new WaitForSeconds(0.5f);
        bool done = false;
        DialogueManager.Instance?.SpeakRescuer(
            "I hear you! Magnetic lock's jammed. Check the wall clock and circuit note—cut the fuse wires in exact order!",
            _clipBackup, () => done = true);
        while (!done) yield return null;
        PuzzleManager3D.Instance?.OnRadioStage2Ready();
    }

    IEnumerator PlayValveGuide()
    {
        yield return new WaitForSeconds(1.2f);
        DialogueManager.Instance?.SpeakRescuer(
            "First lock's open! Water pressure's jamming the second. Listen for my wrench knocking—find that valve and bleed pressure three times!",
            _clipValveGuide);
    }

    IEnumerator PlayCodeGuide()
    {
        yield return new WaitForSeconds(1.0f);
        DialogueManager.Instance?.SpeakRescuer(
            "One lock left! Get the four-digit code from the chalk sun and the floating photo. Hurry, water's rising!",
            _clipCodeGuide);
    }

    // ── Helpers & UI Binding ──────────────────────────────────────────────────

    void UpdateStatic()
    {
        float d1 = Mathf.Abs(_freq - FREQ_BROKEN);
        float d2 = Mathf.Abs(_freq - FREQ_BACKUP);
        ProceduralAudio.Instance?.SetStaticAmount(Mathf.Clamp01(Mathf.Min(d1, d2) / 2f));
    }

    void UpdateDial()
    {
        if (radioDial == null) return;
        float t = (_freq - minFreq) / (maxFreq - minFreq);
        radioDial.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(-150f, 150f, t));
    }

    void UpdateDisplay()
    {
        if (frequencyDisplay != null)
            frequencyDisplay.text = _freq.ToString("F1") + " FM";
    }

    void BindUIReferences()
    {
        if (radioPanel == null) return;

        Transform controlsRoot = FindChild(radioPanel.transform, "DynamicRadioControls (1)")
                              ?? FindChild(radioPanel.transform, "DynamicRadioControls")
                              ?? radioPanel.transform;

        _freqSlider = manualFreqSlider != null
            ? manualFreqSlider
            : FindChild(controlsRoot, "FreqSlider")?.GetComponent<Slider>();

        _btnMinus = manualMinusButton != null
            ? manualMinusButton
            : FindChild(controlsRoot, "BtnMinus")?.GetComponent<Button>();

        _btnPlus = manualPlusButton != null
            ? manualPlusButton
            : FindChild(controlsRoot, "BtnPlus")?.GetComponent<Button>();

        _btnClose = closeButton != null
            ? closeButton
            : FindChild(controlsRoot, "BtnClose")?.GetComponent<Button>();

        _lockSlider = manualLockSlider != null
            ? manualLockSlider
            : FindChild(controlsRoot, "LockBar")?.GetComponent<Slider>();

        _lockStatusText = manualLockStatusText != null
            ? manualLockStatusText
            : FindChild(controlsRoot, "LockStatusText")?.GetComponent<Text>();

        _instructionsText = FindChild(controlsRoot, "InstructionsText")?.GetComponent<Text>();
        if (_instructionsText != null)
            _instructionsText.text = "A / D or ◄ / ► to Tune (Hold SHIFT: Fine Tune) | Mouse Wheel";

        if (_freqSlider != null)
        {
            _freqSlider.onValueChanged.RemoveListener(OnSliderValueChanged);
            _freqSlider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        if (_btnMinus != null)
        {
            _btnMinus.onClick.RemoveListener(StepFrequencyDown);
            _btnMinus.onClick.AddListener(StepFrequencyDown);
        }

        if (_btnPlus != null)
        {
            _btnPlus.onClick.RemoveListener(StepFrequencyUp);
            _btnPlus.onClick.AddListener(StepFrequencyUp);
        }

        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(ClosePanel);
            _btnClose.onClick.AddListener(ClosePanel);
        }
    }

    void EnsureRadioBackdrop()
    {
        if (radioPanel == null) return;
        var existing = radioPanel.transform.Find("ClickBackdrop");
        if (existing != null)
        {
            var existBtn = existing.GetComponent<Button>();
            if (existBtn != null)
            {
                existBtn.onClick.RemoveAllListeners();
                existBtn.onClick.AddListener(ClosePanel);
            }
            existing.SetAsFirstSibling();
        }
    }

    static Transform FindChild(Transform parent, string targetName)
    {
        if (parent == null) return null;
        if (parent.name == targetName) return parent;
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == targetName) return t;
        }
        return null;
    }
}