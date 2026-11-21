using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public static FirstPersonController Instance { get; private set; }

    [SerializeField] float walkSpeed = 3.5f;
    [SerializeField] float wadeSpeed = 1.8f;
    [SerializeField] float mouseSensitivity = 2.0f;
    [SerializeField] float bobFrequency = 1.8f;
    [SerializeField] float bobAmplitude = 0.055f;
    [SerializeField] Transform cameraTransform;

    CharacterController _cc;
    float _xRot;
    float _bobTimer;
    float _bobVelocity;
    Vector3 _camLocalOrigin;
    bool _locked = true;
    bool _isWading;

    public bool MouseLocked
    {
        get => _locked;
        set
        {
            _locked = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }
    }

    public bool IsWading { set => _isWading = value; }

    void Awake()
    {
        Instance = this;
        _cc = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform != null)
            _camLocalOrigin = cameraTransform.localPosition;
        MouseLocked = true;
    }

    void Update()
    {
        if (!_locked) return;
        HandleLook();
        HandleMove();
        HandleBob();
    }

    void HandleLook()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        _xRot = Mathf.Clamp(_xRot - my, -80f, 80f);
        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(_xRot, 0, 0);
        transform.Rotate(Vector3.up * mx);
    }

    void HandleMove()
    {
        float speed = _isWading ? wadeSpeed : walkSpeed;
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 dir = transform.right * h + transform.forward * v;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        Vector3 velocity = dir * speed + Vector3.down * 9.81f;
        _cc.Move(velocity * Time.deltaTime);
    }

    void HandleBob()
    {
        if (cameraTransform == null) return;
        float inputMag = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).magnitude;
        _bobVelocity = Mathf.Lerp(_bobVelocity, inputMag, Time.deltaTime * 8f);
        if (_bobVelocity > 0.1f)
            _bobTimer += Time.deltaTime * bobFrequency * Mathf.PI * 2f;
        float bobY = Mathf.Sin(_bobTimer) * bobAmplitude * _bobVelocity;
        float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmplitude * 0.5f * _bobVelocity;
        cameraTransform.localPosition = _camLocalOrigin + new Vector3(bobX, bobY, 0);
    }
}
