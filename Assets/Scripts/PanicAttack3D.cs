using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class PanicAttack3D : MonoBehaviour
{
    [SerializeField] Volume urpVolume;
    [SerializeField] Slider breathSlider;
    [SerializeField] Transform cameraTransform;
    [SerializeField] float panicIntervalMin = 95f;
    [SerializeField] float panicIntervalMax = 95f;
    [SerializeField] float breathFillSpeed = 0.2f; // 5.0s hold to fill

    Vignette _vignette;
    ChromaticAberration _chroma;
    AudioClip _panicClip;
    bool _inPanic;
    float _panicTimer;
    float _swayTimer;
    float _panicDuration;
    bool _isBlackingOut;

    void Start()
    {
        panicIntervalMin = 95f;
        panicIntervalMax = 95f;
        breathFillSpeed = 0.2f;
        _panicClip = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
        ScheduleNextPanic();

        if (urpVolume != null)
        {
            urpVolume.profile.TryGet(out _vignette);
            urpVolume.profile.TryGet(out _chroma);
        }
        if (breathSlider != null) breathSlider.gameObject.SetActive(false);
    }

    void ScheduleNextPanic()
    {
        _panicTimer = Random.Range(panicIntervalMin, panicIntervalMax);
    }

    void Update()
    {
        if (_inPanic)
        {
            HandlePanicUpdate();
            return;
        }

        _panicTimer -= Time.deltaTime;
        if (_panicTimer <= 0f)
            StartCoroutine(TriggerPanic());
    }

    public void TriggerPanicNow() => StartCoroutine(TriggerPanic());

    IEnumerator TriggerPanic()
    {
        if (_inPanic || _isBlackingOut) yield break;

        _inPanic = true;
        _panicDuration = 0f;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.StartHeartbeat();
        if (breathSlider != null) { breathSlider.value = 0f; breathSlider.gameObject.SetActive(true); }

        if (DialogueManager.Instance != null)
            DialogueManager.Instance.SpeakMurad("Look at me! Don't panic. Hold Space and breathe with me... in... and out...", _panicClip);

        yield return null;
    }

    void HandlePanicUpdate()
    {
        if (_isBlackingOut) return;

        _panicDuration += Time.deltaTime;

        // Failing to calm down within 15 seconds causes a brief blackout & water surge penalty
        if (_panicDuration >= 15f)
        {
            StartCoroutine(DoBlackoutPenalty());
            return;
        }

        float t = breathSlider != null ? breathSlider.value : 0f;

        // Ramp up effects
        SetVignette(Mathf.Lerp(0.2f, 0.75f, 1f - t));
        SetChroma(Mathf.Lerp(0.1f, 0.9f, 1f - t));

        // Camera sway
        _swayTimer += Time.deltaTime * 3.5f;
        if (cameraTransform != null)
        {
            float sway = Mathf.Sin(_swayTimer) * 2.0f * (1f - t);
            cameraTransform.localEulerAngles = new Vector3(
                cameraTransform.localEulerAngles.x,
                cameraTransform.localEulerAngles.y,
                sway);
        }

        // Fill breath on Space hold (5.0s total)
        if (Input.GetKey(KeyCode.Space) && breathSlider != null)
            breathSlider.value = Mathf.Clamp01(breathSlider.value + breathFillSpeed * Time.deltaTime);

        if (breathSlider != null && breathSlider.value >= 1f)
            ClearPanic();
    }

    IEnumerator DoBlackoutPenalty()
    {
        _isBlackingOut = true;
        _inPanic = false;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.StopHeartbeat();
        if (breathSlider != null) breathSlider.gameObject.SetActive(false);

        DialogueManager.Instance?.SetSubtitle("<color=#FF4444>[BLACKOUT]: You hyperventilated! Water surged +3%.</color>");
        WaterController3D.Instance?.AddWaterSurge(0.03f);

        // Heavy blackout fade via vignette
        float elapsed = 0f;
        while (elapsed < 1.2f)
        {
            elapsed += Time.deltaTime;
            SetVignette(1f);
            SetChroma(1f);
            yield return null;
        }

        ClearPanic();
        _isBlackingOut = false;
    }

    void ClearPanic()
    {
        _inPanic = false;
        _panicDuration = 0f;

        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.StopHeartbeat();
        if (breathSlider != null) breathSlider.gameObject.SetActive(false);

        SetVignette(0f);
        SetChroma(0f);

        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(cameraTransform.localEulerAngles.x, cameraTransform.localEulerAngles.y, 0f);

        ScheduleNextPanic();
    }

    void SetVignette(float intensity)
    {
        if (_vignette != null)
            _vignette.intensity.Override(intensity);
    }

    void SetChroma(float intensity)
    {
        if (_chroma != null)
            _chroma.intensity.Override(intensity);
    }
}
