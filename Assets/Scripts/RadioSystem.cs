using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class RadioSystem : MonoBehaviour
{
    [SerializeField] float minFreq  = 88.0f;
    [SerializeField] float maxFreq  = 108.0f;
    [SerializeField] Text      frequencyDisplay;
    [SerializeField] Transform radioDial;
    [SerializeField] AudioSource staticSource;
    [SerializeField] GameObject  radioPanel;

    [Header("Manual UI Buttons (Optional)")]
    [SerializeField] Button closeButton;

    const float FREQ_BROKEN  = 104.5f;
    const float FREQ_BACKUP  = 98.4f;
    const float TUNE_THRESH  = 0.25f;
    const float LOCK_TIME_REQUIRED = 4.0f;
    const float TUNE_ANIM_DURATION = 1.5f;

    float _freq = 92.0f;
    bool  _panelOpen;
    bool  _brokenPlayed;
    bool  _backupPlayed;
    float _lockTimer;
    bool  _lockChimePlayed;
    bool  _isTuning;

    AudioClip _clipBroken, _clipBackup, _clipValveGuide, _clipCodeGuide;

    InputField _freqInput;
    Button     _btnTune;
    Button     _btnClose;
    Slider     _lockSlider;
    Text       _lockStatusText;

    public bool PanelOpen => _panelOpen;
    public float CurrentFrequency => _freq;

    static Sprite _whiteSprite;
    static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite == null)
            {
                var tex = Texture2D.whiteTexture;
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return _whiteSprite;
        }
    }

    void Start()
    {
        // rescuer_02_broken repurposed as Stage4_DoorOverride — no audio at 104.5 broken signal
        _clipBroken     = null;
        _clipBackup     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        _clipValveGuide = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        _clipCodeGuide  = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");
        Debug.Log($"[RadioSystem] Rescuer clips — backup:{_clipBackup != null} valve:{_clipValveGuide != null} code:{_clipCodeGuide != null}");

        BuildRadioUIIfNeeded();
        UpdateDisplay();
        UpdateDial();
    }

    public void OpenPanel()
    {
        // Stage Transition Guarantee: If still in Stage 0, transition to Stage 1 now so radio interaction is guaranteed
        if (PuzzleManager3D.Instance != null &&
            PuzzleManager3D.Instance.CurrentStage == PuzzleStage.Stage0_Locked)
        {
            PuzzleManager3D.Instance.OnFirstFlashlightOn();
        }

        _panelOpen = true;
        if (radioPanel != null) radioPanel.SetActive(true);
        EnsureRadioBackdrop();
        WireManualButtons();

        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateDisplay();
        UpdateDial();

        // Delay one frame — EventSystem needs the panel to be active before ActivateInputField works
        StartCoroutine(FocusInputNextFrame());
    }

    IEnumerator FocusInputNextFrame()
    {
        yield return null;
        if (_freqInput != null)
        {
            _freqInput.text = "";
            _freqInput.Select();
            _freqInput.ActivateInputField();
        }
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

        if (!_isTuning)
        {
            HandleLockOnMechanic();
            UpdateStatic();
            CheckFreq();

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                SubmitFrequencyInput();
        }

        bool inputFocused = _freqInput != null && _freqInput.isFocused;

        if (Input.GetKeyDown(KeyCode.Escape))
            ClosePanel();

        if (!inputFocused && !Input.GetKeyDown(KeyCode.Escape))
        {
            if (!_isTuning && _freqInput != null)
            {
                _freqInput.Select();
                _freqInput.ActivateInputField();
            }
        }
    }

    // ── Input-based tuning ────────────────────────────────────────────────────

    void SubmitFrequencyInput()
    {
        if (_isTuning) return;
        if (_freqInput == null) return;

        string raw = _freqInput.text.Trim();
        if (!float.TryParse(raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float target))
        {
            if (_lockStatusText != null)
                _lockStatusText.text = "<color=#FF4444>Invalid — enter a number like 98.4</color>";
            return;
        }

        target = Mathf.Round(Mathf.Clamp(target, minFreq, maxFreq) * 10f) / 10f;
        StartCoroutine(AnimateTuneTo(target));
    }

    IEnumerator AnimateTuneTo(float target)
    {
        _isTuning = true;
        if (_btnTune   != null) _btnTune.interactable   = false;
        if (_freqInput != null) _freqInput.interactable = false;
        if (_lockStatusText != null) _lockStatusText.text = "";

        float startFreq = _freq;
        float elapsed   = 0f;
        bool  midClick  = false;

        // Ramp up static during sweep
        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.SetStaticAmount(0.9f);

        while (elapsed < TUNE_ANIM_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / TUNE_ANIM_DURATION));
            _freq = Mathf.Lerp(startFreq, target, t);
            UpdateDial();
            UpdateDisplay();

            // Single click near midpoint for tactile feel
            if (!midClick && elapsed >= TUNE_ANIM_DURATION * 0.45f)
            {
                midClick = true;
                if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayDialClick();
            }

            yield return null;
        }

        _freq = target;
        UpdateDial();
        UpdateDisplay();

        if (ProceduralAudio.Instance != null)
        {
            ProceduralAudio.Instance.SetStaticAmount(0f);
            ProceduralAudio.Instance.PlayDialClick();
        }

        _isTuning = false;
        if (_btnTune   != null) _btnTune.interactable   = true;
        if (_freqInput != null) _freqInput.interactable = true;

        // Evaluate the landed frequency immediately
        UpdateStatic();
        CheckFreq();

        // Re-focus so player can type the next frequency right away
        StartCoroutine(FocusInputNextFrame());
    }

    // ── Lock-on mechanic (Stage 1 only) ──────────────────────────────────────

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
        if (diff <= TUNE_THRESH)
        {
            if (_lockTimer == 0f && !_lockChimePlayed)
            {
                _lockChimePlayed = true;
                if (ProceduralAudio.Instance != null)
                    ProceduralAudio.Instance.PlaySignalLockChime();
            }

            // Atmospheric drift — re-enter 98.4 if it escapes the threshold
            float drift = Mathf.Sin(Time.time * 3.5f) * 0.32f * Time.deltaTime;
            _freq = Mathf.Clamp(_freq + drift, minFreq, maxFreq);
            UpdateDial();
            UpdateDisplay();

            _lockTimer += Time.deltaTime;
            float pct = Mathf.Clamp01(_lockTimer / LOCK_TIME_REQUIRED);

            if (_lockStatusText != null)
                _lockStatusText.text = $"<color=#FFE066>SIGNAL LOCKING...</color>  {Mathf.RoundToInt(pct * 100f)}%  (DRIFT! Re-enter 98.4 if lost)";
            if (_lockSlider != null)
                _lockSlider.value = pct;

            if (_lockTimer >= LOCK_TIME_REQUIRED)
            {
                _backupPlayed = true;
                _freq = FREQ_BACKUP;
                UpdateDial();
                UpdateDisplay();

                if (_lockStatusText != null)
                    _lockStatusText.text = "<color=#4CFF88>SIGNAL LOCKED: 98.4 FM — BACKUP FREQUENCY ACQUIRED</color>";
                if (_lockSlider != null)
                    _lockSlider.value = 1f;

                if (ProceduralAudio.Instance != null)
                    ProceduralAudio.Instance.PlaySignalLockedConfirmation();

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
                    _lockStatusText.text = "<color=#FF8866>SIGNAL DRIFTED OUT! Re-enter 98.4 FM</color>";
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
            && Mathf.Abs(_freq - FREQ_BROKEN) < TUNE_THRESH)
        {
            _brokenPlayed = true;
            StartCoroutine(PlayBroken());
        }
    }

    // Called by PuzzleManager after wires done
    public void OnWiresComplete()  => StartCoroutine(PlayValveGuide());

    // Called by PuzzleManager after valve done
    public void OnValveComplete()  => StartCoroutine(PlayCodeGuide());

    // ── Dialogue routines ─────────────────────────────────────────────────────

    IEnumerator PlayBroken()
    {
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.7f);
        yield return new WaitForSeconds(0.5f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0.2f);
        DialogueManager.Instance?.SpeakRescuer(
            "Signal is dying... Can you hear me? Check the Sector B emergency note and tune to 98.4 FM!",
            _clipBroken);
    }

    IEnumerator PlayBackup()
    {
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(0f);
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    void UpdateStatic()
    {
        float d1 = Mathf.Abs(_freq - FREQ_BROKEN);
        float d2 = Mathf.Abs(_freq - FREQ_BACKUP);
        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.SetStaticAmount(Mathf.Clamp01(Mathf.Min(d1, d2) / 2f));
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

    void WireManualButtons()
    {
        if (closeButton != null)
        {
            _btnClose = closeButton;
            _btnClose.onClick.RemoveAllListeners();
            _btnClose.onClick.AddListener(ClosePanel);
        }
    }

    // ── Dynamic UI Builder ────────────────────────────────────────────────────

    void EnsureRadioBackdrop()
    {
        if (radioPanel == null) return;
        var existing = radioPanel.transform.Find("ClickBackdrop");
        if (existing != null)
        {
            var existBtn = existing.GetComponent<Button>();
            if (existBtn != null) { existBtn.onClick.RemoveAllListeners(); existBtn.onClick.AddListener(ClosePanel); }
            existing.SetAsFirstSibling();
            return;
        }

        var go = new GameObject("ClickBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(radioPanel.transform, false);
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
        btn.onClick.AddListener(ClosePanel);
    }

    void BuildRadioUIIfNeeded()
    {
        if (radioPanel == null) return;

        Font font = frequencyDisplay != null ? frequencyDisplay.font : null;
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        EnsureRadioBackdrop();

        Transform existingControls = radioPanel.transform.Find("DynamicRadioControls");
        if (existingControls != null)
        {
            // If the old slider-based UI is present (no FreqInput), destroy and rebuild
            if (existingControls.Find("FreqInput") == null)
            {
                Destroy(existingControls.gameObject);
            }
            else
            {
                _freqInput      = existingControls.Find("FreqInput").GetComponent<InputField>();
                _btnTune        = existingControls.Find("BtnTune")?.GetComponent<Button>();
                _lockSlider     = existingControls.Find("LockBar")?.GetComponent<Slider>();
                _lockStatusText = existingControls.Find("LockStatusText")?.GetComponent<Text>();
                return;
            }
        }

        var root = new GameObject("DynamicRadioControls", typeof(RectTransform));
        root.transform.SetParent(radioPanel.transform, false);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // 1. Prompt label
        var labelGO = new GameObject("FreqLabel", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(root.transform, false);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0.5f, 0.5f);
        labelRT.anchorMax = new Vector2(0.5f, 0.5f);
        labelRT.anchoredPosition = new Vector2(0f, 140f);
        labelRT.sizeDelta = new Vector2(600f, 40f);
        var labelTxt = labelGO.GetComponent<Text>();
        labelTxt.font = font;
        labelTxt.fontSize = 24;
        labelTxt.alignment = TextAnchor.MiddleCenter;
        labelTxt.color = new Color(0.75f, 0.75f, 0.8f, 0.9f);
        labelTxt.text = "ENTER FREQUENCY  (88.0 - 108.0 FM)";

        // 2. InputField
        var inputGO = new GameObject("FreqInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGO.transform.SetParent(root.transform, false);
        var inputRT = inputGO.GetComponent<RectTransform>();
        inputRT.anchorMin = new Vector2(0.5f, 0.5f);
        inputRT.anchorMax = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = new Vector2(-85f, 70f);
        inputRT.sizeDelta = new Vector2(270f, 64f);
        var inputImg = inputGO.GetComponent<Image>();
        inputImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        inputImg.sprite = WhiteSprite;

        var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(inputGO.transform, false);
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(12f, 2f); phRT.offsetMax = new Vector2(-12f, -2f);
        var phTxt = phGO.GetComponent<Text>();
        phTxt.font = font;
        phTxt.fontSize = 24;
        phTxt.fontStyle = FontStyle.Italic;
        phTxt.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
        phTxt.text = "e.g. 95.8";
        phTxt.alignment = TextAnchor.MiddleCenter;

        var itGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        itGO.transform.SetParent(inputGO.transform, false);
        var itRT = itGO.GetComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero; itRT.anchorMax = Vector2.one;
        itRT.offsetMin = new Vector2(12f, 2f); itRT.offsetMax = new Vector2(-12f, -2f);
        var itTxt = itGO.GetComponent<Text>();
        itTxt.font = font;
        itTxt.fontSize = 28;
        itTxt.color = new Color(1f, 0.9f, 0.4f, 1f);
        itTxt.alignment = TextAnchor.MiddleCenter;
        itTxt.supportRichText = false;

        _freqInput = inputGO.GetComponent<InputField>();
        _freqInput.textComponent = itTxt;
        _freqInput.placeholder = phTxt;
        _freqInput.contentType = InputField.ContentType.DecimalNumber;
        _freqInput.characterLimit = 5;
        _freqInput.onSubmit.AddListener(_ => SubmitFrequencyInput());

        // 3. TUNE button (right of input field)
        _btnTune = CreateButton(root.transform, "BtnTune", "TUNE", new Vector2(140f, 70f), new Vector2(140f, 64f), font, SubmitFrequencyInput);

        // 4. Lock Status Text
        var lockTxtGO = new GameObject("LockStatusText", typeof(RectTransform), typeof(Text));
        lockTxtGO.transform.SetParent(root.transform, false);
        var ltRT = lockTxtGO.GetComponent<RectTransform>();
        ltRT.anchorMin = new Vector2(0.5f, 0.5f);
        ltRT.anchorMax = new Vector2(0.5f, 0.5f);
        ltRT.anchoredPosition = new Vector2(0f, -55f);
        ltRT.sizeDelta = new Vector2(680f, 40f);
        _lockStatusText = lockTxtGO.GetComponent<Text>();
        _lockStatusText.font = font;
        _lockStatusText.fontSize = 20;
        _lockStatusText.alignment = TextAnchor.MiddleCenter;
        _lockStatusText.color = Color.white;
        _lockStatusText.text = "";

        // 5. Lock Progress Bar
        var lockBarGO = new GameObject("LockBar", typeof(RectTransform), typeof(Slider));
        lockBarGO.transform.SetParent(root.transform, false);
        var lbRT = lockBarGO.GetComponent<RectTransform>();
        lbRT.anchorMin = new Vector2(0.5f, 0.5f);
        lbRT.anchorMax = new Vector2(0.5f, 0.5f);
        lbRT.anchoredPosition = new Vector2(0f, -105f);
        lbRT.sizeDelta = new Vector2(420f, 18f);

        var lbBg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        lbBg.transform.SetParent(lockBarGO.transform, false);
        var lbgRT = lbBg.GetComponent<RectTransform>();
        lbgRT.anchorMin = Vector2.zero; lbgRT.anchorMax = Vector2.one;
        lbgRT.offsetMin = Vector2.zero; lbgRT.offsetMax = Vector2.zero;
        lbBg.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        lbBg.GetComponent<Image>().sprite = WhiteSprite;

        var lbFillArea = new GameObject("FillArea", typeof(RectTransform));
        lbFillArea.transform.SetParent(lockBarGO.transform, false);
        var lbfaRT = lbFillArea.GetComponent<RectTransform>();
        lbfaRT.anchorMin = Vector2.zero; lbfaRT.anchorMax = Vector2.one;
        lbfaRT.offsetMin = Vector2.zero; lbfaRT.offsetMax = Vector2.zero;

        var lbFill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        lbFill.transform.SetParent(lbFillArea.transform, false);
        var lbfRT = lbFill.GetComponent<RectTransform>();
        lbfRT.anchorMin = Vector2.zero; lbfRT.anchorMax = Vector2.one;
        lbfRT.offsetMin = Vector2.zero; lbfRT.offsetMax = Vector2.zero;
        var lbImg = lbFill.GetComponent<Image>();
        lbImg.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
        lbImg.sprite = WhiteSprite;

        _lockSlider = lockBarGO.GetComponent<Slider>();
        _lockSlider.fillRect = lbfRT;
        _lockSlider.minValue = 0f;
        _lockSlider.maxValue = 1f;
        _lockSlider.value = 0f;
        _lockSlider.interactable = false;

        // 6. Close Button
        _btnClose = CreateButton(root.transform, "BtnClose", "CLOSE (ESC)",
            new Vector2(0f, -148f), new Vector2(240f, 48f), font, ClosePanel);

        // 7. Instructions hint
        var instrGO = new GameObject("InstructionsText", typeof(RectTransform), typeof(Text));
        instrGO.transform.SetParent(root.transform, false);
        var inRT = instrGO.GetComponent<RectTransform>();
        inRT.anchorMin = new Vector2(0.5f, 0.5f);
        inRT.anchorMax = new Vector2(0.5f, 0.5f);
        inRT.anchoredPosition = new Vector2(0f, -210f);
        inRT.sizeDelta = new Vector2(600f, 28f);
        var instrTxt = instrGO.GetComponent<Text>();
        instrTxt.font = font;
        instrTxt.fontSize = 16;
        instrTxt.alignment = TextAnchor.MiddleCenter;
        instrTxt.color = new Color(0.6f, 0.6f, 0.65f, 0.8f);
        instrTxt.text = "Type a frequency then press TUNE or ENTER";
    }

    Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, Font font, UnityEngine.Events.UnityAction action)
    {
        var btnGO = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = btnGO.GetComponent<Image>();
        img.color = new Color(0.2f, 0.22f, 0.28f, 0.9f);
        img.sprite = WhiteSprite;
        img.raycastTarget = true;

        var btn = btnGO.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = new Color(0.2f, 0.22f, 0.28f, 0.9f);
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f, 1f);
        colors.pressedColor     = new Color(0.15f, 0.18f, 0.22f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(action);

        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(btnGO.transform, false);
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var txt = txtGO.GetComponent<Text>();
        txt.font = font;
        txt.fontSize = 20;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        txt.text = label;
        txt.raycastTarget = false;

        return btn;
    }
}
