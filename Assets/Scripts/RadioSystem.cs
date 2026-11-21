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

    float _freq = 92.0f;
    bool  _panelOpen;
    bool  _brokenPlayed;
    bool  _backupPlayed;

    AudioClip _clipBroken, _clipBackup, _clipValveGuide, _clipCodeGuide;

    public bool PanelOpen => _panelOpen;

    void Start()
    {
        _clipBroken     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");
        _clipBackup     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        _clipValveGuide = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        _clipCodeGuide  = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");
        Debug.Log($"[RadioSystem] Rescuer clips — broken:{_clipBroken != null} backup:{_clipBackup != null} valve:{_clipValveGuide != null} code:{_clipCodeGuide != null}");
    }

    public void OpenPanel()
    {
        _panelOpen = true;
        if (radioPanel != null) radioPanel.SetActive(true);
        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = false;
    }

    public void ClosePanel()
    {
        _panelOpen = false;
        if (radioPanel != null) radioPanel.SetActive(false);
        if (FirstPersonController.Instance != null)
            FirstPersonController.Instance.MouseLocked = true;
    }

    void Update()
    {
        if (!_panelOpen) return;

        float delta = (Input.GetAxis("Mouse ScrollWheel") * 10f
                     + Input.GetAxis("Horizontal") * Time.deltaTime * 5f) * scrollStep;

        if (Mathf.Abs(delta) > 0.001f)
        {
            _freq = Mathf.Round(Mathf.Clamp(_freq + delta, minFreq, maxFreq) * 10f) / 10f;
            UpdateDial();
            UpdateDisplay();
            if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayDialClick();
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab))
            ClosePanel();

        UpdateStatic();
        CheckFreq();
    }

    void CheckFreq()
    {
        // 104.5 — bonus broken-signal hint (plays once regardless of stage)
        if (!_brokenPlayed && Mathf.Abs(_freq - FREQ_BROKEN) < TUNE_THRESH)
        {
            _brokenPlayed = true;
            StartCoroutine(PlayBroken());
        }

        // 98.4 — requires stage >= 1 (opening dialogue has already played)
        if (!_backupPlayed
            && PuzzleManager3D.Instance != null
            && PuzzleManager3D.Instance.PuzzleStage >= 1
            && Mathf.Abs(_freq - FREQ_BACKUP) < TUNE_THRESH)
        {
            _backupPlayed = true;
            StartCoroutine(PlayBackup());
        }
    }

    // Called by PuzzleManager after wires done
    public void OnWiresComplete()  => StartCoroutine(PlayValveGuide());

    // Called by PuzzleManager after valve done
    public void OnValveComplete()  => StartCoroutine(PlayCodeGuide());

    // ── Dialogue routines ────────────────────────────────────────────────────

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
        yield return new WaitForSeconds(0.4f);
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
}
