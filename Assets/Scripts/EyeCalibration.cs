using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class EyeCalibration : MonoBehaviour
{
    [Header("Canvas & Area")]
    public RectTransform calibrationArea;

    [Header("Prefabs")]
    public GameObject blueCirclePrefab;

    [Header("Red Circle - Eye Tracking")]
    public RectTransform redRect;

    [Header("Circle Sizes")]
    public float blueCircleRadius = 40f;

    [Header("Blue Circle Drift")]
    [Tooltip("Speed at which the active blue circle drifts around its quadrant (units/sec).")]
    public float blueDriftSpeed = 60f;
    [Tooltip("How often (seconds) the drift picks a new random target within the quadrant.")]
    public float blueDriftInterval = 2f;

    [Header("Fifth Circle - Border")]
    [Tooltip("Speed at which the fifth circle travels around the border (units/sec).")]
    public float borderSpeed = 120f;
    [Tooltip("How long (seconds) the fifth circle travels before calibration ends. 0 = click to finish.")]
    public float borderDuration = 10f;

    [Header("UI References")]
    public TextMeshProUGUI statusText;

    [Header("CSV Logger")]
    public string csvLoggerObjectName = "CSVLogger";
    public string fallbackUserID = "unknown_user";

    [Header("Output")]
    [Tooltip("Folder name created at the root of your Unity project (next to Assets/).")]
    public string outputFolderName = "CalibrationData";

    [Header("RX-Ray Image Reference")]
    public GameObject imagesContainer;

    // ── Internal state ─────────────────────────────────────────────────────────
    private RectTransform[] blueRects = new RectTransform[4];
    private GameObject[] blueObjects = new GameObject[4];
    private GameObject redObject;

    private List<string> csvRows = new List<string>();
    private int savedCount = 0;
    private bool finished = false;
    private string resolvedUserID;

    private static readonly string[] QuadrantNames =
        { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };

    // ── Drift state ────────────────────────────────────────────────────────────
    private int activeBlueIdx = 0;          // which circle is currently shown
    private Vector2 driftTarget;            // current wander destination
    private float driftTimer = 0f;          // countdown to next target pick
    private Vector2[] quadrantOrigins;      // centre of each quadrant
    private float quadrantHalfW;            // half-width of one quadrant
    private float quadrantHalfH;            // half-height of one quadrant

    // ── Fifth circle (border) state ────────────────────────────────────────────
    //private GameObject fifthObject;
    //private RectTransform fifthRect;
    private bool borderPhase = false;
    private float borderT = 0f;            // 0..1 normalized position along perimeter
    //private float borderTimer = 0f;        // counts up; ends phase when >= borderDuration

    private bool isInitialized = false;

    private float xminShownImage;
    private float yminShownImage;
    private float xmaxShownImage;
    private float ymaxShownImage;

    // ── Unity lifecycle ────────────────────────────────────────────────────────


    private (float, float) delta;


    void Start()
    {
        DontDestroyOnLoad(gameObject);
        ValidateReferences();
        resolvedUserID = ResolveUserID();
        EnsureOutputFolder();

        //redObject = redRect != null ? redRect.gameObject : null;
        if (redObject != null)
        {
            redObject.SetActive(true);
            // Optional: Ensure it's drawn on top of other UI elements
            //redObject.transform.SetAsLastSibling();
        }

        // Wait one frame for Canvas to finish layout before spawning
        StartCoroutine(SpawnAfterLayout());
    }

    public (float,float) GetDelta()
    {
        return delta;
    }
    IEnumerator SpawnAfterLayout()
    {
        yield return null; // wait one frame for Canvas layout

        Debug.Log($"[Calibration] Calibration area size: {calibrationArea.rect.width} x {calibrationArea.rect.height}");

        SpawnBlueCircles();

        // Show only the first blue circle; hide the rest
        activeBlueIdx = 0;
        for (int i = 1; i < 4; i++) blueObjects[i].SetActive(false);

        // Seed the first drift target
        driftTarget = RandomPositionInQuadrant(activeBlueIdx);
        driftTimer = blueDriftInterval;

        UpdateStatus($"Look at a blue circle, then click on the left button. ({savedCount}/4)");

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized || finished) return;

        if (borderPhase)
        {
            float w = calibrationArea.rect.width;
            float h = calibrationArea.rect.height;
            float margin = blueCircleRadius;

            // Calculate actual path length to keep speed consistent
            float pathW = Mathf.Max(0, w - (margin * 2f));
            float pathH = Mathf.Max(0, h - (margin * 2f));
            float perimeter = 2f * (pathW + pathH);

            if (perimeter > 0)
            {
                // Move normalized time forward
                float speed01 = borderSpeed / perimeter;
                borderT += speed01 * Time.deltaTime;

                // Loop the animation (0 to 1)
                if (borderT >= 1f) borderT -= 1f;

                // Move the circle
                //fifthRect.anchoredPosition = BorderPosition(borderT, w, h, margin);
            }

            // End logic
            //if (borderDuration > 0f)
            //{
            //    borderTimer += Time.deltaTime;
            //    if (borderTimer >= borderDuration) EndCalibration();
            //}

            //if (Mouse.current.leftButton.wasPressedThisFrame) EndCalibration();
            //return;
        }

        // ── Drift the active blue circle ───────────────────────────────────────
        driftTimer -= Time.deltaTime;
        if (driftTimer <= 0f)
        {
            driftTarget = RandomPositionInQuadrant(activeBlueIdx);
            driftTimer = blueDriftInterval;
        }

        blueRects[activeBlueIdx].anchoredPosition = Vector2.MoveTowards(
            blueRects[activeBlueIdx].anchoredPosition,
            driftTarget,
            blueDriftSpeed * Time.deltaTime);

        if (Mouse.current.leftButton.wasPressedThisFrame)
            OnLeftClick();
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    void SpawnBlueCircles()
    {
        float w = calibrationArea.rect.width;
        float h = calibrationArea.rect.height;
        quadrantHalfW = w * 0.25f;   // half of one quadrant's width
        quadrantHalfH = h * 0.25f;   // half of one quadrant's height

        Vector2[] positions = GetQuadrantPositions();

        for (int i = 0; i < 4; i++)
        {
            GameObject go = Instantiate(blueCirclePrefab, calibrationArea);
            RectTransform rt = go.GetComponent<RectTransform>();

            rt.sizeDelta = new Vector2(blueCircleRadius * 2f, blueCircleRadius * 2f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = positions[i];

            blueObjects[i] = go;
            blueRects[i] = rt;
        }

        // Store quadrant origins for drift boundary calculations
        float qw = w * 0.5f;
        float qh = h * 0.5f;
        quadrantOrigins = new Vector2[]
        {
            new Vector2(-qw * 0.5f,  qh * 0.5f),   // TopLeft
            new Vector2( qw * 0.5f,  qh * 0.5f),   // TopRight
            new Vector2(-qw * 0.5f, -qh * 0.5f),   // BottomLeft
            new Vector2( qw * 0.5f, -qh * 0.5f),   // BottomRight
        };
    }

    // ── Quadrant logic ─────────────────────────────────────────────────────────

    Vector2[] GetQuadrantPositions()
    {
        float w = calibrationArea.rect.width;
        float h = calibrationArea.rect.height;

        // Safety check
        if (w == 0 || h == 0)
        {
            Debug.LogError("[EyeCalibration] calibrationArea has zero size! Check Canvas layout.");
            return new Vector2[4];
        }

        float qw = w * 0.5f;
        float qh = h * 0.5f;
        float margin = blueCircleRadius + 10f;

        Vector2[] origins = {
            new Vector2(-qw * 0.5f,  qh * 0.5f),   // TopLeft
            new Vector2( qw * 0.5f,  qh * 0.5f),   // TopRight
            new Vector2(-qw * 0.5f, -qh * 0.5f),   // BottomLeft
            new Vector2( qw * 0.5f, -qh * 0.5f),   // BottomRight
        };

        float maxOffsetX = qw * 0.5f - margin;
        float maxOffsetY = qh * 0.5f - margin;

        // Clamp maxOffset so it's never negative (very small calibration areas)
        maxOffsetX = Mathf.Max(0f, maxOffsetX);
        maxOffsetY = Mathf.Max(0f, maxOffsetY);

        Vector2[] result = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            float rx = Random.Range(-maxOffsetX, maxOffsetX);
            float ry = Random.Range(-maxOffsetY, maxOffsetY);
            result[i] = origins[i] + new Vector2(rx, ry);
        }
        return result;
    }

    // ── Click handler ──────────────────────────────────────────────────────────

    void OnLeftClick()
    {


        int nearestIdx = FindNearestBlueCircle();
        if (nearestIdx < 0)
        {
            UpdateStatus("No blue circles remaining!");
            return;
        }

        Vector2 redPos = redRect.position;
        Vector2 bluePos = blueRects[nearestIdx].position;


        //foreach (var ball in blueRects)
        //{
        //    ball.gameObject.SetActive(false);
        //}
        //blueRects[nearestIdx].gameObject.SetActive(true);

        delta.Item1 = redPos.x - bluePos.x;
        delta.Item2 = redPos.y - bluePos.y;

        string quadrant = QuadrantNames[nearestIdx];

        csvRows.Add($"{redPos.x:F4},{redPos.y:F4},{bluePos.x:F4},{bluePos.y:F4},{delta.Item1:F4},{delta.Item2:F4}");

        blueObjects[nearestIdx].SetActive(false);
        savedCount++;

        if (savedCount >= 4)
        {
            EndCalibration();
            GetActiveImageName();
            

            WriteCSV();
            
        }
        else
        {
            // Activate the next quadrant's circle and seed its drift
            activeBlueIdx = savedCount;
            blueObjects[activeBlueIdx].SetActive(true);
            driftTarget = RandomPositionInQuadrant(activeBlueIdx);
            driftTimer = blueDriftInterval;
            UpdateStatus($"Look at the next blue circle. ({savedCount}/4)");
        }
    }

    public string GetActiveImageName()
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
                delta.Item1 = Mathf.Abs(delta.Item1 - xminShownImage) / Mathf.Abs(xmaxShownImage - xminShownImage);
                delta.Item2 = Mathf.Abs(delta.Item2 - yminShownImage) / Mathf.Abs(ymaxShownImage - yminShownImage);

                return child.gameObject.name;
            }

        }

        return "None";
    }


    // ── Helpers ────────────────────────────────────────────────────────────────

    int FindNearestBlueCircle()
    {
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < 4; i++)
        {
            if (!blueObjects[i].activeSelf) continue;
            float d = Vector2.Distance(redRect.anchoredPosition, blueRects[i].anchoredPosition);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // Returns a random position inside the given quadrant, clamped to keep the
    // circle fully within bounds.
    Vector2 RandomPositionInQuadrant(int idx)
    {
        float margin = blueCircleRadius + 10f;
        float maxX = Mathf.Max(0f, quadrantHalfW - margin);
        float maxY = Mathf.Max(0f, quadrantHalfH - margin);

        float rx = Random.Range(-maxX, maxX);
        float ry = Random.Range(-maxY, maxY);
        return quadrantOrigins[idx] + new Vector2(rx, ry);
    }

    // ── Fifth circle – border phase ────────────────────────────────────────────

    //void StartBorderPhase()
    //{
    //    borderPhase = true;
    //    borderT = 0f;
    //    borderTimer = 0f;

    //    // 1. Instantiate the circle
    //    fifthObject = Instantiate(blueCirclePrefab, calibrationArea);
    //    fifthObject.name = "BorderCircle";

    //    // 2. Fix the "Invisible/Behind" issue
    //    fifthObject.transform.SetAsLastSibling();

    //    // 3. Fix the "Big Circle" issue (Reset Scale & Size)
    //    fifthRect = fifthObject.GetComponent<RectTransform>();
    //    fifthRect.sizeDelta = new Vector2(blueCircleRadius * 2f, blueCircleRadius * 2f);

    //    // 4. Align Anchors and Pivot for movement math
    //    fifthRect.anchorMin = new Vector2(0.5f, 0.5f);
    //    fifthRect.anchorMax = new Vector2(0.5f, 0.5f);
    //    fifthRect.pivot = new Vector2(0.5f, 0.5f);

    //    if (redRect != null) redRect.SetAsLastSibling();

    //    //UpdateStatus("Follow the moving circle...");
    //}

    // Maps t ∈ [0,1) to a position on the rectangular border (clockwise, starting top-left).
    //Vector2 BorderPosition(float t, float w, float h, float margin)
    //{
    //    float left = -w * 0.5f + margin;
    //    float right = w * 0.5f - margin;
    //    float top = h * 0.5f - margin;
    //    float bottom = -h * 0.5f + margin;

    //    float segTop = (right - left);           // top edge length
    //    float segRight = (top - bottom);           // right edge length
    //    float segBottom = (right - left);           // bottom edge length
    //    float segLeft = (top - bottom);           // left edge length
    //    float perimeter = segTop + segRight + segBottom + segLeft;

    //    float dist = t * perimeter;

    //    if (dist < segTop)                          // top: left → right
    //        return new Vector2(left + dist, top);

    //    dist -= segTop;
    //    if (dist < segRight)                        // right: top → bottom
    //        return new Vector2(right, top - dist);

    //    dist -= segRight;
    //    if (dist < segBottom)                       // bottom: right → left
    //        return new Vector2(right - dist, bottom);

    //    dist -= segBottom;                          // left: bottom → top
    //    return new Vector2(left, bottom + dist);
    //}

    //void OnBorderClick()
    //{
    //    EndCalibration();
    //}

    public void EndCalibration()
    {
        finished = true;
        //if (redObject != null) redObject.SetActive(false);
        UpdateStatus("Calibration complete!");
    }

    void WriteCSV()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("EyeGazeX,"+ "EyeGazeY,"+ "BlueCircleX,"+"BlueCircleY,"+"DeltaX," +"DeltaY");

        foreach (string row in csvRows)
            sb.AppendLine(row);

        string path = CsvPath();
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[Calibration] CSV written to: {path}");
    }

    // ── Path helpers ───────────────────────────────────────────────────────────

    string OutputFolder()
    {
        // Application.dataPath = .../YourProject/Assets
        // Going one level up gives the project root
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, outputFolderName);
    }

    string CsvPath() =>
        Path.Combine(OutputFolder(), $"{resolvedUserID}_eyecalibration.csv");

    void EnsureOutputFolder()
    {
        string folder = OutputFolder();
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            Debug.Log($"[Calibration] Created output folder: {folder}");
        }
    }

    // ── CSVLogger user ID resolution ───────────────────────────────────────────

    string ResolveUserID()
    {
        GameObject loggerGO = GameObject.Find(csvLoggerObjectName);
        if (loggerGO == null)
        {
            Debug.LogWarning($"[Calibration] '{csvLoggerObjectName}' not found. Using fallback ID.");
            return fallbackUserID;
        }

        foreach (MonoBehaviour mb in loggerGO.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            System.Type type = mb.GetType();

            foreach (string fieldName in new[] { "userID", "userId", "UserID", "UserId", "user_id" })
            {
                var field = type.GetField(fieldName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (field != null && field.FieldType == typeof(string))
                {
                    string value = field.GetValue(mb) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        Debug.Log($"[Calibration] Resolved userID '{value}' from {type.Name}.{fieldName}");
                        return value.Trim();
                    }
                }
            }
        }

        Debug.LogWarning($"[Calibration] No userID field found. Using fallback ID.");
        return fallbackUserID;
    }

    // ── UI & validation ────────────────────────────────────────────────────────

    void UpdateStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    void ValidateReferences()
    {
        if (calibrationArea == null) Debug.LogError("[EyeCalibration] calibrationArea is not assigned!");
        if (blueCirclePrefab == null) Debug.LogError("[EyeCalibration] blueCirclePrefab is not assigned!");
        if (redRect == null) Debug.LogError("[EyeCalibration] redRect is not assigned!");
    }
}