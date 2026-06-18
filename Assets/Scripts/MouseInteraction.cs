using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach this script to any GameObject with a Renderer component.
/// Left mouse click  → Red
/// Right mouse click → Green
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MouseInteraction : MonoBehaviour
{
    [Header("Colors")]
    [SerializeField] private Color leftClickColor = Color.red;
    [SerializeField] private Color rightClickColor = Color.green;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Transition")]
    [SerializeField] private bool smoothTransition = true;
    [SerializeField] private float transitionSpeed = 8f;

    // ── private state ──────────────────────────────────────────────
    private Renderer _renderer;
    private Material _material;          // instance material (no shared-material mutation)
    private Color _targetColor;

    // Input actions (New Input System)
    private Mouse _mouse;

    // ── lifecycle ──────────────────────────────────────────────────
    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _material = _renderer.material;     // creates a per-instance copy automatically
        _targetColor = defaultColor;
        _material.color = defaultColor;
    }

    private void OnEnable()
    {
        _mouse = Mouse.current;
    }

    private void Update()
    {
        ReadInput();
        ApplyColor();
    }

    private void OnDestroy()
    {
        // Clean up the instance material we created
        if (_material != null)
            Destroy(_material);
    }

    // ── input ──────────────────────────────────────────────────────
    private void ReadInput()
    {
        if (_mouse == null)
        {
            _mouse = Mouse.current;     // re-query in case device connected late
            return;
        }

        if (_mouse.leftButton.wasPressedThisFrame)
        {
            _targetColor = leftClickColor;
            Debug.Log("[MouseColorChanger] Left click → Red");
        }
        else if (_mouse.rightButton.wasPressedThisFrame)
        {
            _targetColor = rightClickColor;
            Debug.Log("[MouseColorChanger] Right click → Green");
        }
        else if (_mouse.leftButton.wasReleasedThisFrame || _mouse.rightButton.wasReleasedThisFrame)
        {
            _targetColor = defaultColor;
        }
    }

    // ── color application ──────────────────────────────────────────
    private void ApplyColor()
    {
        if (smoothTransition)
        {
            _material.color = Color.Lerp(
                _material.color,
                _targetColor,
                Time.deltaTime * transitionSpeed
            );
        }
        else
        {
            _material.color = _targetColor;
        }
    }

#if UNITY_EDITOR
    // Lets you preview colors in the Inspector without entering Play Mode
    private void OnValidate()
    {
        if (_material != null)
            _material.color = defaultColor;
    }
#endif
}
