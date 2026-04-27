using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class ImageEffectsController : MonoBehaviour
{
    [Header("References")]
    public Transform rxRayImageRoot;

    [Header("Luminance Settings")]
    [Range(0.0001f, 0.1f)] public float mouseSensitivity = 0.01f;
    [Range(0f, 1f)] public float minLuminance = 0f;
    [Range(0f, 1f)] public float maxLuminance = 1f;
    [Range(0f, 1f)] public float defaultLuminance = 0.5f;

    [Header("Contrast Settings")]
    [Range(0.1f, 2f)] public float minContrast = 0.1f;
    [Range(0.1f, 2f)] public float maxContrast = 10f;
    [Range(0.1f, 2f)] public float defaultContrast = 1f;

    [Tooltip("Max seconds between press and release to count as a click.")]
    public float clickThreshold = 0.2f;

    // ── Private ──────────────────────────────────────────────────────────────

    private float _currentLuminance;
    private float _currentContrast;
    private Color _originalColor;       // snapshotted ONCE per image switch
    private Renderer _activeRenderer;
    private Transform _lastActiveImage;
    private Material _activeMaterial;

    private float _leftButtonPressTime;
    private bool _mouseMoved;
    private bool _wasPressed;

    // ── Public getters ───────────────────────────────────────────────────────

    public float GetWindowLevel() => _currentLuminance;
    public float GetWindowWidth() => _currentContrast;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _currentLuminance = defaultLuminance;
        _currentContrast = defaultContrast;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        bool isPressed = mouse.leftButton.isPressed;
        bool wasPressed = _wasPressed;
        _wasPressed = isPressed;

        if (isPressed && !wasPressed)
        {
            _leftButtonPressTime = Time.time;
            _mouseMoved = false;
        }

        if (isPressed)
        {
            float dy = mouse.delta.y.ReadValue();
            if (!Mathf.Approximately(dy, 0f))
            {
                _mouseMoved = true;
                _currentLuminance = Mathf.Clamp(
                    _currentLuminance + dy * mouseSensitivity,
                    minLuminance, maxLuminance);
                ApplyAdjustmentsToActiveImage();
            }

            float dx = mouse.delta.x.ReadValue();
            if (!Mathf.Approximately(dx, 0f))
            {
                _mouseMoved = true;
                _currentContrast = Mathf.Clamp(
                    _currentContrast + dx * mouseSensitivity,
                    minContrast, maxContrast);
                ApplyAdjustmentsToActiveImage();
            }
        }

        if (!isPressed && wasPressed)
        {
            float holdDuration = Time.time - _leftButtonPressTime;
            if (!_mouseMoved && holdDuration <= clickThreshold)
            {
                ResetAdjustments();
                Debug.Log("[Effects] Reset to defaults.");
            }
        }
    }

    // ── Core apply — single pipeline ─────────────────────────────────────────

    private void ApplyAdjustmentsToActiveImage()
    {
        Transform activeImage = GetActiveImageChild();
        if (activeImage == null) return;

        // Snapshot _originalColor only when the active image changes.
        // Never overwrite it during manipulation.
        if (activeImage != _lastActiveImage)
        {
            _lastActiveImage = activeImage;
            _activeRenderer = activeImage.GetComponent<Renderer>();

            if (_activeRenderer != null)
            {
                _activeMaterial = _activeRenderer.material;
                _originalColor = _activeMaterial.color;
                _currentLuminance = defaultLuminance;
                _currentContrast = defaultContrast;
            }
        }

        if (_activeMaterial == null) return;

        // Step 1 — luminance applied to the original snapshot
        Color afterLuminance = ApplyLuminance(_originalColor, _currentLuminance);

        // Step 2 — contrast applied on top of the luminance result
        _activeMaterial.color = ApplyContrast(afterLuminance, _currentContrast);

        Debug.Log($"[Effects] L={_currentLuminance:F3}  C={_currentContrast:F3}");
    }

    // ── Luminance  (0 = black · 0.5 = original · 1 = 4× brighter) ───────────

    private Color ApplyLuminance(Color original, float luminance)
    {
        RGBToHSL(original, out float h, out float s, out float origL);

        if (luminance <= 0.5f)
        {
            float t = luminance * 2f;              // [0 → 1]
            float finalL = Mathf.Lerp(0f, origL, t);
            return HSLToRGB(h, s, finalL);
        }
        else
        {
            float t = (luminance - 0.5f) * 2f; // [0 → 1]
            float multiplier = Mathf.Lerp(1f, 4f, t);   // 1× → 4×
            return original * multiplier;                // HDR-safe
        }
    }

    // ── Contrast  (≈0 = flat gray · 1 = original · 2 = high contrast) ───────

    private Color ApplyContrast(Color color, float contrast)
    {
        //// 1. Extract H, S, and V from the current color
        //Color.RGBToHSV(color, out float h, out float s, out float v);

        //// 2. Modify the saturation
        //// We clamp it between 0 and 1 because HSV values in Unity are normalized
        //float newSaturation = s * contrast;

        //// 3. Convert back to RGB
        //Color finalColor = Color.HSVToRGB(h, newSaturation, v);

        //// 4. Restore the original Alpha (HSV conversion ignores transparency)
        ////finalColor.a = color.a;

        //return finalColor;

        // The pivot point (0.5) is neutral gray. 
        // Values higher than 0.5 get brighter, values lower get darker.
        float pivot = 0.5f;

        // Direct RGB manipulation
        float r = (color.r - pivot) * contrast + pivot;
        float g = (color.g - pivot) * contrast + pivot;
        float b = (color.b - pivot) * contrast + pivot;

        // HDR Safety: 
        // We use Max(0) to prevent negative colors (which cause black artifacts), 
        // but we do NOT clamp the top so that HDR values (> 1.0) can stay bright.
        return new Color(
            Mathf.Max(0f, r),
            Mathf.Max(0f, g),
            Mathf.Max(0f, b),
            color.a
        );

    }



    // ── Reset (button or click) ───────────────────────────────────────────────

    public void ResetAdjustments()
    {
        _currentLuminance = defaultLuminance;
        _currentContrast = defaultContrast;
        ApplyAdjustmentsToActiveImage();

        // Sync UI sliders here if needed:
        // luminanceSlider.value = defaultLuminance;
        // contrastSlider.value  = defaultContrast;
    }

    // ── HSL helpers ──────────────────────────────────────────────────────────

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