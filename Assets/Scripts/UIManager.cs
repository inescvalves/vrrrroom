using Oculus.Interaction.Samples;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
    public GameObject nextImageConfirmation;

    [Header("RX-Ray — drag the 'Images' GameObject here")]
    public Transform rxRayImagesParent;

    [Header("Settings")]
    public float fadeDuration = 0.4f;
    public int imagesBetweenPause = 3;
    public bool pauseBetweenModalities;

    [Header("Managers")]
    public HeadCalibrationManager calibrationManager;

    private GameObject[] panels;
    private int currentIndex = 0;
    private bool isTransitioning = false;

    // RX-Ray image cycling
    private List<GameObject> shuffledImages = new List<GameObject>();
    private int currentImageIndex = 0;
    private bool isOnRxRayScreen = false;
    private bool isOnPauseScreen = false;
    private bool isOnConfirmationScreen = false;
    private int imagesShownSinceLastPause = 0;

    public bool training;

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
        var test = FindAnyObjectByType<EventSystem>();
        panels = new GameObject[]
        {
            homeCanvas,                         // 0
            //startCalibrationCanvas,             // 1
            //calibrationForwardCanvas,           // 2
            //calibrationBackwardCanvas,          // 3
            //calibrationResultsCanvas,           // 4
            rxRayImageCanvas,                   // 5
            pauseBeforeChangingAnchorsCanvas,   // 6
            nextImageConfirmation,              // 7
            trialResultsScreenCanvas            // 8
        };

        HideAll();
        panels[0].SetActive(true);
        currentIndex = 0;
    }

    private void Update()
    {
        if (Mouse.current == null) return;
        if (isTransitioning) return;

        bool rightPressed = Mouse.current.rightButton.wasPressedThisFrame;
        bool leftPressed = Mouse.current.leftButton.wasPressedThisFrame;

        if (isOnConfirmationScreen)
        {
            if (rightPressed) ConfirmNextImage();       // advance to next image
            else if (leftPressed) GoBackToPrevImage();  // revert to previous image
        }
        else if (isOnPauseScreen)
        {
            if (rightPressed) StartCoroutine(ResumeFromPause());
        }
        else if (isOnRxRayScreen)
        {
            if (rightPressed) ShowNextImage();
        }
        else
        {
            if (rightPressed) GoToNext();
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

    public void GoToHome() => JumpTo(0);
    //public void GoToStartCalibration() => JumpTo(1);
    //public void GoToCalibrationForward() => JumpTo(2);
    //public void GoToCalibrationBackward() => JumpTo(3);
    //public void GoToCalibrationResults() => JumpTo(4);
    public void GoToRXRayImage() => JumpTo(1);
    public void GoToPauseBeforeChangingAnchors() => JumpTo(2);
    public void GoToTrialResultsScreen() => JumpTo(3);

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

        if (training == false)
        {
            for (int i = shuffledImages.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (shuffledImages[i], shuffledImages[j]) = (shuffledImages[j], shuffledImages[i]);
            }
        }

        foreach (var img in shuffledImages)
            img.SetActive(false);

        currentImageIndex = 0;
        imagesShownSinceLastPause = 0;

        if (shuffledImages.Count > 0)
        {
            shuffledImages[0].SetActive(true);
            GetOrAddCanvasGroup(shuffledImages[0]).alpha = 1f;
        }

        Debug.Log($"RX-Ray: {shuffledImages.Count} images ready.");
    }

    private void ShowNextImage()
    {
        if (shuffledImages.Count == 0) return;

        if (training)
            StartCoroutine(AdvanceImageDirectly());
        else
            StartCoroutine(FadeToConfirmation());
    }

    // Fade out the current image and show the confirmation screen.
    private IEnumerator FadeToConfirmation()
    {
        isTransitioning = true;
        isOnRxRayScreen = false;

        CanvasGroup currentCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        yield return StartCoroutine(Fade(currentCG, 1f, 0f));
        shuffledImages[currentImageIndex].SetActive(false);

        // Show confirmation canvas
        rxRayImageCanvas.SetActive(true);   // keep the parent canvas active as backdrop
        nextImageConfirmation.SetActive(true);
        CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
        confirmCG.alpha = 0f;
        yield return StartCoroutine(Fade(confirmCG, 0f, 1f));

        isOnConfirmationScreen = true;
       
        
        isTransitioning = false;
    }

    private IEnumerator AdvanceImageDirectly()
    {
        isTransitioning = true;
        isOnRxRayScreen = false;

        // Fade out current image
        CanvasGroup currentCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        yield return StartCoroutine(Fade(currentCG, 1f, 0f));
        shuffledImages[currentImageIndex].SetActive(false);

        currentImageIndex++;
        imagesShownSinceLastPause++;

        // All images shown → go to Trial Results
        if (currentImageIndex >= shuffledImages.Count)
        {
            FindFirstObjectByType<EyeCalibration>().EndCalibration();
            Debug.Log("RX-Ray (training): All images shown. Going to Trial Results.");
            int trialIdx = System.Array.IndexOf(panels, trialResultsScreenCanvas);
            StartCoroutine(FadeTransition(rxRayImageCanvas, trialResultsScreenCanvas, false));
            currentIndex = trialIdx;
            isTransitioning = false;
            while (!Mouse.current.rightButton.wasPressedThisFrame)
                yield return null;
            if (SceneManager.GetActiveScene().name != "VRRRRoom Static")
                SceneManager.LoadScene("VRRRRoom Static");
            yield break;
        }

        // Pause every N images (respects pauseBetweenModalities even in training if you want)
        if (imagesShownSinceLastPause >= imagesBetweenPause && pauseBetweenModalities)
        {
            Debug.Log($"RX-Ray (training): {imagesBetweenPause} images shown, pausing.");
            isOnPauseScreen = true;
            rxRayImageCanvas.SetActive(false);
            pauseBeforeChangingAnchorsCanvas.SetActive(true);
            CanvasGroup pauseCG = GetOrAddCanvasGroup(pauseBeforeChangingAnchorsCanvas);
            pauseCG.alpha = 0f;
            yield return StartCoroutine(Fade(pauseCG, 0f, 1f));
            isTransitioning = false;
            yield break;
        }

        // Fade in next image directly
        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup nextCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        nextCG.alpha = 0f;
        yield return StartCoroutine(Fade(nextCG, 0f, 1f));

        Debug.Log($"RX-Ray (training): Showing image {currentImageIndex + 1} of {shuffledImages.Count}");
        isOnRxRayScreen = true;
        isTransitioning = false;
    }

    // Right-click on confirmation: advance to the next image (or end/pause).
    private void ConfirmNextImage()
    {
        StartCoroutine(ConfirmNextImageRoutine());
    }

    private IEnumerator ConfirmNextImageRoutine()
    {
        isTransitioning = true;
        isOnConfirmationScreen = false;


        
        // Fade out confirmation
        CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
        yield return StartCoroutine(Fade(confirmCG, 1f, 0f));
        nextImageConfirmation.SetActive(false);
      
        
        currentImageIndex++;
        imagesShownSinceLastPause++;

        // All images shown → go to Trial Results
        if (currentImageIndex >= shuffledImages.Count)
        {
            FindFirstObjectByType<EyeCalibration>().EndCalibration();
            Debug.Log("RX-Ray: All images shown. Going to Trial Results.");
            int trialIdx = System.Array.IndexOf(panels, trialResultsScreenCanvas);
            StartCoroutine(FadeTransition(rxRayImageCanvas, trialResultsScreenCanvas, false));
            currentIndex = trialIdx;
            isTransitioning = false;
            while (!Mouse.current.rightButton.wasPressedThisFrame)
                yield return null;
            if (SceneManager.GetActiveScene().name != "VRRRRoom Static")
                SceneManager.LoadScene("VRRRRoom Static");
            yield break;
        }

        // Pause every N images
        if (imagesShownSinceLastPause >= imagesBetweenPause && pauseBetweenModalities)
        {
            Debug.Log($"RX-Ray: {imagesBetweenPause} images shown, pausing.");
            isOnPauseScreen = true;
            rxRayImageCanvas.SetActive(false);
            pauseBeforeChangingAnchorsCanvas.SetActive(true);
            CanvasGroup pauseCG = GetOrAddCanvasGroup(pauseBeforeChangingAnchorsCanvas);
            pauseCG.alpha = 0f;
            yield return StartCoroutine(Fade(pauseCG, 0f, 1f));
            isTransitioning = false;
            yield break;
        }

        // Show next image
        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup nextCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        nextCG.alpha = 0f;
        yield return StartCoroutine(Fade(nextCG, 0f, 1f));

        Debug.Log($"RX-Ray: Showing image {currentImageIndex + 1} of {shuffledImages.Count}");
        isOnRxRayScreen = true;
        isTransitioning = false;
    }

    // Left-click on confirmation: go back to the image that was just dismissed.
    private void GoBackToPrevImage()
    {
        StartCoroutine(GoBackToPrevImageRoutine());
    }

    private IEnumerator GoBackToPrevImageRoutine()
    {
        isTransitioning = true;
        isOnConfirmationScreen = false;

        // Fade out confirmation
        CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
        yield return StartCoroutine(Fade(confirmCG, 1f, 0f));
        nextImageConfirmation.SetActive(false);

        // Restore the current image (index was NOT incremented yet)
        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup imgCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        imgCG.alpha = 0f;
        yield return StartCoroutine(Fade(imgCG, 0f, 1f));

        Debug.Log($"RX-Ray: Back to image {currentImageIndex + 1} of {shuffledImages.Count}");
        isOnRxRayScreen = true;
        isTransitioning = false;
    }

    private IEnumerator ResumeFromPause()
    {
        isTransitioning = true;
        isOnPauseScreen = false;

        CanvasGroup fromCG = GetOrAddCanvasGroup(pauseBeforeChangingAnchorsCanvas);
        yield return StartCoroutine(Fade(fromCG, 1f, 0f));
        pauseBeforeChangingAnchorsCanvas.SetActive(false);

        rxRayImageCanvas.SetActive(true);
        CanvasGroup toCG = GetOrAddCanvasGroup(rxRayImageCanvas);
        toCG.alpha = 0f;
        yield return StartCoroutine(Fade(toCG, 0f, 1f));

        imagesShownSinceLastPause = 0;
        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup imgCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        imgCG.alpha = 1f;

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
        //if (training == false)
        //{
        //    if (to == startCalibrationCanvas) HeadCalibrationManager.Instance?.OnStartCalibrationEnabled();
        //    else if (to == calibrationForwardCanvas) HeadCalibrationManager.Instance?.OnCalibrationForwardEnabled();
        //    else if (to == calibrationBackwardCanvas) HeadCalibrationManager.Instance?.OnCalibrationBackwardEnabled();
        //    else if (to == calibrationResultsCanvas) HeadCalibrationManager.Instance?.OnCalibrationResultsEnabled();

        //}
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