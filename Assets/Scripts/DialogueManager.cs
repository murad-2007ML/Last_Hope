using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [SerializeField] GameObject subtitlePanel;
    [SerializeField] Text       subtitleText;
    [SerializeField] Text       objectiveText;
    [SerializeField] Button     choiceButtonA;
    [SerializeField] Button     choiceButtonB;
    [SerializeField] Text       choiceTextA;
    [SerializeField] Text       choiceTextB;
    [SerializeField] GameObject waveformBar;
    [SerializeField] AudioSource voiceSource;

    bool _rescuerSpeaking;
    Coroutine _typeCoroutine;

    public bool RescuerSpeaking => _rescuerSpeaking;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Self-heal: create AudioSource if none was wired
        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.spatialBlend = 0f;
            voiceSource.volume = 1f;
            voiceSource.playOnAwake = false;
            Debug.LogWarning("[DialogueManager] voiceSource was null — created one automatically.");
        }

        HideChoices();
        SetSubtitleRaw("");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SpeakRescuer(string text, AudioClip clip = null, Action onDone = null)
    {
        _rescuerSpeaking = true;
        string labeled = $"<color=#4CFF88>[RESCUER]:</color>  {text}";
        Run(labeled, clip, onDone, isRescuer: true);
    }

    public void SpeakMurad(string text, AudioClip clip = null, Action onDone = null)
    {
        _rescuerSpeaking = false;
        string labeled = $"<color=#88CCFF>[MURAD]:</color>  {text}";
        Run(labeled, clip, onDone, isRescuer: false);
    }

    public void SetSubtitle(string text) => SetSubtitleRaw(text);

    public void SetObjective(string text)
    {
        if (objectiveText != null)
            objectiveText.text = string.IsNullOrEmpty(text) ? "" : $"► {text}";
    }

    public void ShowChoices(string a, string b, Action onA, Action onB)
    {
        Wire(choiceButtonA, choiceTextA, a, () => { HideChoices(); onA?.Invoke(); });
        Wire(choiceButtonB, choiceTextB, b, () => { HideChoices(); onB?.Invoke(); });
        if (choiceButtonA) choiceButtonA.gameObject.SetActive(true);
        if (choiceButtonB) choiceButtonB.gameObject.SetActive(true);
    }

    public void HideChoices()
    {
        if (choiceButtonA) choiceButtonA.gameObject.SetActive(false);
        if (choiceButtonB) choiceButtonB.gameObject.SetActive(false);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    void Run(string text, AudioClip clip, Action onDone, bool isRescuer)
    {
        if (_typeCoroutine != null) StopCoroutine(_typeCoroutine);
        _typeCoroutine = StartCoroutine(TypeLine(text, clip, onDone, isRescuer));
    }

    IEnumerator TypeLine(string text, AudioClip clip, Action onDone, bool isRescuer)
    {
        // Always render on top of inspect panels
        if (subtitlePanel != null) subtitlePanel.transform.SetAsLastSibling();
        SetSubtitleRaw("");

        if (clip != null)
        {
            Debug.Log($"[DialogueManager] Playing clip: {clip.name} ({clip.length:F1}s)");
            voiceSource.PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning("[DialogueManager] clip is NULL — no audio will play for this line.");
        }

        int blipEvery = isRescuer ? 4 : 6;
        int i = 0;
        foreach (char c in text)
        {
            if (subtitleText != null) subtitleText.text += c;
            i++;
            if (char.IsLetter(c) && i % blipEvery == 0 && ProceduralAudio.Instance != null)
            {
                if (isRescuer) ProceduralAudio.Instance.PlayRadioBlip();
                else           ProceduralAudio.Instance.PlayWhisperTone();
            }
            yield return new WaitForSeconds(0.025f);
        }

        yield return new WaitForSeconds(4.5f);
        SetSubtitleRaw("");
        onDone?.Invoke();
    }

    void SetSubtitleRaw(string text)
    {
        if (subtitleText != null) subtitleText.text = text;
    }

    void Wire(Button btn, Text label, string text, Action action)
    {
        if (btn == null) return;
        if (label != null) label.text = text;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => action?.Invoke());
    }

    // ── Waveform bar animation ─────────────────────────────────────────────────

    void Update()
    {
        if (waveformBar == null) return;
        if (_rescuerSpeaking && subtitleText != null && subtitleText.text.Length > 0)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
            waveformBar.transform.localScale = new Vector3(1f, Mathf.Lerp(0.3f, 1f, pulse), 1f);
        }
        else
        {
            waveformBar.transform.localScale = Vector3.Lerp(
                waveformBar.transform.localScale, new Vector3(1f, 0.05f, 1f), Time.deltaTime * 8f);
        }
    }
}
