using UnityEngine;

/// <summary>
/// Time-fading gaze heatmap, driven purely by the Circle cursor's position and
/// drawn on the active CXR sprite.
///
/// Reads gazeCursor.position each frame, converts it to the active image's local
/// 0..1 coords (so it's automatically clipped to that image), accumulates a
/// fading blob, and shows it on a dedicated heatmap sprite fitted to the image.
///
/// IMPORTANT for VR: put the heatmapRenderer object on a layer BOTH eye cameras
/// render (e.g. the same layer as the CXR images) — NOT the 'Gaze' layer, or it
/// will appear in only one eye.
/// </summary>
[DefaultExecutionOrder(100)]
public class GazeHeatmap : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Circle cursor that sits on the image.")]
    public Transform gazeCursor;

    [Tooltip("SpriteRenderer of the image shown now. Empty = auto-detect from imagesRoot.")]
    public SpriteRenderer activeImage;

    [Tooltip("Parent of the image sprites ('Images'), used to auto-detect the active one.")]
    public Transform imagesRoot;

    [Tooltip("Dedicated MeshRenderer (a Quad) that displays the heatmap (material = GazeHeatmap/DisplayURP).")]
    public Renderer heatmapRenderer;

    [Header("Heatmap buffer")]
    public int resolution = 512;

    [Header("Appearance")]
    public float fadeSeconds = 3f;
    [Range(0.005f, 0.2f)] public float brushRadius = 0.04f;
    [Range(0.05f, 5f)] public float intensity = 1.2f;
    public Gradient colorGradient = DefaultGradient();
    public bool clearOnImageChange = true;

    RenderTexture _accum, _temp;
    Material _stampMat;
    Texture2D _gradientTex;
    SpriteRenderer _lastImage;
    static readonly int HeatTex = Shader.PropertyToID("_HeatTex");
    static readonly int GradientTex = Shader.PropertyToID("_GradientTex");

    void Start()
    {
        _accum = NewRT(); _temp = NewRT(); Clear(_accum);
        var stampShader = Shader.Find("Hidden/GazeHeatmapStamp");
        if (stampShader == null)
        {
            Debug.LogError("GazeHeatmap: shader 'Hidden/GazeHeatmapStamp' not found. " +
                "Create the file GazeHeatmapStamp.shader in Assets (see instructions), " +
                "then add it to Project Settings > Graphics > Always Included Shaders. Disabled.");
            enabled = false;
            return;
        }
        _stampMat = new Material(stampShader);
        BakeGradient();
        if (heatmapRenderer != null)
        {
            heatmapRenderer.material.SetTexture(HeatTex, _accum);
            heatmapRenderer.material.SetTexture(GradientTex, _gradientTex);
        }
    }

    void LateUpdate()
    {
        if (!_visible || _stampMat == null) return;
        var img = ResolveActiveImage();
        if (img == null || img.sprite == null || gazeCursor == null) return;

        if (img != _lastImage)
        {
            if (clearOnImageChange) Clear(_accum);
            _lastImage = img;
        }

        // Circle world position -> active sprite local 0..1 (auto-clipped to the image).
        Bounds b = img.sprite.bounds;
        Vector3 local = img.transform.InverseTransformPoint(gazeCursor.position);
        Vector2 uv = new Vector2(
            Mathf.InverseLerp(b.min.x, b.max.x, local.x),
            Mathf.InverseLerp(b.min.y, b.max.y, local.y));
        bool inside = uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        float decay = Mathf.Exp(-Time.deltaTime * (4f / Mathf.Max(fadeSeconds, 0.01f)));
        _stampMat.SetFloat("_Decay", decay);
        _stampMat.SetFloat("_Radius", brushRadius);
        _stampMat.SetFloat("_Intensity", inside ? intensity * Time.deltaTime : 0f);
        _stampMat.SetVector("_Uv", new Vector4(uv.x, uv.y, 0, 0));
        _stampMat.SetFloat("_Stamp", inside ? 1f : 0f);
        Graphics.Blit(_accum, _temp, _stampMat);
        (_accum, _temp) = (_temp, _accum);

        if (heatmapRenderer != null)
        {
            heatmapRenderer.material.SetTexture(HeatTex, _accum);
            FitTo(img);
        }
    }

    void FitTo(SpriteRenderer img)
    {
        // Fit a unit Quad (1x1 local, centered) to the image's world bounds.
        var t = heatmapRenderer.transform;
        t.rotation = img.transform.rotation;
        // Slightly toward the viewer so it draws over the image, not z-fighting.
        t.position = img.bounds.center - img.transform.forward * 0.002f;
        Vector3 size = img.bounds.size;                 // world size of the image
        t.localScale = new Vector3(size.x, size.y, 1f); // Quad mesh is 1x1, so size == scale
    }

    SpriteRenderer ResolveActiveImage()
    {
        if (activeImage != null && activeImage.enabled &&
            activeImage.gameObject.activeInHierarchy) return activeImage;
        if (imagesRoot != null)
            for (int i = 0; i < imagesRoot.childCount; i++)
            {
                var c = imagesRoot.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                var sr = c.GetComponent<SpriteRenderer>();
                if (sr != null && sr.enabled && sr.sprite != null) return sr;
            }
        return activeImage;
    }

    RenderTexture NewRT()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        rt.Create();
        return rt;
    }

    void Clear(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;
    }

    void BakeGradient()
    {
        const int n = 256;
        _gradientTex = new Texture2D(n, 1, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < n; i++)
            _gradientTex.SetPixel(i, 0, colorGradient.Evaluate(i / (float)(n - 1)));
        _gradientTex.Apply();
    }

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(0f,0f,0.5f), 0.00f),
                new GradientColorKey(new Color(0f,0.4f,1f), 0.25f),
                new GradientColorKey(new Color(0f,1f,0.3f), 0.50f),
                new GradientColorKey(new Color(1f,1f,0f),   0.75f),
                new GradientColorKey(new Color(1f,0f,0f),   1.00f),
            },
            new[] {
                new GradientAlphaKey(0f,   0.00f),
                new GradientAlphaKey(0.6f, 0.15f),
                new GradientAlphaKey(0.9f, 1.00f),
            });
        return g;
    }


    // ---- Visibility API: call from UIManager ----
    bool _visible = true;

    /// <summary>Show or hide the heatmap. When hiding, optionally wipe accumulated heat
    /// so the next analysis phase starts clean.</summary>
    public void SetVisible(bool visible, bool clearOnHide = true)
    {
        _visible = visible;
        if (heatmapRenderer != null) heatmapRenderer.enabled = visible;
        if (!visible && clearOnHide && _accum != null) Clear(_accum);
    }


    void OnDestroy()
    {
        if (_accum) _accum.Release();
        if (_temp) _temp.Release();
        if (_stampMat) Destroy(_stampMat);
        if (_gradientTex) Destroy(_gradientTex);
    }
}