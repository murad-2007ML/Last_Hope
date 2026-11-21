using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum Speaker
{
    Murad,
    Rescuer,
    Player
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
    [SerializeField] AudioClip stage4_DoorOverride;
    [SerializeField] AudioClip ending_Player;
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

    public AudioClip IntroMuradClip         => intro_Murad;
    public AudioClip Stage1RescuerClip      => stage1_Rescuer;
    public AudioClip Stage2RescuerClip      => stage2_Rescuer;
    public AudioClip Stage3RescuerClip      => stage3_Rescuer;
    public AudioClip Stage4RescuerClip      => stage4_Rescuer;
    public AudioClip PanicMuradClip         => panic_Murad;
    public AudioClip EndingRescuerClip      => ending_Rescuer;
    public AudioClip Stage4DoorOverrideClip => stage4_DoorOverride;
    public AudioClip EndingPlayerClip       => ending_Player;

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
        var existing = go.GetComponent<TextMeshProUGUI>();
        if (existing != null) return existing;
        string t = legacy.text;
        Color c = legacy.color;
        int sz = legacy.fontSize;
        TextAnchor align = legacy.alignment;
        DestroyImmediate(legacy);

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
        if (intro_Murad == null)          intro_Murad          = Resources.Load<AudioClip>("Audio/Murad/murad_01_wake");
        if (panic_Murad == null)          panic_Murad          = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
        if (stage1_Rescuer == null)       stage1_Rescuer       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_01_intro");
        if (stage4_DoorOverride == null)  stage4_DoorOverride  = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");
        if (stage2_Rescuer == null)       stage2_Rescuer       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_03_wires");
        if (stage3_Rescuer == null)       stage3_Rescuer       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_04_valve");
        if (stage4_Rescuer == null)       stage4_Rescuer       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_05_code");
        if (ending_Rescuer == null)       ending_Rescuer       = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_06_ending");
        if (ending_Player == null)        ending_Player        = Resources.Load<AudioClip>("Audio/Murad/murad_03_valve");
        if (murad_02_water == null)       murad_02_water       = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
        if (murad_03_valve == null)       murad_03_valve       = Resources.Load<AudioClip>("Audio/Murad/murad_03_valve");
        if (rescuer_02_broken == null)    rescuer_02_broken    = Resources.Load<AudioClip>("Audio/Rescuer/rescuer_02_broken");

        AddOrUpdateEntry("Intro_Murad", Speaker.Murad, intro_Murad,
            "You're awake... My leg is trapped under this concrete. Turn on your flashlight—check the radio!");

        AddOrUpdateEntry("Stage1_Rescuer", Speaker.Rescuer, stage1_Rescuer,
            "Hello?! Can you hear me? Signal's dying! Combine the two maintenance notes for the backup frequency and lock the dial!");

        AddOrUpdateEntry("Stage2_Rescuer", Speaker.Rescuer, stage2_Rescuer,
            "I hear you! Magnetic lock's jammed. Check the wall clock and circuit note—cut the fuse wires in exact order!");

        AddOrUpdateEntry("Stage3_Rescuer", Speaker.Rescuer, stage3_Rescuer,
            "First lock's open! Water pressure's jamming the second. Listen for my wrench knocking—find that valve and bleed pressure three times!");

        AddOrUpdateEntry("Stage4_Rescuer", Speaker.Rescuer, stage4_Rescuer,
            "One lock left! Get the four-digit code from the chalk sun and the floating photo. Hurry, water's rising!");

        AddOrUpdateEntry("Panic_Murad", Speaker.Murad, panic_Murad,
            "Look at me! Don't panic. Hold Space and breathe with me... in... and out...");

        AddOrUpdateEntry("Stage4_DoorOverride", Speaker.Rescuer, stage4_DoorOverride,
            "Code accepted! Hydraulic override active—fifteen seconds till the door opens!");

        AddOrUpdateEntry("Ending_Player", Speaker.Player, ending_Player,
            "Wait! What about Murad?! Get him out too!");

        AddOrUpdateEntry("Ending_Rescuer", Speaker.Rescuer, ending_Rescuer,
            "Calm down, you're safe. Look at the corner... Nobody else was in there.");
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
            if (clip != null) existing.audioClip = clip;
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
            string prefix = entry.speaker switch
            {
                Speaker.Rescuer => "<color=#4CFF88>[RESCUER]:</color>  ",
                Speaker.Player  => "<color=#FFE066>[YOU]:</color>  ",
                _               => "<color=#88CCFF>[MURAD]:</color>  "
            };
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

    public void SpeakPlayer(string text, AudioClip clip = null, Action onDone = null)
    {
        _rescuerSpeaking = false;
        if (clip == null)
            clip = FindClipForText(text, Speaker.Player);
        string labeled = $"<color=#FFE066>[YOU]:</color>  {text}";
        RunTypewriter(labeled, clip, onDone, isRescuer: false);
    }

    AudioClip FindClipForText(string text, Speaker speaker)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var entry in dialogueEntries)
        {
            if (entry.speaker == speaker && entry.audioClip != null &&
                !string.IsNullOrEmpty(entry.exactTranscriptText))
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

    // Called by DoorVoidEnding: stops any active voice line and clears the subtitle panel.
    public void StopAndHide()
    {
        if (_typeCoroutine != null) { StopCoroutine(_typeCoroutine); _typeCoroutine = null; }
        _rescuerSpeaking = false;
        if (voiceSource != null) voiceSource.Stop();
        SetSubtitleRaw("");
    }

    // Hides the persistent HUD elements (objective bar, waveform) but not the subtitle panel
    // (that is already cleared by StopAndHide).
    public void SetHUDVisible(bool visible)
    {
        if (objectiveText != null) objectiveText.gameObject.SetActive(visible);
        if (waveformBar   != null) waveformBar.SetActive(visible);
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
