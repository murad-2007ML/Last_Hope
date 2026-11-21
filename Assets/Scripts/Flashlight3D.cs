using UnityEngine;
using UnityEngine.UI;

public class Flashlight3D : MonoBehaviour
{
    [SerializeField] Light spotLight;
    [SerializeField] float maxIntensity = 2.5f;
    [SerializeField] float drainRate    = 0.012f;
    [SerializeField] float lagSpeed     = 6f;

    [Header("Flashlight Models")]
    [SerializeField] GameObject flashlightStandard; // Standard unlit model ("Flashlight")
    [SerializeField] GameObject flashlightColor;    // Lit model ("FlashlightColor")

    [Header("UI")]
    [SerializeField] Slider batterySlider;
    [SerializeField] GameObject flashlightPromptPanel;

    float _battery = 1f;
    bool  _on;
    bool  _firstOnFired;
    Quaternion _targetRot;

    public static Flashlight3D Instance { get; private set; }
    public float Battery => _battery;
    public bool  IsOn    => _on;

    void Awake()
    {
        Instance = this;
        if (spotLight == null)
            spotLight = GetComponentInChildren<Light>();
        // Start completely off
        if (spotLight != null) spotLight.intensity = 0f;
        _targetRot = transform.rotation;

        FindFlashlightModels();

        // Initially ensure standard model is visible and color model is hidden
        if (flashlightStandard != null) flashlightStandard.SetActive(true);
        if (flashlightColor != null) flashlightColor.SetActive(false);

        // Keep prompt hidden until WakeUpIntro awakens the player at 3.5s
        ShowPrompt(false);
    }

    void FindFlashlightModels()
    {
        Transform parentTransform = transform.parent != null ? transform.parent : transform;
        var allChildren = parentTransform.GetComponentsInChildren<Transform>(true);

        if (flashlightStandard == null)
        {
            foreach (var t in allChildren)
            {
                if (t.name.Equals("Flashlight", System.StringComparison.OrdinalIgnoreCase) && t != transform)
                {
                    flashlightStandard = t.gameObject;
                    break;
                }
            }
        }

        if (flashlightColor == null)
        {
            foreach (var t in allChildren)
            {
                if (t.name.Equals("FlashlightColor", System.StringComparison.OrdinalIgnoreCase))
                {
                    flashlightColor = t.gameObject;
                    break;
                }
            }
            if (flashlightColor == null)
            {
                var go = GameObject.Find("FlashlightColor");
                if (go != null) flashlightColor = go;
            }
        }
    }

    void Update()
    {
        // Only toggle when no inspect panel is open
        bool anyPanelOpen = PuzzleManager3D.Instance != null && PuzzleManager3D.Instance.AnyPanelOpen;

        if (!anyPanelOpen)
        {
            // Toggle strictly with Right-Click or F. Left-Click is NEVER used for flashlight!
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F))
            {
                Toggle();
            }
        }

        UpdateLight();
        UpdateSway();
        UpdateBatteryUI();
    }

    void Toggle()
    {
        _on = !_on;
        if (ProceduralAudio.Instance != null) ProceduralAudio.Instance.PlayFlashlightClick();

        // Swap between standard model and illuminated FlashlightColor model
        if (flashlightStandard != null) flashlightStandard.SetActive(!_on);
        if (flashlightColor != null)    flashlightColor.SetActive(_on);

        // Always hide prompt when flashlight is turned on
        if (_on)
        {
            ShowPrompt(false);
            if (!_firstOnFired)
            {
                _firstOnFired = true;
                if (PuzzleManager3D.Instance != null)
                    PuzzleManager3D.Instance.OnFirstFlashlightOn();
                else
                    FindAnyObjectByType<PuzzleManager3D>()?.OnFirstFlashlightOn();
            }
        }
    }

    void UpdateLight()
    {
        if (_on && _battery > 0f)
        {
            _battery = Mathf.Max(0f, _battery - drainRate * Time.deltaTime);
            float dimFactor = _battery < 0.05f ? 0.2f : Mathf.Lerp(0.2f, 1f, _battery);
            if (spotLight != null)
                spotLight.intensity = Mathf.Lerp(spotLight.intensity, maxIntensity * dimFactor, Time.deltaTime * 5f);
        }
        else if (spotLight != null)
        {
            float target = (_on && _battery <= 0f) ? maxIntensity * 0.2f : 0f;
            spotLight.intensity = Mathf.Lerp(spotLight.intensity, target, Time.deltaTime * 5f);
        }
    }

    void UpdateSway()
    {
        Vector3 mDelta = new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0f) * 0.3f;
        _targetRot = Quaternion.Euler(mDelta) * transform.parent.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * lagSpeed);
    }

    void UpdateBatteryUI()
    {
        if (batterySlider != null) batterySlider.value = _battery;
    }

    public void ShowPrompt(bool visible)
    {
        if (flashlightPromptPanel != null)
        {
            if (visible)
            {
                var tmp = flashlightPromptPanel.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = "Right-Click to turn on Flashlight";
                else
                {
                    var legacy = flashlightPromptPanel.GetComponentInChildren<Text>(true);
                    if (legacy != null) legacy.text = "Right-Click to turn on Flashlight";
                }
            }
            flashlightPromptPanel.SetActive(visible);
        }

        // Also ensure all instances of FlashlightPrompt in scene match requested visibility
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            var roots = activeScene.GetRootGameObjects();
            foreach (var r in roots)
            {
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == "FlashlightPrompt")
                    {
                        t.gameObject.SetActive(visible);
                    }
                }
            }
        }
    }
}
