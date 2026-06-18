using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class VRCursor : MonoBehaviour
{
    [Header("References")]
    public UIManager uiManager;
    public Transform legendSquaresParent;
    public VRCursorPainter cursorPainter;

    [Header("Settings")]
    public float clickDistanceThreshold = 999f; // max distance to consider, 999 = always pick closest

    [Header("Undo/Redo Buttons")]
    public SpriteRenderer undoButton;

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
        if (!uiManager.isOnEllipseScreen) return;
        if (uiManager == null) return;
        if (Mouse.current == null) return;

        spriteRenderer.enabled = uiManager.isOnEllipseScreen;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();

        //// Use camera unproject — same as the painter does
        Camera cam = Camera.main;
        //transform.position = cam.ScreenToWorldPoint(
        //    new Vector3(mouseScreen.x, mouseScreen.y, uiManager.rxRayImageCanvas.transform.position.z));

        //float cursorWorldZ = transform.position.z;
        //float camDepth = cam.WorldToScreenPoint(new Vector3(0, 0, cursorWorldZ)).z;
        //Vector3 worldPoint = cam.ScreenToWorldPoint(
        //    new Vector3(mouseScreen.x, mouseScreen.y, camDepth));

        //transform.position = new Vector3(
        //    worldPoint.x + cursorOffsetX,
        //    worldPoint.y + cursorOffsetY,
        //    cursorWorldZ);
        
        // Use the active image Z as the depth reference — same as VRCursorPainter
        float imageZ = GetActiveImageZ();

        Vector3 worldPoint = cam.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, imageZ));
        
        transform.position = new Vector3(
           worldPoint.x + cursorOffsetX,
           worldPoint.y + cursorOffsetY,
           imageZ);

        //--------------------

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (IsCursorOverUndoButton())
            {
                cursorPainter?.Undo();
            }
            else if (IsCursorOverImage())
            {
                // painting handled by VRCursorPainter
            }
            else
            {
                TryPickColorUnderCursor();
            }
        }
    }

    // Gets the Z position of the currently active image — same reference as VRCursorPainter
    private float GetActiveImageZ()
    {
        if (uiManager.rxRayImagesParent != null)
        {
            foreach (Transform child in uiManager.rxRayImagesParent)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
                if (sr != null) return sr.transform.position.z;
            }
        }
        // Fallback to canvas Z
        return uiManager.rxRayImageCanvas.transform.position.z;
    }

    public bool IsCursorOverUndoButton()
    {
        if (undoButton == null || !undoButton.gameObject.activeInHierarchy)
            return false;

        Vector2 cursorXY = new Vector2(transform.position.x, transform.position.y);
        Bounds bounds = undoButton.bounds;

        // Check if the cursor position is currently inside the Undo Sprite Renderer bounds
        return (cursorXY.x >= bounds.min.x && cursorXY.x <= bounds.max.x &&
                cursorXY.y >= bounds.min.y && cursorXY.y <= bounds.max.y);
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

    private void TryPickColorUnderCursor()
    {
        if (squares == null || squares.Length == 0) return;

        // Cast a ray from the camera through the cursor's current world position
        Camera cam = Camera.main;
        Vector3 cursorWorld = transform.position;

        // Build a ray from camera origin toward the cursor
        Ray ray = new Ray(cam.transform.position, (cursorWorld - cam.transform.position).normalized);

        Transform closestSquare = null;
        float closestDist = Mathf.Infinity;

        foreach (Transform sq in squares)
        {
            SpriteRenderer squareSR = sq.GetComponent<SpriteRenderer>();
            if (squareSR == null) continue;

            Bounds b = squareSR.bounds;

            // Check if the ray intersects this square's world bounds
            if (b.IntersectRay(ray, out float dist))
            {
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestSquare = sq;
                }
            }
        }

        if (closestSquare != null)
        {
            SpriteRenderer squareSR = closestSquare.GetComponent<SpriteRenderer>();
            Color squareColor = squareSR.color != Color.white
                ? squareSR.color
                : squareSR.sharedMaterial.color;

            lastSquareColor = squareColor;
            spriteRenderer.color = lastSquareColor;
            Debug.Log($"[VRCursor] Color picked from {closestSquare.name}: {lastSquareColor}");
        }
    }
}