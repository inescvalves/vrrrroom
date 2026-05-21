using UnityEngine;
using UnityEngine.InputSystem;

public class VRCursorPainter : MonoBehaviour
{
    [Header("References")]
    public VRCursor vrCursor;
    public UIManager uiManager;

    [Header("Brush Settings")]
    public int brushSize = 5;

    private Texture2D paintTexture;
    private SpriteRenderer activeImageRenderer;
    private Sprite originalSprite;
    private bool wasOnEllipseScreen = false;

    private void Update()
    {
        if (uiManager == null) return;

        // Reset drawing when leaving ellipse screen
        if (wasOnEllipseScreen && !uiManager.isOnEllipseScreen)
        {
            ResetPainting();
        }
        wasOnEllipseScreen = uiManager.isOnEllipseScreen;

        if (!uiManager.isOnEllipseScreen) return;
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.isPressed)
        {
            TryPaint();
        }
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

        // Unproject mouse directly into the world plane of the image
        Camera cam = Camera.main;
        Vector2 mouseScreen = Mouse.current.position.ReadValue();

        // Get the Z depth of the image in screen space
        float imageWorldZ = targetSR.transform.position.z;
        float camSpaceZ = cam.WorldToScreenPoint(
            new Vector3(0, 0, imageWorldZ)).z;

        Vector3 worldPoint = cam.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, camSpaceZ));

        Bounds bounds = targetSR.bounds;

        // Log to verify alignment
        Debug.Log($"Mouse world: {worldPoint.x:F3}, {worldPoint.y:F3} | " +
                  $"Bounds X: {bounds.min.x:F3} to {bounds.max.x:F3} | " +
                  $"Bounds Y: {bounds.min.y:F3} to {bounds.max.y:F3}");

        float nx = (worldPoint.x - bounds.min.x) / (bounds.max.x - bounds.min.x);
        float ny = (worldPoint.y - bounds.min.y) / (bounds.max.y - bounds.min.y);

        if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return;

        int px = Mathf.RoundToInt(nx * paintTexture.width);
        int py = Mathf.RoundToInt(ny * paintTexture.height);

        Color paintColor = Color.white; // fallback
        if (vrCursor != null)
        {
            SpriteRenderer cursorSR = vrCursor.GetComponent<SpriteRenderer>();
            if (cursorSR != null) paintColor = cursorSR.color;
        }
        if (paintColor == Color.white) return;
        PaintCircle(px, py, paintColor);
        paintTexture.Apply();
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
}