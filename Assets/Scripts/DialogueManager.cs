using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum Speaker
{
    Murad,
    Rescuer
}

[System.Serializable]
public class SyncedDialogueEntry
{
    public string id;
    public Speaker speaker;
    public AudioClip audioClip;
    [TextArea(2, 5)]
    public string exactTranscriptText;

    public SyncedDialogueEntry() { }

    public SyncedDialogueEntry(string id, Speaker speaker, AudioClip audioClip, string exactTranscriptText)
    {
        this.id = id;
        this.speaker = speaker;
        this.audioClip = audioClip;
        this.exactTranscriptText = exactTranscriptText;
    }
}

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [SerializeField] GameObject subtitlePanel;
    [SerializeField] TextMeshProUGUI subtitleText;
    [SerializeField] TextMeshProUGUI objectiveText;
    [SerializeField] Button choiceButtonA;
    [SerializeField] Button choiceButtonB;
    [SerializeField] TextMeshProUGUI choiceTextA;
    [SerializeField] TextMeshProUGUI choiceTextB;
    [SerializeField] GameObject waveformBar;
    [SerializeField] AudioSource voiceSource;

    [Header("Voice Audio Clips")]
    [SerializeField] AudioClip intro_Murad;
    [SerializeField] AudioClip stage1_Rescuer;
    [SerializeField] AudioClip stage2_Rescuer;
    [SerializeField] AudioClip stage3_Rescuer;
    [SerializeField] AudioClip stage4_Rescuer;
    [SerializeField] AudioClip panic_Murad;
    [SerializeField] AudioClip ending_Rescuer;

    [Header("Additional Voice Clips")]
    [SerializeField] AudioClip rescuer_02_broken;
    [SerializeField] AudioClip murad_02_water;
    [SerializeField] AudioClip murad_03_valve;
    [SerializeField] AudioClip murad_04_end;

    [Header("Synced Dialogue Database")]
    [SerializeField] List<SyncedDialogueEntry> dialogueEntries = new List<SyncedDialogueEntry>();

    bool _rescuerSpeaking;
    Coroutine _typeCoroutine;

    public bool IsSpeaking => _typeCoroutine != null;
    public bool RescuerSpeaking => _rescuerSpeaking;
    public List<SyncedDialogueEntry> DialogueEntries => dialogueEntries;

    public AudioClip IntroMuradClip       => intro_Murad;
    public AudioClip Stage1RescuerClip    => stage1_Rescuer;
    public AudioClip Stage2RescuerClip    => stage2_Rescuer;
    public AudioClip Stage3RescuerClip    => stage3_Rescuer;
    public AudioClip Stage4RescuerClip    => stage4_Rescuer;
    public AudioClip PanicMuradClip       => panic_Murad;
    public AudioClip EndingRescuerClip    => ending_Rescuer;

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
        }

        // Auto-upgrade any legacy UI Text components to TextMeshProUGUI if missing
        AutoUpgradeUI();

        // Populate database entries with Resource audio clips if not already configured in Inspector
        EnsureDefaultDialogueEntries();

        HideChoices();
        SetSubtitleRaw("");
        if (subtitlePanel != null) subtitlePanel.SetActive(false);
    }

    void AutoUpgradeUI()
    {
        if (subtitleText == null && subtitlePanel != null)
        {
            subtitleText = subtitlePanel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (subtitleText == null)
            {
                var legacy = subtitlePanel.GetComponentInChildren<Text>(true);
                if (legacy != null) subtitleText = UpgradeTextToTMP(legacy);
            }
        }
        else if (subtitleText == null)
        {
            var go = GameObject.Find("SubtitleText");
            if (go != null)
            {
                subtitleText = go.GetComponent<TextMeshProUGUI>();
                if (subtitleText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) subtitleText = UpgradeTextToTMP(legacy);
                }
            }
        }

        if (objectiveText == null)
        {
            var go = GameObject.Find("ObjectiveText");
            if (go != null)
            {
                objectiveText = go.GetComponent<TextMeshProUGUI>();
                if (objectiveText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) objectiveText = UpgradeTextToTMP(legacy);
                }
            }
        }

        if (choiceTextA == null && choiceButtonA != null)
        {
            choiceTextA = choiceButtonA.GetComponentInChildren<TextMeshProUGUI>(true);
            if (choiceTextA == null)
            {
                var legacy = choiceButtonA.GetComponentInChildren<Text>(true);
                if (legacy != null) choiceTextA = UpgradeTextToTMP(legacy);
            }
        }

        if (choiceTextB == null && choiceButtonB != null)
        {
            choiceTextB = choiceButtonB.GetComponentInChildren<TextMeshProUGUI>(true);
            if (choiceTextB == null)
            {
                var legacy = choiceButtonB.GetComponentInChildren<Text>(true);
                if (legacy != null) choiceTextB = UpgradeTextToTMP(legacy);
            }
        }
    }

    public static TextMeshProUGUI UpgradeTextToTMP(Text legacy)
    {
        if (legacy == null) return null;
        GameObject go = legacy.gameObject;
        string t = legacy.text;
        Color c = legacy.color;
        int sz = legacy.fontSize;
        TextAnchor align = legacy.alignment;
        Destroy(legacy);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = t;
        tmp.color = c;
        tmp.fontSize = sz > 0 ? sz : 20;
        tmp.alignment = MapAlignment(align);
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        return tmp;
    }

    static TextAlignmentOptions MapAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Midline;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Center;
        }
    }

    public void EnsureDefaultDialogueEntries()
    {
        if (intro_Murad == null)        intro_Murad        = Resources.Load<AudioClip>("Audio/Murad/murad_01_wake");
        if (stage1_Rescuer == null)     stage1_Rescuer     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_01_intro");
        if (stage2_Rescuer == null)     stage2_Rescuer     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        if (stage3_Rescuer == null)     stage3_Rescuer     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        if (stage4_Rescuer == null)     stage4_Rescuer     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");
        if (ending_Rescuer == null)     ending_Rescuer     = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_06_ending");
        if (rescuer_02_broken == null)  rescuer_02_broken  = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");
        if (murad_02_water == null)     murad_02_water     = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
        if (murad_03_valve == null)     murad_03_valve     = Resources.Load<AudioClip>("Audio/Murad/murad_03_valve");
        if (murad_04_end == null)       murad_04_end       = Resources.Load<AudioClip>("Audio/Murad/murad_04_end");

        AddOrUpdateEntry("Intro_Murad", Speaker.Murad, intro_Murad,
            "You're finally awake... My leg is pinned under this concrete, I can't move. Check the radio on the table!");

        AddOrUpdateEntry("Stage1_Rescuer", Speaker.Rescuer, stage1_Rescuer,
            "Hello?! Can anyone hear me? Signal is dying... Check Note #1 and Note #2, apply the offset, and tune the radio!");

        AddOrUpdateEntry("Stage2_Rescuer", Speaker.Rescuer, stage2_Rescuer,
            "Good, you found the backup frequency! Now listen: check the fuse box on the wall. Cut the wires in sequence — check the wall clock and Note #3!");

        AddOrUpdateEntry("Stage3_Rescuer", Speaker.Rescuer, stage3_Rescuer,
            "Power is rerouted to the hydraulic lines! Now you need to equalize pipe pressure with the valves. Listen to the acoustic resonance — release when the gauge is in the green!");

        AddOrUpdateEntry("Stage4_Rescuer", Speaker.Rescuer, stage4_Rescuer,
            "Pressure equalized! The keypad on the blast door is active. Enter the clearance code from the clipboard log: zero seven one five!");

        AddOrUpdateEntry("Panic_Murad", Speaker.Murad, panic_Murad,
            "Look at me, breathe with me: 1... 2... 3... (Hold [SPACE])");

        AddOrUpdateEntry("Ending_Rescuer", Speaker.Rescuer, ending_Rescuer,
            "Hold on, I've got you! You're safe now... breathe.");

        // Preserved optional/dialogue lines
        AddOrUpdateEntry("murad_02_water", Speaker.Murad, murad_02_water,
            "The water's rising fast! It's already up to our knees! Hurry, please!");

        AddOrUpdateEntry("murad_03_valve", Speaker.Murad, murad_03_valve,
            "Listen to the pipes... The steam hissing changes when the valve aligns. Don't let it over-pressurize!");

        AddOrUpdateEntry("murad_04_end", Speaker.Murad, murad_04_end,
            "You did it... The emergency seals are releasing! Get the door open, hurry!");

        AddOrUpdateEntry("rescuer_02_broken", Speaker.Rescuer, rescuer_02_broken,
            "Signal is breaking up... I'm sending the backup frequency protocol. Listen carefully!");

        AddOrUpdateEntry("rescuer_06_ending", Speaker.Rescuer, ending_Rescuer,
            "Hold on, I've got you! You're safe now... breathe.");
    }

    void AddOrUpdateEntry(string id, Speaker speaker, AudioClip clip, string text)
    {
        var existing = dialogueEntries.Find(e => string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            dialogueEntries.Add(new SyncedDialogueEntry(id, speaker, clip, text));
        }
        else
        {
            if (existing.audioClip == null && clip != null)
                existing.audioClip = clip;
            if (string.IsNullOrEmpty(existing.exactTranscriptText))
                existing.exactTranscriptText = text;
        }
    }

    public SyncedDialogueEntry GetEntry(string id)
    {
        return dialogueEntries.Find(e => string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void PlayEntry(string id, Action onDone = null)
    {
        var entry = GetEntry(id);
        if (entry != null)
        {
            _rescuerSpeaking = entry.speaker == Speaker.Rescuer;
            string prefix = entry.speaker == Speaker.Rescuer ? "<color=#4CFF88>[RESCUER]:</color>  " : "<color=#88CCFF>[MURAD]:</color>  ";
            RunTypewriter(prefix + entry.exactTranscriptText, entry.audioClip, onDone, entry.speaker == Speaker.Rescuer);
        }
        else
        {
            Debug.LogWarning($"[DialogueManager] Dialogue entry '{id}' not found!");
            onDone?.Invoke();
        }
    }

    public void SpeakRescuer(string text, AudioClip clip = null, Action onDone = null)
    {
        _rescuerSpeaking = true;
        if (clip == null)
            clip = FindClipForText(text, Speaker.Rescuer);
        string labeled = $"<color=#4CFF88>[RESCUER]:</color>  {text}";
        RunTypewriter(labeled, clip, onDone, isRescuer: true);
    }

    public void SpeakMurad(string text, AudioClip clip = null, Action onDone = null)
    {
        _rescuerSpeaking = false;
        if (clip == null)
            clip = FindClipForText(text, Speaker.Murad);
        string labeled = $"<color=#88CCFF>[MURAD]:</color>  {text}";
        RunTypewriter(labeled, clip, onDone, isRescuer: false);
    }

    AudioClip FindClipForText(string text, Speaker speaker)
    {
        foreach (var entry in dialogueEntries)
        {
            if (entry.speaker == speaker && entry.audioClip != null)
            {
                if (text.IndexOf(entry.exactTranscriptText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.exactTranscriptText.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return entry.audioClip;
                }
            }
        }
        return null;
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

    void RunTypewriter(string fullRichText, AudioClip clip, Action onDone, bool isRescuer)
    {
        if (_typeCoroutine != null) StopCoroutine(_typeCoroutine);
        _typeCoroutine = StartCoroutine(TypeLineTMP(fullRichText, clip, onDone, isRescuer));
    }

    IEnumerator TypeLineTMP(string fullRichText, AudioClip clip, Action onDone, bool isRescuer)
    {
        if (subtitlePanel != null)
        {
            var img = subtitlePanel.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            subtitlePanel.SetActive(true);
        }

        if (subtitleText == null)
        {
            AutoUpgradeUI();
            if (subtitleText == null)
            {
                if (subtitlePanel != null) subtitlePanel.SetActive(false);
                _typeCoroutine = null;
                onDone?.Invoke();
                yield break;
            }
        }

        // Assign full rich-text markup so formatting tags are intact
        subtitleText.text = fullRichText;
        subtitleText.ForceMeshUpdate();
        int totalChars = subtitleText.textInfo.characterCount;
        subtitleText.maxVisibleCharacters = 0;

        if (clip != null && voiceSource != null)
        {
            voiceSource.PlayOneShot(clip);
        }

        // Reveal speed across audioClip.length * 0.9f
        float revealDuration = (clip != null && clip.length > 0.1f)
            ? (clip.length * 0.9f)
            : Mathf.Max(0.5f, totalChars * 0.035f);

        float charDelay = totalChars > 0 ? (revealDuration / totalChars) : 0.035f;
        int blipEvery = isRescuer ? 4 : 6;

        for (int i = 1; i <= totalChars; i++)
        {
            subtitleText.maxVisibleCharacters = i;
            if (i % blipEvery == 0 && ProceduralAudio.Instance != null)
            {
                if (isRescuer) ProceduralAudio.Instance.PlayRadioBlip();
                else           ProceduralAudio.Instance.PlayWhisperTone();
            }
            yield return new WaitForSeconds(charDelay);
        }

        // Subtitle display duration strictly Mathf.Max(audioClip.length + 1.0f, 4.5f)
        float targetTotalDuration = Mathf.Max((clip != null ? clip.length : 0f) + 1.0f, 4.5f);
        float holdRemaining = targetTotalDuration - revealDuration;
        if (holdRemaining > 0f)
            yield return new WaitForSeconds(holdRemaining);

        SetSubtitleRaw("");
        if (subtitlePanel != null) subtitlePanel.SetActive(false);
        _typeCoroutine = null;
        onDone?.Invoke();
    }

    void SetSubtitleRaw(string text)
    {
        bool hasText = !string.IsNullOrEmpty(text);
        if (subtitlePanel != null)
            subtitlePanel.SetActive(hasText);

        if (subtitleText != null)
        {
            subtitleText.text = text ?? "";
            subtitleText.maxVisibleCharacters = (text ?? "").Length;
        }
    }

    void Wire(Button btn, TextMeshProUGUI label, string text, Action action)
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
        if (_rescuerSpeaking && subtitleText != null && subtitleText.maxVisibleCharacters > 0 && !string.IsNullOrEmpty(subtitleText.text))
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
