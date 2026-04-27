using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.HID;
using UnityEngine.UIElements;

public class CSVLogger : MonoBehaviour
{

    [Header("User Settings")]
    [Tooltip("Enter the participant or session User ID here.")]
    public string userID;

    [Header("Logging Settings")]
    [Tooltip("How many times per second positions are recorded.")]
    public float logInterval;   // 10 Hz default

    [Tooltip("Log on every Update instead of on a timer.")]
    public bool logEveryFrame = false;

    private string _filePath;
    private float _timer;
    private bool _initialized;

    public RectTransform eyeGaze;
    public GameObject headPosition;

    private float zoomIn;
    private float zoomOut;
    private float calibrationReset;


    public GameObject imagesRxRayParent;
    public GameObject pause;


    [Header("RX-Ray Image Reference")]
    public GameObject imagesContainer;

    private float xminShownImage;
    private float yminShownImage;
    private float xmaxShownImage;
    private float ymaxShownImage;

    private float eyeGazeX;
    private float eyeGazeY;

    private float normalizationEyeGazeX;
    private float normalizationEyeGazeY;

    [SerializeField] private ImageLuminanceController luminanceController;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (string.IsNullOrWhiteSpace(userID))
        {
            Debug.LogError("[PositionLogger] UserID is empty! Set it in the Inspector.");
            enabled = false;
            return;
        }

        InitializeCSV();
        calibrationReset = headPosition.transform.position.z;

        eyeGazeX = eyeGaze.position.x;
        eyeGazeY = eyeGaze.position.y;
    }

    // Update is called once per frame
    void Update()
    {
        if (!_initialized) return;

        if (logEveryFrame)
        {
            WriteRow();
        }
        else
        {
            _timer += Time.deltaTime;
            if (_timer >= logInterval)
            {
                _timer = 0f;
                WriteRow();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Creates the CSVLoggers folder and writes the CSV header.
    private void InitializeCSV()
    {
        // Build folder path: [project root]/CSVLoggers/
        // Application.dataPath = …/Assets  →  parent = project root
        string folderPath = Path.Combine(Application.dataPath, "..", "CSVLoggers");
        folderPath = Path.GetFullPath(folderPath);   // normalize slashes

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        // Unique filename per user + session timestamp (avoids overwriting)
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{userID}_{timestamp}_gaze.csv";
        _filePath = Path.Combine(folderPath, fileName);

        // Write header row
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("UserID,Timestamp,SessionTime,ImageName, WindowLevel, ZoomIn,ZoomOut,HeadGazeZ, HeadGazeY, HeadGazeZ, EyeGazeX (normalization), EyeGazeY (normalization)");
        File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);

        _initialized = true;
        Debug.Log($"[PositionLogger] Logging to: {_filePath}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Appends one data row to the CSV.
    private void WriteRow()
    {
        
        if (imagesRxRayParent.activeSelf == true && pause.activeSelf == false) {
            RectTransform rt = eyeGaze.GetComponent<RectTransform>();

            // ISO-8601 wall-clock time + Unity session time
            string wallTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string sessionTime = Time.time.ToString("F4");
            string activeImage = GetActiveImageName();

            float headPosX = headPosition.transform.position.x;
            float headPosY = headPosition.transform.position.y;
            float headPosZ = headPosition.transform.position.z;

            eyeGazeX = eyeGaze.position.x;
            eyeGazeY = eyeGaze.position.y;

            //Zoom
            if (headPosZ > calibrationReset)
            {
                zoomIn = headPosZ - calibrationReset;
                zoomOut = 0;
            }
            else
            {
                zoomOut = calibrationReset - headPosZ;
                zoomIn = 0;
            }

            float windowLevel = luminanceController.GetWindowLevel();

            if (activeImage != "None" && normalizationEyeGazeX >= 0 && normalizationEyeGazeX <= 1 && normalizationEyeGazeY >= 0 && normalizationEyeGazeY <= 1)
            {
                string line = $"{userID},{wallTime},{sessionTime},{activeImage},{windowLevel},{zoomIn},{zoomOut},{headPosX},{headPosY},{headPosZ},{normalizationEyeGazeX},{normalizationEyeGazeY}";
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }


        }
    
    }

    private string GetActiveImageName()
    {
        if (imagesContainer == null) return "None";

        foreach (Transform child in imagesContainer.gameObject.transform)
        {
            if (child.gameObject.activeSelf)
            {
                //x min, y min, x max and y max of the image
                // Get sprite renderer from the active child
                SpriteRenderer ActiveImage = child.gameObject.GetComponent<SpriteRenderer>();

                if (ActiveImage != null)
                {
                    Bounds bounds = ActiveImage.bounds;

                    xminShownImage = bounds.min.x;
                    yminShownImage = bounds.min.y;
                    xmaxShownImage = bounds.max.x;
                    ymaxShownImage = bounds.max.y;
                }
                normalizationEyeGazeX = Mathf.Abs(eyeGazeX - xminShownImage) / Mathf.Abs(xmaxShownImage - xminShownImage);
                normalizationEyeGazeY = Mathf.Abs(eyeGazeY - yminShownImage) / Mathf.Abs(ymaxShownImage - yminShownImage);

                return child.gameObject.name;
            }
           
        }

        return "None";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Call this from another script (e.g. a button) to flush and close gracefully.
    // The file is always valid because we append line-by-line, but this is handy
    // if you want to stop logging mid-session.
    public void StopLogging()
    {
        _initialized = false;
        Debug.Log("[PositionLogger] Logging stopped.");
    }

}
