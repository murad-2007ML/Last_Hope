using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WaterController3D : MonoBehaviour
{
    public static WaterController3D Instance { get; private set; }

    [SerializeField] Transform waterPlane;
    [SerializeField] float startY = 0.15f;
    [SerializeField] float maxY = 0.85f;
    [SerializeField] float riseSpeed = 0.001166667f; // 0.70m over 600s (10 minutes)
    [SerializeField] Transform radioTransform;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] Button restartButton;
    [SerializeField] FirstPersonController fpsController;

    [Header("Water Level HUD")]
    [SerializeField] Slider waterLevelSlider;
    [SerializeField] Image waterFillImage;
    [SerializeField] TextMeshProUGUI waterLevelText;
    [SerializeField] TextMeshProUGUI timeRemainingText;
    [SerializeField] Image warningFlashImage;

    [Header("Elapsed Timer")]
    [SerializeField] TextMeshProUGUI elapsedTimerText;

    float _speedMultiplier = 1f;
    bool _rising = true;
    bool _gameOver;
    bool _cinematicMode;
    float _flashTimer;
    float _elapsed;
    bool _muradWaterLinePlayed;

    // Colors for fill bar: safe → danger → critical
    static readonly Color ColorSafe     = new Color(0.15f, 0.45f, 0.85f);
    static readonly Color ColorDanger   = new Color(0.9f,  0.6f,  0.1f);
    static readonly Color ColorCritical = new Color(0.9f,  0.1f,  0.1f);

    public bool Rising { get => _rising; set => _rising = value; }

    // Called by DoorVoidEnding to suppress camera shake and game-over during the cinematic.
    public void SetCinematicMode(bool on) => _cinematicMode = on;

    public void SetHUDVisible(bool visible)
    {
        if (waterLevelSlider  != null) waterLevelSlider.gameObject.SetActive(visible);
        if (waterLevelText    != null) waterLevelText.gameObject.SetActive(visible);
        if (timeRemainingText != null) timeRemainingText.gameObject.SetActive(visible);
        if (warningFlashImage != null) warningFlashImage.gameObject.SetActive(visible);
        if (elapsedTimerText  != null) elapsedTimerText.gameObject.SetActive(visible);
    }

    void Awake()
    {
        Instance = this;
        AutoUpgradeUI();
    }

    void AutoUpgradeUI()
    {
        if (waterLevelText == null)
        {
            var go = GameObject.Find("WaterLevelText");
            if (go != null)
            {
                waterLevelText = go.GetComponent<TextMeshProUGUI>();
                if (waterLevelText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) waterLevelText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
        if (timeRemainingText == null)
        {
            var go = GameObject.Find("TimeRemainingText");
            if (go != null)
            {
                timeRemainingText = go.GetComponent<TextMeshProUGUI>();
                if (timeRemainingText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) timeRemainingText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
        if (elapsedTimerText == null)
        {
            var go = GameObject.Find("ElapsedTimerText");
            if (go != null)
            {
                elapsedTimerText = go.GetComponent<TextMeshProUGUI>();
                if (elapsedTimerText == null)
                {
                    var legacy = go.GetComponent<Text>();
                    if (legacy != null) elapsedTimerText = DialogueManager.UpgradeTextToTMP(legacy);
                }
            }
        }
    }

    void Start()
    {
        // Calibrate rise speed to exactly 600 seconds (10 minutes)
        riseSpeed = (maxY - startY) / 600f;

        if (waterPlane != null)
        {
            waterPlane.gameObject.SetActive(true);
            Vector3 p = waterPlane.position;
            p.y = startY;
            waterPlane.position = p;
        }

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartGame);
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
        if (warningFlashImage != null)
            warningFlashImage.color = new Color(1f, 0f, 0f, 0f);

        UpdateHUD(0f);
    }

    void Update()
    {
        if (!_gameOver)
        {
            _elapsed += Time.deltaTime;
            if (elapsedTimerText != null)
            {
                int m = (int)(_elapsed / 60f);
                int s = (int)(_elapsed % 60f);
                int ms = (int)((_elapsed * 100f) % 100f);
                elapsedTimerText.text = $"{m:00}:{s:00}.{ms:00}";
            }
        }

        if (!_rising || _gameOver || waterPlane == null) return;

        Vector3 pos = waterPlane.position;
        pos.y += riseSpeed * _speedMultiplier * Time.deltaTime;
        waterPlane.position = pos;

        if (fpsController != null)
            fpsController.IsWading = pos.y > 0.3f;

        if (pos.y >= maxY)
            TriggerGameOver();

        float fillPct = Mathf.Clamp01((pos.y - startY) / (maxY - startY));
        UpdateHUD(fillPct);
        UpdateWarningFlash(fillPct);

        // Murad reacts when water hits 50%
        if (!_muradWaterLinePlayed && fillPct >= 0.5f)
        {
            _muradWaterLinePlayed = true;
            DialogueManager.Instance?.PlayEntry("murad_02_water");
        }
    }

    public void AddSpeedPercent(float percent)
    {
        _speedMultiplier += percent / 100f;
    }

    public void AddWaterSurge(float percent)
    {
        if (_gameOver || waterPlane == null) return;

        float surgeMeters = (maxY - startY) * percent;
        Vector3 pos = waterPlane.position;
        pos.y = Mathf.Min(maxY, pos.y + surgeMeters);
        waterPlane.position = pos;

        // Play loud rushing water splash sound
        ProceduralAudio.Instance?.PlayWaterSurge();

        // Brief camera shake & screen edge flash
        StartCoroutine(DoCameraShake(0.45f, 0.08f));
        StartCoroutine(DoSurgeFlash());

        float fillPct = Mathf.Clamp01((pos.y - startY) / (maxY - startY));
        UpdateHUD(fillPct);

        if (pos.y >= maxY)
            TriggerGameOver();
    }

    IEnumerator DoCameraShake(float duration, float magnitude)
    {
        if (_cinematicMode) yield break;
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Transform camT = cam.transform;
        Vector3 origPos = camT.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = (Random.value * 2f - 1f) * magnitude;
            float y = (Random.value * 2f - 1f) * magnitude;
            camT.localPosition = origPos + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        camT.localPosition = origPos;
    }

    IEnumerator DoSurgeFlash()
    {
        if (warningFlashImage == null) yield break;

        Color surgeColor = new Color(0.1f, 0.2f, 0.85f, 0.45f);
        float elapsed = 0f;
        float dur = 0.5f;

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dur;
            float a = Mathf.Sin(t * Mathf.PI) * 0.45f;
            warningFlashImage.color = new Color(surgeColor.r, surgeColor.g, surgeColor.b, a);
            yield return null;
        }

        warningFlashImage.color = new Color(1f, 0f, 0f, 0f);
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    void UpdateHUD(float fillPct)
    {
        if (waterLevelSlider != null)
            waterLevelSlider.value = fillPct;

        // Bar color: blue → orange → red
        if (waterFillImage != null)
        {
            Color c = fillPct < 0.5f
                ? Color.Lerp(ColorSafe,   ColorDanger,   fillPct * 2f)
                : Color.Lerp(ColorDanger, ColorCritical, (fillPct - 0.5f) * 2f);
            waterFillImage.color = c;
        }

        // Percentage label
        if (waterLevelText != null)
            waterLevelText.text = $"WATER  {Mathf.RoundToInt(fillPct * 100f)}%";

        // Time remaining estimate (meters left / current rise speed)
        if (timeRemainingText != null)
        {
            if (_rising && waterPlane != null && waterPlane.gameObject.activeSelf)
            {
                float metersLeft = maxY - waterPlane.position.y;
                float secsLeft   = metersLeft / (riseSpeed * _speedMultiplier);
                timeRemainingText.text = secsLeft < 60f
                    ? $"~{Mathf.CeilToInt(secsLeft)}s LEFT"
                    : $"~{Mathf.CeilToInt(secsLeft / 60f)}m LEFT";
                timeRemainingText.color = secsLeft < 20f ? ColorCritical
                                        : secsLeft < 40f ? ColorDanger
                                        : Color.white;
            }
            else
            {
                timeRemainingText.text = "";
            }
        }
    }

    void UpdateWarningFlash(float fillPct)
    {
        if (warningFlashImage == null) return;
        if (fillPct < 0.75f) { warningFlashImage.color = new Color(1f, 0f, 0f, 0f); return; }

        _flashTimer += Time.deltaTime * Mathf.Lerp(1.5f, 4f, (fillPct - 0.75f) / 0.25f);
        float alpha = (Mathf.Sin(_flashTimer) * 0.5f + 0.5f) * Mathf.Lerp(0.05f, 0.22f, (fillPct - 0.75f) / 0.25f);
        warningFlashImage.color = new Color(1f, 0f, 0f, alpha);
    }

    void TriggerGameOver()
    {
        _gameOver = true;
        _rising   = false;
        if (fpsController != null) fpsController.MouseLocked = false;
        if (gameOverPanel  != null) gameOverPanel.SetActive(true);
        if (warningFlashImage != null) warningFlashImage.color = new Color(1f, 0f, 0f, 0f);
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.SetStaticAmount(1f);
    }

    void RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}
