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
    [SerializeField] float panicIntervalMin = 45f;
    [SerializeField] float panicIntervalMax = 50f;
    [SerializeField] float breathFillSpeed = 0.6f;

    Vignette _vignette;
    ChromaticAberration _chroma;
    bool _inPanic;
    float _panicTimer;
    float _swayTimer;

    void Start()
    {
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
        _inPanic = true;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.StartHeartbeat();
        if (breathSlider != null) { breathSlider.value = 0f; breathSlider.gameObject.SetActive(true); }

        if (DialogueManager.Instance != null)
            DialogueManager.Instance.SpeakMurad("Look at me, breathe with me: 1... 2... 3...");

        yield return null;
    }

    void HandlePanicUpdate()
    {
        float t = breathSlider != null ? breathSlider.value : 0f;

        // Ramp up effects
        SetVignette(Mathf.Lerp(0f, 0.65f, 1f - t));
        SetChroma(Mathf.Lerp(0f, 0.8f, 1f - t));

        // Camera sway
        _swayTimer += Time.deltaTime * 3f;
        if (cameraTransform != null)
        {
            float sway = Mathf.Sin(_swayTimer) * 1.5f * (1f - t);
            cameraTransform.localEulerAngles = new Vector3(
                cameraTransform.localEulerAngles.x,
                cameraTransform.localEulerAngles.y,
                sway);
        }

        // Fill breath on Space hold
        if (Input.GetKey(KeyCode.Space) && breathSlider != null)
            breathSlider.value = Mathf.Clamp01(breathSlider.value + breathFillSpeed * Time.deltaTime);

        if (breathSlider != null && breathSlider.value >= 1f)
            ClearPanic();
    }

    void ClearPanic()
    {
        _inPanic = false;
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
