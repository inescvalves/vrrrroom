using System.Collections;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Canvas))]
public class CanvasHeadsetAligner : MonoBehaviour
{
    [Header("Placement")]
    [Range(0.3f, 5f)]
    public float distanceFromHead = 1.5f;

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
        if (_cam != null)
            StartCoroutine(AlignOnStartup());
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

        // Disable the GameObject before snapping so the jump is never rendered
        gameObject.SetActive(false);
        SnapToHeadset();
        gameObject.SetActive(true);
    }

    private void SnapToHeadset()
    {
        transform.position = TargetPosition();
        transform.rotation = TargetRotation();
    }

    private Vector3 TargetPosition()
    {
        // Use full camera forward (including pitch on Y axis)
        return _cam.transform.position
             + _cam.transform.forward * distanceFromHead
             + Vector3.up * verticalOffset;
    }

    private Quaternion TargetRotation()
    {
        // Canvas faces the camera, Y axis matches camera's Y axis fully
        Vector3 lookDir = TargetPosition() - _cam.transform.position;
        if (lookDir == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(lookDir.normalized);
    }

    public void Recenter()
    {
        StopAllCoroutines();
        StartCoroutine(AlignOnStartup());
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var cam = GetComponent<Canvas>()?.worldCamera ?? Camera.main;
        if (cam == null) return;

        Vector3 target = cam.transform.position
                       + cam.transform.forward * distanceFromHead
                       + Vector3.up * verticalOffset;

        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
        Gizmos.DrawWireSphere(target, 0.04f);
        Gizmos.DrawLine(cam.transform.position, target);

        float w = 780f * 0.002f;
        float h = GetComponent<RectTransform>().rect.height * 0.002f;
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireCube(target, new Vector3(w, h == 0f ? 0.5f : h, 0.001f));
    }
#endif

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