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

    public float Battery => _battery;
    public bool  IsOn    => _on;

    void Awake()
    {
        if (spotLight == null)
            spotLight = GetComponentInChildren<Light>();
        // Start completely off
        if (spotLight != null) spotLight.intensity = 0f;
        _targetRot = transform.rotation;

        FindFlashlightModels();

        // Initially ensure standard model is visible and color model is hidden
        if (flashlightStandard != null) flashlightStandard.SetActive(true);
        if (flashlightColor != null) flashlightColor.SetActive(false);

        ShowPrompt(true);
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

        // Support Left-Click (as requested) as well as Right-Click and F key
        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F)) && !anyPanelOpen)
            Toggle();

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

        if (_on && !_firstOnFired)
        {
            _firstOnFired = true;
            ShowPrompt(false);
            PuzzleManager3D.Instance?.OnFirstFlashlightOn();
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

    void ShowPrompt(bool visible)
    {
        if (flashlightPromptPanel != null) flashlightPromptPanel.SetActive(visible);
    }
}
