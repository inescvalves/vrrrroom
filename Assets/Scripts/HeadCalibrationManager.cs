using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class HeadCalibrationManager : MonoBehaviour
{
    public static HeadCalibrationManager Instance { get; private set; }

    [Header("Camera Reference")]
    public Transform cameraTransform;

    [Header("Calibration Result UI")]
    public TextMeshProUGUI calibrationResultText;

    [Header("RX Image UI")]
    public GameObject imageRX;
    public GameObject imageChildRX1;

    [Header("Canvas Aligner")]
    public CanvasHeadsetAligner targetAligner;

    // Recorded positions
    private float baseZ;
    private float forwardZ;
    private float backwardZ;

    // Tracking which phase is active
    private bool trackingForward = false;
    private bool trackingBackward = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
    }

    private void Update()
    {
        if (trackingForward)
        {
            float currentZ = cameraTransform.position.z;
            if (currentZ > baseZ)
            {
                forwardZ = currentZ;
            }
        }

        if (trackingBackward)
        {
            float currentZ = cameraTransform.position.z;
            if (currentZ < baseZ)
            {
                backwardZ = currentZ;
            }
        }
 
        if (imageChildRX1.activeSelf && Mouse.current.leftButton.wasPressedThisFrame && UIManager.Instance.isOnEllipseScreen == false)
        {
            float distance = RecordDistance();
        }
    }

    public float RecordDistance()
    {
        float distance = Mathf.Abs(cameraTransform.position.z - imageRX.transform.position.z);
        
        PlayerPrefs.SetFloat("CalibratedDistance", distance);
        PlayerPrefs.Save();

        if (targetAligner != null)
        {
            targetAligner.SetDistanceFromHead(distance);
            targetAligner.Recenter();
        }
        else
        {
            Debug.LogWarning("HeadCalibrationManager: targetAligner is not assigned.");
        }

        return distance;
    }


    // -------------------------------------------------------
    // Called by UIManager when each canvas becomes active
    // -------------------------------------------------------

    public void OnStartCalibrationEnabled()
    {
        baseZ = cameraTransform.position.z;
        forwardZ = baseZ;
        backwardZ = baseZ;
        trackingForward = false;
        trackingBackward = false;
        Debug.Log($"Calibration: Base Z recorded = {baseZ}");
    }

    public void OnCalibrationForwardEnabled()
    {
        trackingForward = true;
        trackingBackward = false;
        Debug.Log("Calibration: Tracking forward Z...");
    }

    public void OnCalibrationBackwardEnabled()
    {
        trackingForward = false;
        trackingBackward = true;
        Debug.Log("Calibration: Tracking backward Z...");
    }

    public void OnCalibrationResultsEnabled()
    {
        trackingForward = false;
        trackingBackward = false;

        float forwardCm = Mathf.Abs(forwardZ - baseZ) * 100f;
        float backwardCm = Mathf.Abs(backwardZ - baseZ) * 100f;

        if (calibrationResultText != null)
        {
            calibrationResultText.text =
                $"<color=\"yellow\">{forwardCm:F1}</color> cm forward\n" +
                $"<color=\"orange\">{backwardCm:F1}</color> cm backward";
        }

        Debug.Log($"Calibration Results — Forward: {forwardCm:F1} cm | Backward: {backwardCm:F1} cm");
    }
}
