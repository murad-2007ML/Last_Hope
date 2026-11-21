using UnityEngine;
using UnityEngine.UI;

public class WaterController3D : MonoBehaviour
{
    [SerializeField] Transform waterPlane;
    [SerializeField] float startY = 0.15f;
    [SerializeField] float maxY = 0.85f;
    [SerializeField] float riseSpeed = 0.0008333333f; // 0.70m over 840s (14 minutes)
    [SerializeField] Transform radioTransform;
    [SerializeField] GameObject gameOverPanel;
    [SerializeField] Button restartButton;
    [SerializeField] FirstPersonController fpsController;

    [Header("Water Level HUD")]
    [SerializeField] Slider waterLevelSlider;
    [SerializeField] Image waterFillImage;
    [SerializeField] Text waterLevelText;
    [SerializeField] Text timeRemainingText;
    [SerializeField] Image warningFlashImage;

    [Header("Elapsed Timer")]
    [SerializeField] Text elapsedTimerText;

    float _speedMultiplier = 1f;
    bool _rising = true;
    bool _gameOver;
    float _flashTimer;
    float _elapsed;
    bool _muradWaterLinePlayed;

    // Colors for fill bar: safe → danger → critical
    static readonly Color ColorSafe     = new Color(0.15f, 0.45f, 0.85f);
    static readonly Color ColorDanger   = new Color(0.9f,  0.6f,  0.1f);
    static readonly Color ColorCritical = new Color(0.9f,  0.1f,  0.1f);

    public bool Rising { get => _rising; set => _rising = value; }

    void Start()
    {
        // Calibrate rise speed to exactly 840 seconds (14 minutes) for realistic pacing
        riseSpeed = (maxY - startY) / 840f;

        if (waterPlane != null)
        {
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

        if (radioTransform != null && pos.y >= radioTransform.position.y)
            TriggerGameOver();

        if (pos.y >= maxY)
            TriggerGameOver();

        float fillPct = Mathf.Clamp01((pos.y - startY) / (maxY - startY));
        UpdateHUD(fillPct);
        UpdateWarningFlash(fillPct);

        // Murad reacts when water hits 50% (preserving character voice)
        if (!_muradWaterLinePlayed && fillPct >= 0.5f)
        {
            _muradWaterLinePlayed = true;
            var clip = Resources.Load<AudioClip>("Audio/Murad/murad_02_water");
            DialogueManager.Instance?.SpeakMurad(
                "The water... it's rising faster. You have to hurry.", clip);
        }
    }

    public void AddSpeedPercent(float percent)
    {
        _speedMultiplier += percent / 100f;
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
            if (_rising && waterPlane != null)
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

        // Pulse red at screen edges when critical
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
