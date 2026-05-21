using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class VRCursor : MonoBehaviour
{
    [Header("References")]
    public UIManager uiManager;
    public Transform legendSquaresParent; // drag the Legend GameObject here

    [Header("Settings")]
    public float clickDistanceThreshold = 999f; // max distance to consider, 999 = always pick closest

    [Header("Undo/Redo Buttons")]
    public SpriteRenderer undoButton;
    public SpriteRenderer redoButton;

    private SpriteRenderer spriteRenderer;
    private Transform[] squares;

    private Color lastSquareColor = Color.white;

    private Camera fixedCamera;

    [Header("Cursor Calibration")]
    public float cursorOffsetY = 0f; // tune this in Inspector at runtime
    public float cursorOffsetX = 0f; // in case X also drifts

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        // Collect all Square L children from the legend
        RefreshSquares();
        fixedCamera = Camera.main;
    }

    public void RefreshSquares()
    {
        if (legendSquaresParent == null) return;

        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in legendSquaresParent)
        {
            if (child.name.StartsWith("Square L"))
                list.Add(child);
        }
        squares = list.ToArray();
    }

    private void Update()
    {
        if (uiManager == null) return;
        if (Mouse.current == null) return;

        spriteRenderer.enabled = uiManager.isOnEllipseScreen;
        if (!uiManager.isOnEllipseScreen) return;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();

        // Use camera unproject — same as the painter does
        Camera cam = Camera.main;
        float cursorWorldZ = transform.position.z;
        float camDepth = cam.WorldToScreenPoint(new Vector3(0, 0, cursorWorldZ)).z;
        Vector3 worldPoint = cam.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, camDepth));

        transform.position = new Vector3(
            worldPoint.x + cursorOffsetX,
            worldPoint.y + cursorOffsetY,
            cursorWorldZ);

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (IsCursorOverImage() || IsCursorOverButton())
            {
                // Do nothing
            }
            else
            {
                SnapToClosestSquare();
            }
        }
    }

    private bool IsCursorOverButton()
    {
        Vector2 cursorXY = new Vector2(transform.position.x, transform.position.y);

        if (undoButton != null && undoButton.gameObject.activeInHierarchy)
        {
            Bounds b = undoButton.bounds;
            if (cursorXY.x >= b.min.x && cursorXY.x <= b.max.x &&
                cursorXY.y >= b.min.y && cursorXY.y <= b.max.y)
                return true;
        }

        if (redoButton != null && redoButton.gameObject.activeInHierarchy)
        {
            Bounds b = redoButton.bounds;
            if (cursorXY.x >= b.min.x && cursorXY.x <= b.max.x &&
                cursorXY.y >= b.min.y && cursorXY.y <= b.max.y)
                return true;
        }

        return false;
    }

    private bool IsCursorOverImage()
    {
        if (uiManager.rxRayImagesParent == null) return false;

        Vector2 cursorXY = new Vector2(transform.position.x, transform.position.y);

        foreach (Transform child in uiManager.rxRayImagesParent)
        {
            if (!child.gameObject.activeInHierarchy) continue;

            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            if (sr == null) continue;

            // Check if cursor XY is within the sprite bounds (ignore Z)
            Bounds bounds = sr.bounds;
            if (cursorXY.x >= bounds.min.x && cursorXY.x <= bounds.max.x &&
                cursorXY.y >= bounds.min.y && cursorXY.y <= bounds.max.y)
                return true;
        }

        return false;
    }

    private void SnapToClosestSquare()
    {
        if (squares == null || squares.Length == 0) return;

        Transform closest = null;
        float closestDist = float.MaxValue;
        Vector3 cursorPos = transform.position;

        foreach (Transform sq in squares)
        {
            Vector2 cursorXY = new Vector2(cursorPos.x, cursorPos.y);
            Vector2 squareXY = new Vector2(sq.position.x, sq.position.y);
            float dist = Vector2.Distance(cursorXY, squareXY);

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = sq;
            }
        }

        if (closest == null) return;

        transform.position = new Vector3(closest.position.x, closest.position.y, transform.position.z);

        SpriteRenderer squareSR = closest.GetComponent<SpriteRenderer>();
        if (squareSR != null && spriteRenderer != null)
        {
            // Try SpriteRenderer.color first, fall back to material color
            Color squareColor = squareSR.color != Color.white
                ? squareSR.color
                : squareSR.sharedMaterial.color;

            lastSquareColor = squareColor;
            spriteRenderer.color = lastSquareColor;
            Debug.Log($"[VRCursor] Snapped to {closest.name}, color: {lastSquareColor}");
        }
    }
}