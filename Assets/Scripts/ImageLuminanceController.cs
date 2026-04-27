using System;
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
                // Single click -> reset luminance
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
                _currentLuminance = defaultLuminance; // 0.5 = original appearance
                _originalColor = _activeMaterial.color;
            }
        }

        if (_activeMaterial == null) return;

        RGBToHSL(_originalColor, out float hue, out float sat, out float originalL);

        Debug.Log("[Luminance] " + _currentLuminance);

        if (_currentLuminance <= 0.5f)
        {
            // 0.0 → 0.5 : Black → original color
            float t = _currentLuminance * 2f;
            float finalL = Mathf.Lerp(0f, originalL, t);

            _activeMaterial.color = HSLToRGB(hue, sat, finalL);
        }
        else
        {
            // 0.5 → 1.0 : original color -> brighter (HDR multiply)
            float t = (_currentLuminance - 0.5f) * 2f; // [0, 1]
            float multiplier = Mathf.Lerp(1f, 4f, t);           // 1× → 4×

            // HDR color — values above 1 make it visibly brighter
            _activeMaterial.color = _originalColor * multiplier;
        }
    }

    public void ResetLuminance()
    {
        _currentLuminance = defaultLuminance;
        ApplyLuminanceToActiveImage();
    }

    // ── HSL ──────────────────────────────────────────────────────────────────

    private static void RGBToHSL(Color color, out float h, out float s, out float l)
    {
        // Step 1: RGB → HSV using Unity's built-in
        Color.RGBToHSV(color, out float hHSV, out float sHSV, out float v);

        // Step 2: HSV → HSL
        h = hHSV;                              // Hue is identical in both models
        l = v * (1f - sHSV / 2f);             // L = V * (1 - S_hsv / 2)

        if (l <= 0f || l >= 1f)               // S_hsl = 0 if L == 0 or L == 1
            s = 0f;
        else
            s = (v - l) / Mathf.Min(l, 1f - l); // S_hsl = (V - L) / min(L, 1-L)
    }

    private static Color HSLToRGB(float h, float s, float l)
    {
        // HSL → HSV
        float v = l + s * Mathf.Min(l, 1f - l);
        float sHSV = v == 0f ? 0f : 2f * (1f - l / v);

        return Color.HSVToRGB(h, sHSV, v);
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
        if (_activeMaterial != null) Destroy(_activeMaterial);
    }
}