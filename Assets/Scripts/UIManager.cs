using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Canvases — assign in order of flow")]
    public GameObject homeCanvas;
    public GameObject startCalibrationCanvas;
    public GameObject calibrationForwardCanvas;
    public GameObject calibrationBackwardCanvas;
    public GameObject calibrationResultsCanvas;
    public GameObject rxRayImageCanvas;
    public GameObject pauseBeforeChangingAnchorsCanvas;
    public GameObject trialResultsScreenCanvas;
    

    [Header("RX-Ray — drag the 'Images' GameObject here")]
    public Transform rxRayImagesParent;

    [Header("Settings")]
    public float fadeDuration = 0.4f;
    public int imagesBetweenPause = 3; // Pause every N images

    private GameObject[] panels;
    private int currentIndex = 0;
    private bool isTransitioning = false;

    // RX-Ray image cycling
    private List<GameObject> shuffledImages = new List<GameObject>();
    private int currentImageIndex = 0;
    private bool isOnRxRayScreen = false;
    private bool isOnPauseScreen = false;
    private int imagesShownSinceLastPause = 0;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        panels = new GameObject[]
        {
            homeCanvas,                     // 0
            startCalibrationCanvas,         // 1
            calibrationForwardCanvas,       // 2
            calibrationBackwardCanvas,      // 3
            calibrationResultsCanvas,       // 4
            rxRayImageCanvas,                // 5
            pauseBeforeChangingAnchorsCanvas, // 6
            trialResultsScreenCanvas       // 7
            
        };

        HideAll();
        panels[0].SetActive(true);
        currentIndex = 0;
    }

    private void Update()
    {
        if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;

        if (isTransitioning) return;

        if (isOnPauseScreen)
        {
            // Resume from pause — go back to RX-Ray images
            StartCoroutine(ResumeFromPause());
        }
        else if (isOnRxRayScreen)
        {
            ShowNextImage();
        }
        else
        {
            GoToNext();
        }
    }

    // -------------------------------------------------------
    // Screen Navigation
    // -------------------------------------------------------

    private void GoToNext()
    {
        if (currentIndex >= panels.Length - 1) return;

        int nextIndex = currentIndex + 1;
        bool enteringRxRay = panels[nextIndex] == rxRayImageCanvas;

        StartCoroutine(FadeTransition(panels[currentIndex], panels[nextIndex], enteringRxRay));
        currentIndex = nextIndex;
    }

    public void GoToHome()                       => JumpTo(0);
    public void GoToStartCalibration()           => JumpTo(1);
    public void GoToCalibrationForward()         => JumpTo(2);
    public void GoToCalibrationBackward()        => JumpTo(3);
    public void GoToCalibrationResults()         => JumpTo(4);
    public void GoToRXRayImage()                 => JumpTo(5);
    public void GoToPauseBeforeChangingAnchors() => JumpTo(6);
    public void GoToTrialResultsScreen()         => JumpTo(7);
    

    private void JumpTo(int index)
    {
        if (isTransitioning || index == currentIndex) return;
        bool enteringRxRay = panels[index] == rxRayImageCanvas;
        StartCoroutine(FadeTransition(panels[currentIndex], panels[index], enteringRxRay));
        currentIndex = index;
    }

    // -------------------------------------------------------
    // RX-Ray Image Cycling
    // -------------------------------------------------------

    private void SetupRxRayImages()
    {
        shuffledImages.Clear();

        if (rxRayImagesParent == null)
        {
            Debug.LogWarning("UIManager: rxRayImagesParent is not assigned!");
            return;
        }

        foreach (Transform child in rxRayImagesParent)
            shuffledImages.Add(child.gameObject);

        // Fisher-Yates shuffle
        for (int i = shuffledImages.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffledImages[i], shuffledImages[j]) = (shuffledImages[j], shuffledImages[i]);
        }

        foreach (var img in shuffledImages)
            img.SetActive(false);

        currentImageIndex = 0;
        imagesShownSinceLastPause = 0;

        if (shuffledImages.Count > 0)
            shuffledImages[0].SetActive(true);

        Debug.Log($"RX-Ray: {shuffledImages.Count} images shuffled.");
    }

    private void ShowNextImage()
    {
        if (shuffledImages.Count == 0) return;

        // Hide current image
        shuffledImages[currentImageIndex].SetActive(false);
        currentImageIndex++;
        imagesShownSinceLastPause++;

        // All images shown → go to Trial Results
        if (currentImageIndex >= shuffledImages.Count)
        {
            Debug.Log("RX-Ray: All images shown. Going to Trial Results.");
            isOnRxRayScreen = false;
            int trialResultsIndex = System.Array.IndexOf(panels, trialResultsScreenCanvas);
            StartCoroutine(FadeTransition(rxRayImageCanvas, trialResultsScreenCanvas, false));
            currentIndex = trialResultsIndex;
            return;
        }

        // Every N images → show pause screen
        if (imagesShownSinceLastPause >= imagesBetweenPause)
        {
            Debug.Log($"RX-Ray: {imagesBetweenPause} images shown, pausing.");
            isOnRxRayScreen = false;
            isOnPauseScreen = true;
            StartCoroutine(FadeTransition(rxRayImageCanvas, pauseBeforeChangingAnchorsCanvas, false));
            return;
        }

        // Show next image normally
        shuffledImages[currentImageIndex].SetActive(true);
        Debug.Log($"RX-Ray: Showing image {currentImageIndex + 1} of {shuffledImages.Count}");
    }

    private IEnumerator ResumeFromPause()
    {
        isTransitioning = true;
        isOnPauseScreen = false;

        // Fade out pause screen, fade in RX-Ray canvas
        CanvasGroup fromCG = GetOrAddCanvasGroup(pauseBeforeChangingAnchorsCanvas);
        yield return StartCoroutine(Fade(fromCG, 1f, 0f));
        pauseBeforeChangingAnchorsCanvas.SetActive(false);

        rxRayImageCanvas.SetActive(true);
        CanvasGroup toCG = GetOrAddCanvasGroup(rxRayImageCanvas);
        toCG.alpha = 0f;
        yield return StartCoroutine(Fade(toCG, 0f, 1f));

        // Show the next image and reset pause counter
        imagesShownSinceLastPause = 0;
        shuffledImages[currentImageIndex].SetActive(true);
        Debug.Log($"RX-Ray: Resumed, showing image {currentImageIndex + 1} of {shuffledImages.Count}");

        isOnRxRayScreen = true;
        isTransitioning = false;
    }

    // -------------------------------------------------------
    // Fade Transition
    // -------------------------------------------------------

    private IEnumerator FadeTransition(GameObject from, GameObject to, bool enteringRxRay = false)
    {
        isTransitioning = true;
        isOnRxRayScreen = false;

        CanvasGroup fromCG = GetOrAddCanvasGroup(from);
        yield return StartCoroutine(Fade(fromCG, 1f, 0f));
        from.SetActive(false);

        to.SetActive(true);
        CanvasGroup toCG = GetOrAddCanvasGroup(to);
        toCG.alpha = 0f;
        yield return StartCoroutine(Fade(toCG, 0f, 1f));

        if (enteringRxRay)
        {
            SetupRxRayImages();
            isOnRxRayScreen = true;
        }

        isTransitioning = false;
    }

    private IEnumerator Fade(CanvasGroup cg, float from, float to)
    {
        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }

        cg.alpha = to;
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    private void HideAll()
    {
        foreach (var panel in panels)
            if (panel != null) panel.SetActive(false);
    }
}
