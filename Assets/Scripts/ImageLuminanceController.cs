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

    private float _currentLuminance;
    private Renderer _activeRenderer;
    private Transform _lastActiveImage;
    private Material _activeMaterial;

    private float _leftButtonPressTime;
    private bool _mouseMoved;
    private bool _wasPressed;

    public float GetWindowLevel() => _currentLuminance;

    private void Awake()
    {
        _currentLuminance = defaultLuminance;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse == null) return;

        // FIX: Removido o bloco que fazia CursorLockMode.Locked no clique esquerdo.
        // No Quest standalone, bloquear o cursor no primeiro clique matava todo o input
        // subsequente nos outros scripts (UIManager, VRCursorPainter, etc).
        // O cursor é gerido exclusivamente pelo UIManager.SetEllipsesLegend().

        // Show Mouse on Escape (mantido para debug no editor)
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
            float mouseDeltaY = mouse.delta.y.ReadValue();

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

        if (!isPressed && wasPressed)
        {
            float holdDuration = Time.time - _leftButtonPressTime;
            bool isClick = !_mouseMoved && holdDuration <= clickThreshold;

            if (isClick)
            {
                ResetLuminance();
                Debug.Log("[Luminance] Reset to default.");
            }
        }
    }

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
                _currentLuminance = defaultLuminance;
                _originalColor = _activeMaterial.color;
            }
        }

        if (_activeMaterial == null) return;

        RGBToHSL(_originalColor, out float hue, out float sat, out float originalL);

        Debug.Log("[Luminance] " + _currentLuminance);

        if (_currentLuminance <= 0.5f)
        {
            float t = _currentLuminance * 2f;
            float finalL = Mathf.Lerp(0f, originalL, t);
            _activeMaterial.color = HSLToRGB(hue, sat, finalL);
        }
        else
        {
            float t = (_currentLuminance - 0.5f) * 2f;
            float multiplier = Mathf.Lerp(1f, 4f, t);
            _activeMaterial.color = _originalColor * multiplier;
        }
    }

    public void ResetLuminance()
    {
        _currentLuminance = defaultLuminance;
        ApplyLuminanceToActiveImage();
    }

    private static void RGBToHSL(Color color, out float h, out float s, out float l)
    {
        Color.RGBToHSV(color, out float hHSV, out float sHSV, out float v);
        h = hHSV;
        l = v * (1f - sHSV / 2f);
        if (l <= 0f || l >= 1f)
            s = 0f;
        else
            s = (v - l) / Mathf.Min(l, 1f - l);
    }

    private static Color HSLToRGB(float h, float s, float l)
    {
        float v = l + s * Mathf.Min(l, 1f - l);
        float sHSV = v == 0f ? 0f : 2f * (1f - l / v);
        return Color.HSVToRGB(h, sHSV, v);
    }

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