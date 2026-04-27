using UnityEngine;
using UnityEngine.InputSystem;

public class ImageLuminanceController : MonoBehaviour
{
    [Header("References")]
    public Transform rxRayImageRoot;

    [Header("Luminance Settings")]
    [Range(0.0001f, 0.1f)]
    public float mouseSensitivity;

    [Range(0f, 1f)]
    public float minLuminance;

    [Range(0f, 1f)]
    public float maxLuminance;

    [Range(0f, 1f)]
    public float defaultLuminance;

    [Tooltip("Max seconds between press and release to count as a click.")]
    public float clickThreshold;

    private Color _originalColor;

    // ── private ──────────────────────────────────────────────────────────────

    private float _currentLuminance;
    private Renderer _activeRenderer;
    private Transform _lastActiveImage;
    private Material _activeMaterial;

    private float _leftButtonPressTime;
    private bool _mouseMoved;
    private bool _wasPressed;

    // ── Public getter — remapped to 0-1 ──────────────────────────────────────

    public float GetWindowLevel() => _currentLuminance;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _currentLuminance = defaultLuminance;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse == null) return;

        // 1. Hide/Lock Mouse on Click
        if (mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // 2. Show Mouse on Escape
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Mouse.current == null) return;

        bool isPressed = Mouse.current.leftButton.isPressed;
        bool wasPressed = _wasPressed;
        _wasPressed = isPressed;

        // ── Button just pressed ───────────────────────────────────────────
        if (isPressed && !wasPressed)
        {
            _leftButtonPressTime = Time.time;
            _mouseMoved = false;
        }

        // ── Button held — check for mouse movement ────────────────────────
        if (isPressed)
        {
            float mouseDeltaY = Mouse.current.delta.y.ReadValue();

            if (!Mathf.Approximately(mouseDeltaY, 0f))
            {
                _mouseMoved = true;

                _currentLuminance = Mathf.Clamp(
                    _currentLuminance + mouseDeltaY * mouseSensitivity,
                    minLuminance,
                    maxLuminance
                );

                ApplyLuminanceToActiveImage();
            }
        }

        // ── Button just released ──────────────────────────────────────────
        if (!isPressed && wasPressed)
        {
            float holdDuration = Time.time - _leftButtonPressTime;

            bool isClick = !_mouseMoved && holdDuration <= clickThreshold;

            if (isClick)
            {
                // Single click → reset luminance
                ResetLuminance();
                Debug.Log("[Luminance] Reset to default.");
            }
        }
    }

    // ── Luminance ────────────────────────────────────────────────────────────

    private void ApplyLuminanceToActiveImage()
    {
        Transform activeImage = GetActiveImageChild();
        if (activeImage == null) return;

        if (activeImage != _lastActiveImage)
        {
            _lastActiveImage = activeImage;
            _activeRenderer = activeImage.GetComponent<Renderer>();

            if (_activeRenderer != null)
            {
                _activeMaterial = _activeRenderer.material;

                // Save the ORIGINAL color BEFORE any modification
                _originalColor = _activeMaterial.color;

                // Extract original luminance from it
                RGBToHSL(_originalColor, out float h, out float s, out float originalL);
                _currentLuminance = originalL;
                defaultLuminance = originalL;
            }
        }

        if (_activeMaterial == null) return;

        // Always use H and S from the ORIGINAL color, only change L
        RGBToHSL(_originalColor, out float hue, out float sat, out float _);
        _activeMaterial.color = HSLToRGB(hue, sat, _currentLuminance);
    }

    public void ResetLuminance()
    {
        _currentLuminance = defaultLuminance;
        ApplyLuminanceToActiveImage();
    }

    // ── HSL ──────────────────────────────────────────────────────────────────

    private static void RGBToHSL(Color color, out float h, out float s, out float l)
    {
        float r = color.r, g = color.g, b = color.b;
        float max = Mathf.Max(r, g, b);
        float min = Mathf.Min(r, g, b);
        float delta = max - min;

        l = (max + min) / 2f;
        s = delta == 0f ? 0f : delta / (1f - Mathf.Abs(2f * l - 1f));

        if (delta == 0f) { h = 0f; }
        else if (max == r) { h = (((g - b) / delta) % 6f + 6f) % 6f / 6f; }
        else if (max == g) { h = ((b - r) / delta + 2f) / 6f; }
        else { h = ((r - g) / delta + 4f) / 6f; }
    }

    private static Color HSLToRGB(float h, float s, float l)
    {
        if (s == 0f) return new Color(l, l, l);

        float c = (1f - Mathf.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Mathf.Abs((h * 6f) % 2f - 1f));
        float m = l - c / 2f;

        float r, g, b;
        switch (Mathf.FloorToInt(h * 6f) % 6)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }

        return new Color(r + m, g + m, b + m);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Transform GetActiveImageChild()
    {
        if (rxRayImageRoot == null) return null;
        foreach (Transform child in rxRayImageRoot)
            if (child.gameObject.activeInHierarchy) return child;
        return null;
    }

    private void OnDestroy()
    {
        if (_activeMaterial != null)
            Destroy(_activeMaterial);
    }
}