using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class CircleCalibration : MonoBehaviour
{
    [Header("Canvas & Area")]
    public RectTransform calibrationArea;

    [Header("Prefabs")]
    public GameObject blueCirclePrefab;

    [Header("Red Circle - Eye Tracking")]
    public RectTransform redRect;

    [Header("Circle Sizes")]
    public float blueCircleRadius = 40f;

    [Header("UI References")]
    public TextMeshProUGUI statusText;

    [Header("CSV Logger")]
    public string csvLoggerObjectName = "CSVLogger";
    public string fallbackUserID = "unknown_user";

    [Header("Output")]
    [Tooltip("Folder name created at the root of your Unity project (next to Assets/).")]
    public string outputFolderName = "CalibrationData";

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

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Start()
    {
        ValidateReferences();
        resolvedUserID = ResolveUserID();
        EnsureOutputFolder();

        redObject = redRect != null ? redRect.gameObject : null;

        // Wait one frame for Canvas to finish layout before spawning
        StartCoroutine(SpawnAfterLayout());
    }

    IEnumerator SpawnAfterLayout()
    {
        yield return null; // wait one frame for Canvas layout

        Debug.Log($"[Calibration] Calibration area size: {calibrationArea.rect.width} x {calibrationArea.rect.height}");

        SpawnBlueCircles();
        UpdateStatus($"Look at a blue circle, then click. ({savedCount}/4)");
    }

    void Update()
    {
        if (!finished && Mouse.current.leftButton.wasPressedThisFrame)
            OnLeftClick();
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    void SpawnBlueCircles()
    {
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
    }

    // ── Quadrant logic ─────────────────────────────────────────────────────────

    Vector2[] GetQuadrantPositions()
    {
        float w = calibrationArea.rect.width;
        float h = calibrationArea.rect.height;

        // Safety check
        if (w == 0 || h == 0)
        {
            Debug.LogError("[CircleCalibration] calibrationArea has zero size! Check Canvas layout.");
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

        Vector2 redPos = redRect.anchoredPosition;
        Vector2 bluePos = blueRects[nearestIdx].anchoredPosition;

        float deltaX = redPos.x - bluePos.x;
        float deltaY = redPos.y - bluePos.y;
        float absX = Mathf.Abs(deltaX);
        float absY = Mathf.Abs(deltaY);
        float euclidean = Vector2.Distance(redPos, bluePos);

        string quadrant = QuadrantNames[nearestIdx];

        csvRows.Add($"{quadrant}," +
                    $"{bluePos.x:F4},{bluePos.y:F4}," +
                    $"{redPos.x:F4},{redPos.y:F4}," +
                    $"{deltaX:F4},{deltaY:F4}," +
                    $"{absX:F4},{absY:F4}," +
                    $"{euclidean:F4}");

        Debug.Log($"[Calibration] {quadrant} | " +
                  $"Blue({bluePos.x:F2},{bluePos.y:F2}) " +
                  $"Red({redPos.x:F2},{redPos.y:F2}) | " +
                  $"Δ({deltaX:F2},{deltaY:F2}) " +
                  $"Abs({absX:F2},{absY:F2}) " +
                  $"Dist={euclidean:F2}");

        blueObjects[nearestIdx].SetActive(false);
        savedCount++;

        if (savedCount >= 4)
        {
            WriteCSV();
            finished = true;
            if (redObject != null) redObject.SetActive(false);
            UpdateStatus($"Calibration complete!");
        }
        else
        {
            UpdateStatus($"Look at the next blue circle. ({savedCount}/4)");
        }
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

    void WriteCSV()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Quadrant," +
                      "BlueX,BlueY," +
                      "RedX,RedY," +
                      "DeltaX,DeltaY," +
                      "AbsDistanceX,AbsDistanceY," +
                      "EuclideanDistance");

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
        if (calibrationArea == null) Debug.LogError("[CircleCalibration] calibrationArea is not assigned!");
        if (blueCirclePrefab == null) Debug.LogError("[CircleCalibration] blueCirclePrefab is not assigned!");
        if (redRect == null) Debug.LogError("[CircleCalibration] redRect is not assigned!");
    }
}