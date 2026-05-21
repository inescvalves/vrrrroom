using UnityEngine;

public class EyeTrackingDisc : MonoBehaviour
{
    [Header("References")]
    public OVREyeGaze leftEye;   // drag OVREyeGaze GameObject
    public OVREyeGaze rightEye;  // drag OVREyeGaze GameObject
    public Transform rxRayImageRoot;

    private Renderer _renderer;

    private bool isActive = false;

    public void SetActive(bool active)
    {
        isActive = active;
        if (!active) SetVisible(false);
    }

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        SetVisible(false);
    }

    private void Update()
    {
        if (!isActive) return;

        // Average both eyes for more stable gaze
        OVREyeGaze activeEye = (leftEye != null && leftEye.EyeTrackingEnabled)
                               ? leftEye : rightEye;

        if (activeEye == null || !activeEye.EyeTrackingEnabled)
        {
            SetVisible(false);
            return;
        }

        Ray gazeRay = new Ray(activeEye.transform.position,
                              activeEye.transform.forward);

        if (Physics.Raycast(gazeRay, out RaycastHit hit, 50f))
        {
            if (rxRayImageRoot != null &&
                !hit.transform.IsChildOf(rxRayImageRoot) &&
                hit.transform != rxRayImageRoot)
            {
                SetVisible(false);
                return;
            }

            //Vector3 target = hit.point + hit.normal * surfaceOffset;
            //transform.position = Vector3.Lerp(transform.position, target,Time.deltaTime * followSpeed);
            transform.position = hit.point;
            transform.rotation = Quaternion.LookRotation(hit.normal);
            SetVisible(true);
        }
        else
        {
            SetVisible(false);
        }
    }

    private void SetVisible(bool v)
    {
        if (_renderer != null && _renderer.enabled != v)
            _renderer.enabled = v;
    }
}