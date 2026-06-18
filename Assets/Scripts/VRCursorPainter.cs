using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class VRCursorPainter : MonoBehaviour
{
    [Header("References")]
    public VRCursor vrCursor;
    public UIManager uiManager;

    [Header("Brush Settings")]
    public int brushSize = 5;

    [Header("CSV Output")]
    public string outputFolderName = "PaintingData";
    public string fallbackUserID = "unknown_user";

    private Texture2D paintTexture;
    private SpriteRenderer activeImageRenderer;
    private Sprite originalSprite;
    private bool wasOnEllipseScreen = false;

    private List<string> csvRows = new List<string>();

    // Using standard Stack classes makes state management much cleaner and less bug-prone than LinkedLists
    private Stack<(Color[] pixels, List<string> rows)> undoStack = new Stack<(Color[], List<string>)>();

    private const int MaxUndoSteps = 30;
    private bool isStrokeInProgress = false;
    private List<string> currentStrokeRows = new List<string>();

    private static readonly Dictionary<string, string> ColorLabels = new Dictionary<string, string>
    {
        { "8B0000", "Airway wall thickening" },
        { "FF0000", "Atelectasis" },
        { "DC3132", "Consolidation" },
        { "006400", "Emphysema & High lung volume/emphysema" },
        { "00FF00", "Enlarged cardiac silhouette" },
        { "B2FF59", "Enlarged hilum" },
        { "0000FF", "Fibrosis & Interstitial lung disease" },
        { "4169E1", "Fracture & Acute fracture" },
        { "00FFFF", "Groundglass opacity" },
        { "FFA500", "Hiatal hernia" },
        { "C71585", "Mass" },
        { "82BE08", "Nodule" },
        { "8B4513", "Lung nodule or mass" },
        { "FFAEA9", "Pleural abnormality" },
        { "FFE4C4", "Pleural effusion" },
        { "00BFFF", "Pleural thickening" },
        { "4B0082", "Pneumothorax" },
        { "808000", "Pulmonary edema" },
        { "008B8B", "Quality issue" },
        { "8A2BE2", "Support devices" },
        { "FF00FF", "Wide mediastinum & Abnormal mediastinal contour" },
    };

    private void Update()
    {
        
        if (Mouse.current == null) return;


        if (uiManager == null) return;

        // Reset drawing when leaving ellipse screen
        if (uiManager.trialResultsScreenCanvas.activeSelf)
        {
            WriteCSV();
            ResetPainting();
        }

        if (!uiManager.isOnEllipseScreen) return;

        wasOnEllipseScreen = uiManager.isOnEllipseScreen;

        // 1. CRITICAL GUARD (Standard UI Elements): Check if the mouse is clicking on a regular UI element
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            if (Mouse.current.leftButton.wasReleasedThisFrame && isStrokeInProgress)
            {
                CommitStroke();
            }
            return; // EXIT EARLY.
        }

        // 2. CRITICAL GUARD (World Sprites): Check if cursor is over your custom Undo SpriteRenderer button
        if (vrCursor != null && vrCursor.IsCursorOverUndoButton())
        {
            // If they release the mouse here, clear stroke without polluting snapshots
            if (Mouse.current.leftButton.wasReleasedThisFrame && isStrokeInProgress)
            {
                isStrokeInProgress = false;
                currentStrokeRows.Clear();
            }
            return; // EXIT EARLY. Do not paint over or snapshot while clicking the Undo asset.
        }

        // 3. Normal Painting Input Processing
        if (Mouse.current.leftButton.isPressed)
        {
            TryPaint();
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && isStrokeInProgress)
        {
            CommitStroke();
        }
    }

    void CommitStroke()
    {
        isStrokeInProgress = false;
        if (currentStrokeRows.Count == 0) return;

        // Correctly update the placeholder row list we pushed at the start of the stroke
        if (undoStack.Count > 0)
        {
            var last = undoStack.Pop();
            undoStack.Push((last.pixels, new List<string>(currentStrokeRows)));
        }

        csvRows.AddRange(currentStrokeRows);
        currentStrokeRows.Clear();
    }

    void TryPaint()
    {
        if (uiManager.rxRayImagesParent == null) return;

        SpriteRenderer targetSR = null;
        foreach (Transform child in uiManager.rxRayImagesParent)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            if (sr != null) { targetSR = sr; break; }
        }

        if (targetSR == null) return;
        if (targetSR != activeImageRenderer) SetupPaintTexture(targetSR);

        // This block is now perfectly safe because the UI guard above blocks button clicks
        if (Mouse.current.leftButton.wasPressedThisFrame && !isStrokeInProgress)
        {
            isStrokeInProgress = true;
            currentStrokeRows.Clear();
            SaveUndoSnapshot();
        }
        
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Camera cam = Camera.main;
        float imageZ = activeImageRenderer.transform.position.z;


        Vector3 worldPoint = cam.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, imageZ));
        transform.position = new Vector3(
          worldPoint.x,
          worldPoint.y,
          imageZ);

        //Camera cam = Camera.main;
        //float imageWorldZ = targetSR.transform.position.z;
        //float camSpaceZ = cam.WorldToScreenPoint(new Vector3(0, 0, imageWorldZ)).z;
        //Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, imageWorldZ));



        Bounds bounds = targetSR.bounds;
        float nx = (worldPoint.x - bounds.min.x) / (bounds.max.x - bounds.min.x);
        float ny = (worldPoint.y - bounds.min.y) / (bounds.max.y - bounds.min.y);

        if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return;

        Color paintColor = Color.white;
        if (vrCursor != null)
        {
            SpriteRenderer cursorSR = vrCursor.GetComponent<SpriteRenderer>();
            if (cursorSR != null) paintColor = cursorSR.color;
        }

        ColorUtility.TryParseHtmlString("#C0C0C0", out Color targetColor);
        if (paintColor == targetColor) return;

        int px = Mathf.RoundToInt(nx * paintTexture.width);
        int py = Mathf.RoundToInt(ny * paintTexture.height);

        PaintCircle(px, py, paintColor);
        paintTexture.Apply();

        string hex = ColorToHex(paintColor);
        string label = ColorLabels.TryGetValue(hex, out string found) ? found : "Unknown";
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string imageName = targetSR.gameObject.name;

        string normXStr = nx.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        string normYStr = ny.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

        currentStrokeRows.Add($"{timestamp},{imageName},{normXStr},{normYStr},{hex},{label}");
    }

    void PaintCircle(int cx, int cy, Color color)
    {
        for (int x = -brushSize; x <= brushSize; x++)
        {
            for (int y = -brushSize; y <= brushSize; y++)
            {
                if (x * x + y * y <= brushSize * brushSize)
                {
                    int px = cx + x;
                    int py = cy + y;
                    if (px >= 0 && px < paintTexture.width && py >= 0 && py < paintTexture.height)
                        paintTexture.SetPixel(px, py, color);
                }
            }
        }
    }

    void SetupPaintTexture(SpriteRenderer sr)
    {
        activeImageRenderer = sr;
        originalSprite = sr.sprite;

        Texture2D originalTex = originalSprite.texture;
        paintTexture = new Texture2D(originalTex.width, originalTex.height, TextureFormat.RGBA32, false);
        paintTexture.SetPixels(originalTex.GetPixels());
        paintTexture.Apply();

        sr.sprite = Sprite.Create(
            paintTexture,
            new Rect(0, 0, paintTexture.width, paintTexture.height),
            new Vector2(0.5f, 0.5f),
            originalSprite.pixelsPerUnit
        );
    }

    public void ResetPainting()
    {
        if (activeImageRenderer != null && originalSprite != null)
        {
            activeImageRenderer.sprite = originalSprite;
        }
        paintTexture = null;
        activeImageRenderer = null;
        originalSprite = null;
        undoStack.Clear();
        currentStrokeRows.Clear();
        csvRows.Clear();
        isStrokeInProgress = false;
    }

    void WriteCSV()
    {
        // Flush any in-progress stroke before writing
        if (isStrokeInProgress) CommitStroke();

        if (csvRows.Count == 0) return;

        EnsureOutputFolder();
        string userID = ResolveUserID();
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(OutputFolder(), $"{userID}_painting_{timestamp}.csv");

        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Timestamp,ImageName,NormalizedX,NormalizedY,ColorHex,Label");
            foreach (string row in csvRows)
                sb.AppendLine(row);

            File.WriteAllText(path, sb.ToString());
            //Run the ellipse estimator script right after writing the raw file
            EllipseEstimator.ProcessDrawingToEllipses(path);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRCursorPainter] Failed to write CSV: {e.Message}");
        }

        csvRows.Clear();
    }

    string ColorToHex(Color color)
    {
        Color32 c = color;
        return $"{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    string OutputFolder()
    {

#if UNITY_ANDROID && !UNITY_EDITOR
    return Path.Combine(Application.persistentDataPath, outputFolderName);
#else
        //string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string projectRoot = Path.Combine(Application.dataPath, "..", "PaintingData");
        return Path.GetFullPath(projectRoot);
#endif

    }

    void EnsureOutputFolder()
    {
        string folder = OutputFolder();
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }

    string ResolveUserID()
    {
        CSVLogger logger = FindFirstObjectByType<CSVLogger>();
        if (logger != null && !string.IsNullOrWhiteSpace(logger.userID))
        {
            return logger.userID.Trim();
        }
        return fallbackUserID;
    }

    void SaveUndoSnapshot()
    {
        if (paintTexture == null) return;

        // Cap old undo steps using a temporary list representation to manage sizes cleanly
        undoStack.Push((paintTexture.GetPixels(), new List<string>()));

        if (undoStack.Count > MaxUndoSteps)
        {
            var items = new List<(Color[], List<string>)>(undoStack);
            items.RemoveAt(items.Count - 1); // Drop the oldest item
            items.Reverse();
            undoStack = new Stack<(Color[], List<string>)>(items);
        }
    }

    public void Undo()
    {
        if (isStrokeInProgress)
        {
            isStrokeInProgress = false;
            currentStrokeRows.Clear();
        }

        if (undoStack.Count == 0 || paintTexture == null) return;

        var undoEntry = undoStack.Pop();

        Color[] postPixels = paintTexture.GetPixels();

        // Safely strip the row counts recorded during this undone frame block
        int removeCount = undoEntry.rows.Count;
        if (removeCount > 0 && csvRows.Count >= removeCount)
            csvRows.RemoveRange(csvRows.Count - removeCount, removeCount);

        paintTexture.SetPixels(undoEntry.pixels);
        paintTexture.Apply();
    }
}