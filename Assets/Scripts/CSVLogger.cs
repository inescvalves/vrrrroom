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

    public GameObject eyeGaze;
    public GameObject headPosition;

    private float zoomIn;
    private float zoomOut;
    private float calibrationReset;


    public GameObject imagesRxRayParent;
    public GameObject pause;


    [Header("RX-Ray Image Reference")]
    public GameObject imagesContainer;

    //private float xminShownImage;
    //private float yminShownImage;
    //private float xmaxShownImage;
    //private float ymaxShownImage;

    //private float eyeGazeX;
    //private float eyeGazeY;



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
        //xminShownImage = 0;
        //yminShownImage = 0;
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
        sb.AppendLine("UserID,Timestamp,SessionTime,ImageName,ZoomIn,ZoomOut,HeadGazeZ, HeadGazeY, HeadGazeZ, EyeGazeX,EyeGazeY");
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

            if (activeImage != "None")
            {
                string line = $"{userID},{wallTime},{sessionTime},{activeImage},{zoomIn},{zoomOut},{headPosX},{headPosY},{headPosZ},{rt.position.x},{rt.position.x}";
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
                // recalculate the eye gaze circle
                //EyeGazeToPixelCoordinates(child);
                return child.gameObject.name;
            }
           
        }

        return "None";
    }

    //private string EyeGazeToPixelCoordinates(GameObject child)
    //{
    //    SpriteRenderer sr = child.GetComponent<SpriteRenderer>();

    //    xmaxShownImage = sr.sprite.rect.width;
    //    ymaxShownImage = sr.sprite.rect.height;

    //    // Convert Circle world position → local position relative to image
    //    RectTransform rt = eyeGaze.GetComponent<RectTransform>();

    //    eyeGazeX = xmaxShownImage / 2f + rt.position.x;
    //    eyeGazeY = ymaxShownImage / 2f + rt.position.y;

    //    return null;
    //}
    

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
