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

    // Color hex → label mapping
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
        if (uiManager == null)
        {
            Debug.LogWarning("[VRCursorPainter] uiManager is null!");
            return;
        }

        Debug.Log($"[VRCursorPainter] isOnEllipseScreen: {uiManager.isOnEllipseScreen}");

        // Reset drawing when leaving ellipse screen
        if (uiManager.trialResultsScreenCanvas.activeSelf)
        {
            Debug.Log($"[VRCursorPainter] Leaving ellipse screen. Rows collected: {csvRows.Count}");
            WriteCSV();
            ResetPainting();
        }
        wasOnEllipseScreen = uiManager.isOnEllipseScreen;

        if (!uiManager.isOnEllipseScreen) return;
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.isPressed)
        {
            Debug.Log("[VRCursorPainter] Left button pressed, calling TryPaint.");
            TryPaint();
        }
    }

    void TryPaint()
    {
        if (uiManager.rxRayImagesParent == null)
        {
            Debug.LogWarning("[VRCursorPainter] rxRayImagesParent is null!");
            return;
        }

        SpriteRenderer targetSR = null;
        foreach (Transform child in uiManager.rxRayImagesParent)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            if (sr != null) { targetSR = sr; break; }
        }

        if (targetSR == null)
        {
            Debug.LogWarning("[VRCursorPainter] No active SpriteRenderer found in rxRayImagesParent!");
            return;
        }

        if (targetSR != activeImageRenderer) SetupPaintTexture(targetSR);

        Camera cam = Camera.main;
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        float imageWorldZ = targetSR.transform.position.z;
        float camSpaceZ = cam.WorldToScreenPoint(new Vector3(0, 0, imageWorldZ)).z;
        Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, camSpaceZ));

        Bounds bounds = targetSR.bounds;
        float nx = (worldPoint.x - bounds.min.x) / (bounds.max.x - bounds.min.x);
        float ny = (worldPoint.y - bounds.min.y) / (bounds.max.y - bounds.min.y);

        Debug.Log($"[VRCursorPainter] nx:{nx:F3} ny:{ny:F3}");

        if (nx < 0 || nx > 1 || ny < 0 || ny > 1)
        {
            Debug.LogWarning($"[VRCursorPainter] Out of bounds! nx:{nx:F3} ny:{ny:F3}");
            return;
        }

        Color paintColor = Color.white;
        if (vrCursor != null)
        {
            SpriteRenderer cursorSR = vrCursor.GetComponent<SpriteRenderer>();
            if (cursorSR != null) paintColor = cursorSR.color;
        }

        Debug.Log($"[VRCursorPainter] paintColor: {paintColor}");
        ColorUtility.TryParseHtmlString("#C0C0C0", out Color targetColor);
        if (paintColor == targetColor)
        {
            Debug.LogWarning("[VRCursorPainter] Paint color is white — skipping. Select a color first!");
            return;
        }

        int px = Mathf.RoundToInt(nx * paintTexture.width);
        int py = Mathf.RoundToInt(ny * paintTexture.height);

        PaintCircle(px, py, paintColor);
        paintTexture.Apply();

        string hex = ColorToHex(paintColor);
        string label = ColorLabels.TryGetValue(hex, out string found) ? found : "Unknown";
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string imageName = targetSR.gameObject.name;

        csvRows.Add($"{timestamp},{imageName},{nx},{ny},{hex},{label}");
        Debug.Log($"[VRCursorPainter] Row added. Total rows: {csvRows.Count}");
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
                    if (px >= 0 && px < paintTexture.width &&
                        py >= 0 && py < paintTexture.height)
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
    }

    // ── CSV ────────────────────────────────────────────────────────────────────

    void WriteCSV()
    {
        if (csvRows.Count == 0)
        {
            Debug.LogWarning("[VRCursorPainter] No rows to write.");
            return;
        }

        EnsureOutputFolder();

        string userID = ResolveUserID();
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(OutputFolder(), $"{userID}_painting_{timestamp}.csv");

        Debug.Log($"[VRCursorPainter] Writing {csvRows.Count} rows to: {path}");

        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Timestamp,ImageName,PixelX,PixelY,ColorHex,Label");
            foreach (string row in csvRows)
                sb.AppendLine(row);

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[VRCursorPainter] CSV written successfully.");
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
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, outputFolderName);
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
            Debug.Log($"[VRCursorPainter] Found userID: {logger.userID}");
            return logger.userID.Trim();
        }

        Debug.LogWarning("[VRCursorPainter] CSVLogger not found or userID empty, using fallback.");
        return fallbackUserID;
    }
}