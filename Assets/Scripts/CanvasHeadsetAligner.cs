using System.Collections;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Canvas))]
public class CanvasHeadsetAligner : MonoBehaviour
{
    [Header("Placement")]
    [Range(0.3f, 5f)]
    public float distanceFromHead;

    [Range(-1f, 1f)]
    public float verticalOffset = 0f;

    [Header("Timing")]
    [Range(0f, 3f)]
    public float startupDelay = 0.5f;

    private Canvas _canvas;
    private Camera _cam;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _cam = _canvas.worldCamera;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[CanvasHeadsetAligner] No camera found.");
            return;
        }

        if (_canvas.renderMode != RenderMode.WorldSpace)
        {
            _canvas.renderMode = RenderMode.WorldSpace;
            Debug.LogWarning("[CanvasHeadsetAligner] Canvas forced to World Space.");
        }
    }

    private void Start()
    {
        if (PlayerPrefs.HasKey("CalibratedDistance"))
        {
            distanceFromHead = PlayerPrefs.GetFloat("CalibratedDistance");
        }

        if (_cam != null)
            StartCoroutine(AlignOnStartup());
    }

    private void OnEnable()
    {
        if (PlayerPrefs.HasKey("CalibratedDistance"))
        {
            float savedDistance = PlayerPrefs.GetFloat("CalibratedDistance");
            SetDistanceFromHead(savedDistance);
        }

        if (IsHeadsetTracked())
        {
            SnapToHeadset();
        }
    }

    private IEnumerator AlignOnStartup()
    {
        yield return new WaitForSeconds(startupDelay);

        float timeout = 3f, elapsed = 0f;
        while (!IsHeadsetTracked() && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        _canvas.enabled = false;
        SnapToHeadset();
        _canvas.enabled = true;
    }

    private void SnapToHeadset()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[CanvasHeadsetAligner] SnapToHeadset: no camera found, aborting.");
            return;
        }

        transform.position = TargetPosition();
        transform.rotation = TargetRotation();
    }

    private Vector3 TargetPosition()
    {
        Vector3 camForwardFlat = new Vector3(_cam.transform.forward.x, 0f, _cam.transform.forward.z).normalized;

        return _cam.transform.position
             + camForwardFlat * distanceFromHead
             + Vector3.up * verticalOffset;
    }

    private Quaternion TargetRotation()
    {
        Vector3 camForwardFlat = new Vector3(_cam.transform.forward.x, 0f, _cam.transform.forward.z);

        if (camForwardFlat == Vector3.zero) return Quaternion.identity;

        return Quaternion.LookRotation(camForwardFlat.normalized, Vector3.up);
    }

    public void RecenterInstant(float newDistance)
    {
        StopAllCoroutines();
        distanceFromHead = newDistance;
        SnapToHeadset();
    }

    public void Recenter()
    {
        StopAllCoroutines();

        if (!gameObject.activeInHierarchy)
        {
            SnapToHeadset();
            return;
        }

        StartCoroutine(AlignOnStartup());
    }

    public void SetDistanceFromHead(float distance)
    {
        distanceFromHead = distance;
    }

    private static bool IsHeadsetTracked()
    {
        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.HeadMounted | InputDeviceCharacteristics.TrackedDevice,
            devices);

        foreach (var d in devices)
        {
            if (d.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState s) &&
                (s & (InputTrackingState.Position | InputTrackingState.Rotation)) != 0)
                return true;
        }
        return false;
    }
}