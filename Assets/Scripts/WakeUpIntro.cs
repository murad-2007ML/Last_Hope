using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

public class WakeUpIntro : MonoBehaviour
{
    public static WakeUpIntro Instance { get; private set; }
    public static bool IntroPlayed { get; private set; }

    [SerializeField] Canvas targetCanvas;
    [SerializeField] Volume urpVolume;
    [SerializeField] Transform cameraTransform;

    GameObject _eyelidRoot;
    RectTransform _topEyelid;
    RectTransform _bottomEyelid;
    Vignette _vignette;
    float _origVignetteIntensity = 0.25f;
    Vector3 _initialCamLocalRotEuler;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInit()
    {
        if (Instance == null && FindAnyObjectByType<WakeUpIntro>() == null)
        {
            var go = new GameObject("WakeUpIntro_Controller");
            go.AddComponent<WakeUpIntro>();
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(WakeUpSequence());
    }

    IEnumerator WakeUpSequence()
    {
        // 1. Lock player movement & cursor
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = true;
            FirstPersonController.Instance.MouseLocked = true;
        }

        // 2. Resolve references
        if (targetCanvas == null)
            targetCanvas = FindAnyObjectByType<Canvas>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (urpVolume == null)
            urpVolume = FindAnyObjectByType<Volume>();

        if (urpVolume != null && urpVolume.profile != null)
        {
            if (urpVolume.profile.TryGet(out _vignette))
            {
                _origVignetteIntensity = _vignette.intensity.value;
                _vignette.intensity.value = 0.88f; // Groggy dark vignette
                _vignette.smoothness.value = 0.65f;
            }
        }

        // 3. Create Top and Bottom Eyelid UI Panels
        CreateEyelidPanels();
        SetEyelidsOpen(0f); // Fully closed at start (pitch black)

        // Initial groggy camera disorientation (tilted down & sideways)
        Quaternion camBaseRot = Quaternion.identity;
        if (cameraTransform != null)
            camBaseRot = cameraTransform.localRotation;

        float timer = 0f;
        const float totalIntroTime = 3.5f;
        bool muradLineTriggered = false;

        while (timer < totalIntroTime)
        {
            float dt = Time.deltaTime;
            timer += dt;

            // Trigger Murad's line right as eyes begin opening (0.6s)
            if (!muradLineTriggered && timer >= 0.6f)
            {
                muradLineTriggered = true;
                TriggerMuradWakeUpLine();
            }

            // Disable eyelids once fully open (3.2s)
            if (timer >= 3.2f && _eyelidRoot != null && _eyelidRoot.activeSelf)
            {
                _eyelidRoot.SetActive(false);
            }

            // Calculate eyelid open factor based on 2-blink timeline
            float openFactor = CalculateEyelidFactor(timer);
            SetEyelidsOpen(openFactor);

            // Groggy camera pitch & roll sway settling by 3.2s
            if (cameraTransform != null)
            {
                float settleRatio = Mathf.Clamp01(timer / 3.2f);
                float swayRemaining = 1f - Mathf.SmoothStep(0f, 1f, settleRatio);

                float pitchOffset = -8f * swayRemaining + Mathf.Sin(timer * 2.8f) * 1.5f * swayRemaining;
                float rollOffset  = 4f * swayRemaining * Mathf.Cos(timer * 2.1f);
                cameraTransform.localRotation = camBaseRot * Quaternion.Euler(pitchOffset, 0f, rollOffset);
            }

            // URP Vignette recovery
            if (_vignette != null)
            {
                float vigRatio = Mathf.Clamp01(timer / 3.2f);
                _vignette.intensity.value = Mathf.Lerp(0.88f, _origVignetteIntensity, vigRatio);
            }

            yield return null;
        }

        // Camera settles to eye level
        if (cameraTransform != null)
            cameraTransform.localRotation = camBaseRot;

        // Unlock player movement at 3.5s
        if (FirstPersonController.Instance != null)
        {
            FirstPersonController.Instance.MovementLocked = false;
        }

        // Show center prompt "Right-Click to turn on Flashlight"
        ShowFlashlightPrompt();
        IntroPlayed = true;

        if (_eyelidRoot != null)
            Destroy(_eyelidRoot);
    }

    float CalculateEyelidFactor(float t)
    {
        // Blink 1: 0.0s -> 0.8s part halfway, 0.8s -> 1.2s blink shut
        if (t < 0.8f)
        {
            float p = t / 0.8f;
            return Mathf.SmoothStep(0f, 0.5f, p);
        }
        if (t < 1.2f)
        {
            float p = (t - 0.8f) / 0.4f;
            return Mathf.Lerp(0.5f, 0f, p);
        }
        // Blink 2: 1.2s -> 2.0s open wider, 2.0s -> 2.3s quick blink
        if (t < 2.0f)
        {
            float p = (t - 1.2f) / 0.8f;
            return Mathf.SmoothStep(0f, 0.78f, p);
        }
        if (t < 2.3f)
        {
            float p = (t - 2.0f) / 0.3f;
            return Mathf.Lerp(0.78f, 0.05f, p);
        }
        // Final opening: 2.3s -> 3.2s open completely
        if (t < 3.2f)
        {
            float p = (t - 2.3f) / 0.9f;
            return Mathf.SmoothStep(0.05f, 1f, p);
        }
        return 1f;
    }

    void CreateEyelidPanels()
    {
        if (targetCanvas == null) return;

        _eyelidRoot = new GameObject("WakeUpEyelids", typeof(RectTransform));
        _eyelidRoot.transform.SetParent(targetCanvas.transform, false);
        _eyelidRoot.transform.SetAsLastSibling();

        var rootRT = _eyelidRoot.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.sizeDelta = Vector2.zero;

        // Top Eyelid (covers top 50%)
        var topGO = new GameObject("TopEyelid", typeof(RectTransform), typeof(Image));
        topGO.transform.SetParent(_eyelidRoot.transform, false);
        _topEyelid = topGO.GetComponent<RectTransform>();
        _topEyelid.anchorMin = new Vector2(0f, 0.5f);
        _topEyelid.anchorMax = new Vector2(1f, 1f);
        _topEyelid.pivot = new Vector2(0.5f, 1f); // Anchored to top
        _topEyelid.sizeDelta = Vector2.zero;
        _topEyelid.anchoredPosition = Vector2.zero;
        var topImg = topGO.GetComponent<Image>();
        topImg.color = Color.black;
        topImg.raycastTarget = false;

        // Bottom Eyelid (covers bottom 50%)
        var bottomGO = new GameObject("BottomEyelid", typeof(RectTransform), typeof(Image));
        bottomGO.transform.SetParent(_eyelidRoot.transform, false);
        _bottomEyelid = bottomGO.GetComponent<RectTransform>();
        _bottomEyelid.anchorMin = new Vector2(0f, 0f);
        _bottomEyelid.anchorMax = new Vector2(1f, 0.5f);
        _bottomEyelid.pivot = new Vector2(0.5f, 0f); // Anchored to bottom
        _bottomEyelid.sizeDelta = Vector2.zero;
        _bottomEyelid.anchoredPosition = Vector2.zero;
        var bottomImg = bottomGO.GetComponent<Image>();
        bottomImg.color = Color.black;
        bottomImg.raycastTarget = false;
    }

    void SetEyelidsOpen(float openFactor)
    {
        // openFactor = 0 (shut / scale Y = 1), openFactor = 1 (fully open / scale Y = 0)
        float hScale = Mathf.Clamp01(1f - openFactor);
        if (_topEyelid != null)
            _topEyelid.localScale = new Vector3(1f, hScale, 1f);
        if (_bottomEyelid != null)
            _bottomEyelid.localScale = new Vector3(1f, hScale, 1f);
    }

    void TriggerMuradWakeUpLine()
    {
        // 3D spatial audio source behind the player
        Vector3 playerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Vector3 backDir = Camera.main != null ? -Camera.main.transform.forward : Vector3.back;
        Vector3 rightDir = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        Vector3 muradSpatialPos = playerPos + backDir * 2.2f - rightDir * 1.0f + Vector3.up * 0.15f;

        // Position where Murad Character is located if present in scene
        var muradObj = GameObject.Find("Murad_Character");
        if (muradObj != null) muradSpatialPos = muradObj.transform.position;

        var audioClip = Resources.Load<AudioClip>("Audio/Murad/murad_01_wake");
        if (audioClip != null)
        {
            var audioGO = new GameObject("MuradSpatialAudio", typeof(AudioSource));
            audioGO.transform.position = muradSpatialPos;
            var src = audioGO.GetComponent<AudioSource>();
            src.spatialBlend = 1f; // 3D sound behind player
            src.minDistance = 1.2f;
            src.maxDistance = 12f;
            src.volume = 1f;
            src.PlayOneShot(audioClip);
            Destroy(audioGO, audioClip.length + 1f);
        }

        // Trigger synced subtitle in DialogueManager
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.SpeakMurad(
                "You're finally awake... My leg is pinned under this concrete, I can't move. Check the radio on the table!",
                null); // Clip already playing spatially
        }
    }

    void ShowFlashlightPrompt()
    {
        if (Flashlight3D.Instance != null)
        {
            Flashlight3D.Instance.ShowPrompt(true);
            return;
        }

        // Fallback: search all transforms (active or inactive) without creating duplicate GameObject
        var allT = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in allT)
        {
            if (t != null && t.name == "FlashlightPrompt" && t.gameObject.scene.isLoaded)
            {
                var txt = t.GetComponentInChildren<TextMeshProUGUI>(true);
                if (txt != null) txt.text = "Right-Click to turn on Flashlight";
                t.gameObject.SetActive(true);
                return;
            }
        }
    }
}
