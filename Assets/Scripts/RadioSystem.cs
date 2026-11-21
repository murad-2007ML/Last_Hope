using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class RadioSystem : MonoBehaviour
{
    [SerializeField] float minFreq  = 88.0f;
    [SerializeField] float maxFreq  = 108.0f;
    [SerializeField] float scrollStep = 0.1f;
    [SerializeField] Text      frequencyDisplay;
    [SerializeField] Transform radioDial;
    [SerializeField] AudioSource staticSource;
    [SerializeField] GameObject  radioPanel;

    const float FREQ_BROKEN  = 104.5f;
    const float FREQ_BACKUP  = 98.4f;
    const float TUNE_THRESH  = 0.25f;
    const float LOCK_TIME_REQUIRED = 4.0f;

    float _freq = 92.0f;
    bool  _panelOpen;
    bool  _brokenPlayed;   // 104.5 bonus hint (not required)
    bool  _backupPlayed;   // 98.4 — advances to stage 2
    float _lockTimer;
    bool  _lockChimePlayed;

    // Rescuer voice acting audio clips (ElevenLabs)
    AudioClip _clipBroken, _clipBackup, _clipValveGuide, _clipCodeGuide;

    // Dynamic UI References
    Slider _freqSlider;
    Slider _lockSlider;
    Text   _lockStatusText;
    Text   _instructionsText;
    bool   _updatingSliderInternal;

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

    Button _btnClose;
    Button _btnMinus;
    Button _btnPlus;

    void Start()
    {
        // Load voice acting audio clips to preserve character dialogues
        _clipBroken     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");
        _clipBackup     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        _clipValveGuide = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        _clipCodeGuide  = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");
        Debug.Log($"[RadioSystem] Rescuer clips — broken:{_clipBroken != null} backup:{_clipBackup != null} valve:{_clipValveGuide != null} code:{_clipCodeGuide != null}");

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

        // Pause FirstPersonController movement and look, unlock cursor
        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_freqSlider != null)
        {
            _updatingSliderInternal = true;
            _freqSlider.value = _freq;
            _updatingSliderInternal = false;
        }

        UpdateDisplay();
        UpdateDial();
    }

    public void ClosePanel()
    {
        _panelOpen = false;
        if (radioPanel != null) radioPanel.SetActive(false);

        // Resume FirstPersonController movement and look, lock cursor
        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!_panelOpen) return;

        HandleInputTuning();
        HandleLockOnMechanic();

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.C))
            ClosePanel();

        // Direct screen-point click fallback so UI buttons work 100% reliably
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mPos = Input.mousePosition;
            if (_btnClose != null && _btnClose.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_btnClose.GetComponent<RectTransform>(), mPos, null))
            {
                ClosePanel();
            }
            else if (_btnMinus != null && _btnMinus.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_btnMinus.GetComponent<RectTransform>(), mPos, null))
            {
                float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.5f : 0.1f;
                _freq = Mathf.Clamp(_freq - step, minFreq, maxFreq);
                OnFrequencyChangedByUser();
            }
            else if (_btnPlus != null && _btnPlus.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_btnPlus.GetComponent<RectTransform>(), mPos, null))
            {
                float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.5f : 0.1f;
                _freq = Mathf.Clamp(_freq + step, minFreq, maxFreq);
                OnFrequencyChangedByUser();
            }
        }

        UpdateStatic();
        CheckFreq();
    }

    void HandleInputTuning()
    {
        float prevFreq = _freq;
        bool isFineTune = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // 1. Mouse Scroll Wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            float step = isFineTune ? 0.05f : 0.1f;
            _freq += Mathf.Sign(scroll) * step;
        }

        // 2. Keyboard A / D or LeftArrow / RightArrow (2.5 FM/sec continuous, 0.8 FM/sec fine)
        float keyRate = isFineTune ? 0.8f : 2.5f;
        float keyInput = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  keyInput -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keyInput += 1f;

        if (keyInput != 0f)
        {
            _freq += keyInput * keyRate * Time.deltaTime;
        }

        _freq = Mathf.Clamp(_freq, minFreq, maxFreq);

        if (Mathf.Abs(_freq - prevFreq) > 0.001f)
        {
            OnFrequencyChangedByUser();
        }
    }

    void OnFrequencyChangedByUser()
    {
        UpdateDial();
        UpdateDisplay();
        SyncSliderFromFreq();

        if (ProceduralAudio.Instance != null)
            ProceduralAudio.Instance.PlayDialClick();
    }

    void HandleLockOnMechanic()
    {
        // Only active during Stage 1 backup signal search
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
            // Inside lock-on zone!
            if (_lockTimer == 0f && !_lockChimePlayed)
            {
                _lockChimePlayed = true;
                if (ProceduralAudio.Instance != null)
                    ProceduralAudio.Instance.PlaySignalLockChime();
            }

            // Active atmospheric interference drift trying to pull away
            float drift = Mathf.Sin(Time.time * 3.5f) * 0.32f * Time.deltaTime;
            _freq = Mathf.Clamp(_freq + drift, minFreq, maxFreq);
            UpdateDial();
            UpdateDisplay();
            SyncSliderFromFreq();

            _lockTimer += Time.deltaTime;
            float pct = Mathf.Clamp01(_lockTimer / LOCK_TIME_REQUIRED);

            if (_lockStatusText != null)
                _lockStatusText.text = $"<color=#FFE066>SIGNAL LOCKING...</color>  {Mathf.RoundToInt(pct * 100f)}%  (COUNTER DRIFT!)";
            if (_lockSlider != null)
                _lockSlider.value = pct;

            if (_lockTimer >= LOCK_TIME_REQUIRED)
            {
                // Full Lock Achieved!
                _backupPlayed = true;
                _freq = FREQ_BACKUP;
                UpdateDial();
                UpdateDisplay();
                SyncSliderFromFreq();

                if (_lockStatusText != null)
                    _lockStatusText.text = "<color=#4CFF88>SIGNAL LOCKED: 98.4 FM — BACKUP FREQUENCY ACQUIRED</color>";
                if (_lockSlider != null)
                    _lockSlider.value = 1f;

                if (ProceduralAudio.Instance != null)
                    ProceduralAudio.Instance.PlaySignalLockedConfirmation();

                // Immediately unlock Stage 2 so Fuse Box is ready without delay
                PuzzleManager3D.Instance?.OnRadioStage2Ready();

                StartCoroutine(PlayBackup());
            }
        }
        else
        {
            // Decays if player drifts out of range
            if (_lockTimer > 0f)
            {
                _lockTimer = Mathf.Max(0f, _lockTimer - Time.deltaTime * 0.8f);
                float pct = Mathf.Clamp01(_lockTimer / LOCK_TIME_REQUIRED);
                if (_lockStatusText != null)
                    _lockStatusText.text = "<color=#FF8866>SIGNAL DRIFTED OUT! Realign to 98.4 FM</color>";
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

    void SyncSliderFromFreq()
    {
        if (_freqSlider != null && !_updatingSliderInternal)
        {
            _updatingSliderInternal = true;
            _freqSlider.value = _freq;
            _updatingSliderInternal = false;
        }
    }

    void CheckFreq()
    {
        // 104.5 — bonus broken-signal hint (plays once during stage 1+)
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

    // ── Dialogue routines (preserving ElevenLabs character audio) ────────────

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
            "The magnetic lock is frozen. Check the wall clock — if it stopped before 04:00, open the fuse box and cut the YELLOW wire first, then the BLUE wire. Do NOT touch the red wire!",
            _clipBackup, () => done = true);
        while (!done) yield return null;
        PuzzleManager3D.Instance?.OnRadioStage2Ready();
    }

    IEnumerator PlayValveGuide()
    {
        yield return new WaitForSeconds(1.2f);
        DialogueManager.Instance?.SpeakRescuer(
            "Second lock is jammed by water pressure. I'm banging a wrench on the main pipe from outside — listen closely, find which pipe the sound comes from, and turn its valve!",
            _clipValveGuide);
    }

    IEnumerator PlayCodeGuide()
    {
        yield return new WaitForSeconds(1.0f);
        DialogueManager.Instance?.SpeakRescuer(
            "Last lock! Combine the two digits under the sun drawing on the wall with the two digits on the back of the photo floating in the water, and enter the 4-digit code!",
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

    // ── Dynamic UI Builder ───────────────────────────────────────────────────

    void BuildRadioUIIfNeeded()
    {
        if (radioPanel == null) return;

        Font font = frequencyDisplay != null ? frequencyDisplay.font : null;
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Transform existingControls = radioPanel.transform.Find("DynamicRadioControls");
        if (existingControls != null)
        {
            // Already built, link fields
            _freqSlider = existingControls.Find("FreqSlider")?.GetComponent<Slider>();
            _lockSlider = existingControls.Find("LockBar")?.GetComponent<Slider>();
            _lockStatusText = existingControls.Find("LockStatusText")?.GetComponent<Text>();
            return;
        }

        var root = new GameObject("DynamicRadioControls", typeof(RectTransform));
        root.transform.SetParent(radioPanel.transform, false);
        var rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // 1. Interactive Frequency Slider
        var sliderGO = new GameObject("FreqSlider", typeof(RectTransform), typeof(Slider));
        sliderGO.transform.SetParent(root.transform, false);
        var sRT = sliderGO.GetComponent<RectTransform>();
        sRT.anchorMin = new Vector2(0.5f, 0.35f);
        sRT.anchorMax = new Vector2(0.5f, 0.35f);
        sRT.sizeDelta = new Vector2(400f, 24f);

        // Slider Background
        var bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        bgGO.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 0.85f);
        bgGO.GetComponent<Image>().sprite = WhiteSprite;

        // Fill Area
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        var faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = Vector2.zero; faRT.anchorMax = Vector2.one;
        faRT.offsetMin = new Vector2(5f, 0f); faRT.offsetMax = new Vector2(-5f, 0f);

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(fillArea.transform, false);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
        var fillImg = fillGO.GetComponent<Image>();
        fillImg.color = new Color(0.9f, 0.7f, 0.2f, 0.8f);
        fillImg.sprite = WhiteSprite;

        // Handle Area
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGO.transform, false);
        var haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
        haRT.offsetMin = new Vector2(10f, 0f); haRT.offsetMax = new Vector2(-10f, 0f);

        var handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGO.transform.SetParent(handleArea.transform, false);
        var hRT = handleGO.GetComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(20f, 32f);
        var hImg = handleGO.GetComponent<Image>();
        hImg.color = new Color(1f, 0.85f, 0.4f, 1f);
        hImg.sprite = WhiteSprite;

        _freqSlider = sliderGO.GetComponent<Slider>();
        _freqSlider.fillRect = fillRT;
        _freqSlider.handleRect = hRT;
        _freqSlider.targetGraphic = hImg;
        _freqSlider.direction = Slider.Direction.LeftToRight;
        _freqSlider.minValue = minFreq;
        _freqSlider.maxValue = maxFreq;
        _freqSlider.value = _freq;

        _freqSlider.onValueChanged.AddListener(val =>
        {
            if (_updatingSliderInternal) return;
            _freq = Mathf.Round(val * 10f) / 10f;
            UpdateDial();
            UpdateDisplay();
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayDialClick();
        });

        // 2. Step Buttons: [-] and [+]
        _btnMinus = CreateButton(root.transform, "BtnMinus", "◄ -0.1 FM", new Vector2(-260f, -65f), new Vector2(100f, 32f), font, () =>
        {
            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.5f : 0.1f;
            _freq = Mathf.Clamp(_freq - step, minFreq, maxFreq);
            OnFrequencyChangedByUser();
        });

        _btnPlus = CreateButton(root.transform, "BtnPlus", "+0.1 FM ►", new Vector2(260f, -65f), new Vector2(100f, 32f), font, () =>
        {
            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.5f : 0.1f;
            _freq = Mathf.Clamp(_freq + step, minFreq, maxFreq);
            OnFrequencyChangedByUser();
        });

        // 3. Close Button
        _btnClose = CreateButton(root.transform, "BtnClose", "[ X ] CLOSE (ESC)", new Vector2(0f, -180f), new Vector2(200f, 36f), font, ClosePanel);

        // 4. Lock Status Text
        var lockTxtGO = new GameObject("LockStatusText", typeof(RectTransform), typeof(Text));
        lockTxtGO.transform.SetParent(root.transform, false);
        var ltRT = lockTxtGO.GetComponent<RectTransform>();
        ltRT.anchorMin = new Vector2(0.5f, 0.25f);
        ltRT.anchorMax = new Vector2(0.5f, 0.25f);
        ltRT.sizeDelta = new Vector2(600f, 32f);
        _lockStatusText = lockTxtGO.GetComponent<Text>();
        _lockStatusText.font = font;
        _lockStatusText.fontSize = 18;
        _lockStatusText.alignment = TextAnchor.MiddleCenter;
        _lockStatusText.color = Color.white;
        _lockStatusText.text = "";

        // 5. Lock Progress Slider
        var lockBarGO = new GameObject("LockBar", typeof(RectTransform), typeof(Slider));
        lockBarGO.transform.SetParent(root.transform, false);
        var lbRT = lockBarGO.GetComponent<RectTransform>();
        lbRT.anchorMin = new Vector2(0.5f, 0.20f);
        lbRT.anchorMax = new Vector2(0.5f, 0.20f);
        lbRT.sizeDelta = new Vector2(300f, 14f);

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

        // 6. Tuning Controls Instructions
        var instrGO = new GameObject("InstructionsText", typeof(RectTransform), typeof(Text));
        instrGO.transform.SetParent(root.transform, false);
        var inRT = instrGO.GetComponent<RectTransform>();
        inRT.anchorMin = new Vector2(0.5f, 0.12f);
        inRT.anchorMax = new Vector2(0.5f, 0.12f);
        inRT.sizeDelta = new Vector2(700f, 28f);
        _instructionsText = instrGO.GetComponent<Text>();
        _instructionsText.font = font;
        _instructionsText.fontSize = 14;
        _instructionsText.alignment = TextAnchor.MiddleCenter;
        _instructionsText.color = new Color(0.75f, 0.75f, 0.8f, 0.8f);
        _instructionsText.text = "A / D or ◄ / ► to Tune  (Hold SHIFT: Fine Tune)  |  Mouse Wheel  |  Drag Slider";
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
        colors.normalColor = new Color(0.2f, 0.22f, 0.28f, 0.9f);
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f, 1f);
        colors.pressedColor = new Color(0.15f, 0.18f, 0.22f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(action);

        var txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(btnGO.transform, false);
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var txt = txtGO.GetComponent<Text>();
        txt.font = font;
        txt.fontSize = 14;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        txt.text = label;
        txt.raycastTarget = false; // Never block click on parent Button

        return btn;
    }
}
