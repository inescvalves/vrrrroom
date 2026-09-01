using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Globalization;
using System.IO;
using System.Text;

public class HeadCalibrationManager : MonoBehaviour
{
    public static HeadCalibrationManager Instance { get; private set; }

    [Header("Camera Reference")]
    public Transform cameraTransform;

    [Header("Calibration Result UI")]
    //public TextMeshProUGUI calibrationResultText;

    [Header("RX Image UI")]
    public GameObject imageRX;
    public GameObject imageChildRX1;

    [Header("Canvas Aligner")]
    public CanvasHeadsetAligner targetAligner;

    [Header("CSV Output")]
    public string csvLoggerObjectName = "CSVLogger";
    public string fallbackUserID = "unknown_user";
    public string outputFolderName = "CalibrationData";

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

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == "VRRRRoom Training")
        {
            // Always reset to 1.5m at the start of training
            PlayerPrefs.SetFloat("CalibratedDistance", 1.5f);
            PlayerPrefs.Save();

            if (targetAligner != null)
            {
                targetAligner.SetDistanceFromHead(1.5f);
                targetAligner.Recenter();
            }
            Debug.Log("Training started: distance reset to 1.5m");
        }
        else if (SceneManager.GetActiveScene().name == "VRRRRoom Static")
        {
            // Use whatever was saved during training (measured or 1.5 fallback)
            float savedDistance = PlayerPrefs.GetFloat("CalibratedDistance", 1.5f);

            if (targetAligner != null)
            {
                targetAligner.SetDistanceFromHead(savedDistance);
                targetAligner.Recenter();
            }
            Debug.Log($"Static scene: using calibrated distance = {savedDistance}m");
        }
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
            SaveDistanceCSV(distance);
        }
    }

    private void SaveDistanceCSV(float distance)
    {
        string folder;
#if UNITY_ANDROID && !UNITY_EDITOR
        folder = Path.Combine(Application.persistentDataPath, outputFolderName);
#else
        folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, outputFolderName);
#endif
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, $"{ResolveUserID()}_distance.csv");
        string value = distance.ToString("F4", CultureInfo.InvariantCulture);

        File.WriteAllText(path, "Distance\n" + value + "\n");
        Debug.Log($"[HeadCalibrationManager] Distance CSV written to: {path}");
    }

    public float RecordDistance()
    {
        if (cameraTransform == null || imageRX == null)
        {
            Debug.LogError("[HeadCalibrationManager] Missing cameraTransform or imageRX references!");
            return 1.5f;
        }

        // Default target position to the parent container
        Vector3 targetImagePosition = imageRX.transform.position;

        
        Transform imagesFolder = imageRX.transform.Find("Images");
        if (imagesFolder != null)
        {
            foreach (Transform child in imagesFolder)
            {
                if (child.gameObject.activeInHierarchy)
                {
                    targetImagePosition = child.position;
                    Debug.Log($"[HeadCalibrationManager] Found active child image: {child.name} for measuring.");
                    break;
                }
            }
        }

        // Calculate true 3D straight-line distance instead of strict Z-axis delta
        float distance = Vector3.Distance(cameraTransform.position, targetImagePosition);

        // Save to disk
        PlayerPrefs.SetFloat("CalibratedDistance", distance);
        PlayerPrefs.Save();
        Debug.Log($"[HeadCalibrationManager] Calibrated distance measured: {distance:F2}m");

        // Force ALL Canvas Aligner scripts in the scene to update instantly
        CanvasHeadsetAligner[] allAligners = Resources.FindObjectsOfTypeAll<CanvasHeadsetAligner>();
        foreach (var aligner in allAligners)
        {
            aligner.SetDistanceFromHead(distance);
            aligner.Recenter();
        }

        // Reposition RX-Ray Image to the new distance
        Vector3 camForwardFlat = new Vector3(
            cameraTransform.forward.x, 0f, cameraTransform.forward.z).normalized;

        Vector3 newPosition = cameraTransform.position + camForwardFlat * distance;

        imageRX.transform.position = new Vector3(
        imageRX.transform.position.x,
        imageRX.transform.position.y,
        newPosition.z);

        if (UIManager.Instance != null)
            UIManager.Instance.UpdateRxRayCanvasZ(newPosition.z);

        return distance;
    }

    private string ResolveUserID()
    {
        GameObject loggerGO = GameObject.Find(csvLoggerObjectName);
        if (loggerGO == null) return fallbackUserID;

        foreach (MonoBehaviour mb in loggerGO.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            foreach (string fieldName in new[] { "userID", "userId", "UserID", "UserId", "user_id" })
            {
                var field = mb.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (field != null && field.FieldType == typeof(string))
                {
                    string value = field.GetValue(mb) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
            }
        }
        return fallbackUserID;
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

        //if (calibrationResultText != null)
        //{
        //    calibrationResultText.text =
        //        $"<color=\"yellow\">{forwardCm:F1}</color> cm forward\n" +
        //        $"<color=\"orange\">{backwardCm:F1}</color> cm backward";
        //}

        Debug.Log($"Calibration Results — Forward: {forwardCm:F1} cm | Backward: {backwardCm:F1} cm");
    }
}
