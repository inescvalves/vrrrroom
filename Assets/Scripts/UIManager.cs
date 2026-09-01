using Oculus.Interaction.Samples;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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
    public GameObject analysisConcludedConfirmation;
    public GameObject ellipsesLegend;
    public EyeTrackingDisc eyeTrackingDisc;

    [Header("RX-Ray — drag the 'Images' GameObject here")]
    public Transform rxRayImagesParent;

    [Header("Settings")]
    public float fadeDuration = 0.4f;
    public int imagesBetweenPause = 3;
    public bool pauseBetweenModalities;

    // -------------------------------------------------------
    // ANALYSIS TIMER (NEW)
    // -------------------------------------------------------
    [Header("Analysis Timer")]
    [Tooltip("Enable the countdown that limits the analysis (search) phase of each image.")]
    public bool useAnalysisTimer = true;

    [Tooltip("Length of the analysis phase, in seconds. Protocol default: 120 s (2 minutes).")]
    public float analysisTimeLimit = 120f;

    /// <summary>Raised when the analysis phase of an image ends.
    /// (imageIndex, secondsSpent, endedByTimeout)</summary>
    public event Action<int, float, bool> OnAnalysisPhaseEnded;

    private float analysisTimeRemaining;
    private bool analysisTimerRunning;
    private float analysisElapsed;          // accumulated search time for the current image
    private bool analysisTimerExpired;      // guards against double-firing on the same image

    [Header("Managers")]
    public CanvasHeadsetAligner calibrationManager;

    public GameObject analysisText;
    public GameObject diagnosisText;
    public GameObject headCalibrationText;
    public GameObject eyeCalibrationText;

    public GameObject eyeCalibrationCanvas;
    public GameObject headCalibrationCanvas;

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
    private bool isOnAnalysisConfirmationScreen = false;
    public bool isOnEllipseScreen = false;

    public bool training;

    private bool firstImageShown = false;

    public Transform vrCursorRect;
    private GameObject vrCursorInstance;
    private Camera mainCamera;

    private EyeCalibration eyeCalibration;

    private float rxRayZMin = 1f;
    private float rxRayZScrollSpeed = 120f;
    private float newZ = 1.5f;

    private bool eyeCalibrationFinish = false;


    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        mainCamera = Camera.main;
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
            analysisConcludedConfirmation,
            trialResultsScreenCanvas            // 8
        };

        HideAll();
        panels[0].SetActive(true);
        currentIndex = 0;
        vrCursorRect.gameObject.SetActive(false);

        eyeCalibration = FindFirstObjectByType<EyeCalibration>();
        if (training)
        {
            diagnosisText.SetActive(false);
            analysisText.SetActive(false);
            headCalibrationText.SetActive(false);
            eyeCalibrationText.SetActive(false);
        }
    }

    private void Update()
    {
        if (Mouse.current == null) return;
        if (isTransitioning) return;

        // Countdown ticks only while the participant is actually in the analysis phase.
        TickAnalysisTimer();
        if (isTransitioning) return;   // the timer may have just fired and started a transition

        bool rightPressed = Mouse.current.rightButton.wasPressedThisFrame;
        bool leftPressed = Mouse.current.leftButton.wasPressedThisFrame;

        // Block right-click while eye calibration is still running
        if (eyeCalibration != null && !eyeCalibration.finished && eyeCalibration.imageRXRay.activeSelf)
            rightPressed = false;

        if (isOnEllipseScreen)
            UpdateVRCursor();

        if (isOnConfirmationScreen)
        {
            if (training)
            {
                diagnosisText.SetActive(false);
                analysisText.SetActive(false);
                headCalibrationText.SetActive(false);
            }
            if (rightPressed) { isOnEllipseScreen = false; ConfirmNextImage(); }
            else if (leftPressed) StartCoroutine(GoBackToEllipseRoutine());
        }
        else if (isOnAnalysisConfirmationScreen)
        {
            if (training)
            {
                diagnosisText.SetActive(false);
                analysisText.SetActive(false);
                headCalibrationText.SetActive(false);
            }
            if (rightPressed) { UpdateRxRayCanvasZ(calibrationManager.distanceFromHead); newZ = calibrationManager.distanceFromHead; isOnEllipseScreen = true; GoBackToPrevImage(); }
            else if (leftPressed) { isOnEllipseScreen = false; GoBackToPrevImage(); }
        }
        else if (isOnRxRayScreen && isOnEllipseScreen)
        {
            if (training)
            {
                diagnosisText.SetActive(true);
                analysisText.SetActive(false);
                headCalibrationText.SetActive(false);
            }
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                newZ += scroll * rxRayZScrollSpeed;
                newZ = Mathf.Clamp(newZ, rxRayZMin, calibrationManager.distanceFromHead);
                UpdateRxRayCanvasZ(newZ);
            }

            if (rightPressed)
            {
                vrCursorRect.gameObject.SetActive(false);
                StartCoroutine(FadeToConfirmation());
                UpdateRxRayCanvasZ(calibrationManager.distanceFromHead);
            }

        }
        else if (isOnRxRayScreen && !isOnEllipseScreen)
        {

            if (training && !eyeCalibrationCanvas.activeSelf)
            {
                if (headCalibrationCanvas.activeSelf)
                {
                    headCalibrationText.SetActive(true);
                    diagnosisText.SetActive(false);
                    analysisText.SetActive(false);
                }
                else
                {
                    headCalibrationText.SetActive(false);
                    diagnosisText.SetActive(false);
                    analysisText.SetActive(true);
                }
            }
            else if (training && eyeCalibrationCanvas.activeSelf)
            {
                headCalibrationText.SetActive(false);
                diagnosisText.SetActive(false);
                analysisText.SetActive(false);
                if (eyeCalibration.finished == true && eyeCalibrationFinish == false)
                {
                    eyeCalibrationText.SetActive(true);
                    eyeCalibrationFinish = true;
                }
            }
            if (rightPressed)
            {
                // Manual end of the analysis phase — unchanged behaviour:
                // goes to the "analysis concluded" confirmation message.
                EndAnalysisTimer(false);
                ShowNextImage();
                eyeCalibrationText.SetActive(false);
            }
        }
        else if (isOnPauseScreen)
        {
            if (training)
            {
                headCalibrationText.SetActive(false);
                diagnosisText.SetActive(false);
                analysisText.SetActive(false);
                eyeCalibrationText.SetActive(false);
            }
            if (rightPressed) StartCoroutine(ResumeFromPause());
        }
        else
        {
            if (training)
            {
                headCalibrationText.SetActive(false);
                diagnosisText.SetActive(false);
                analysisText.SetActive(false);
                eyeCalibrationText.SetActive(false);
            }
            if (rightPressed) GoToNext();
        }
    }

    // -------------------------------------------------------
    // Analysis Timer (NEW)
    // -------------------------------------------------------

    /// <summary>Starts (or resumes) the analysis countdown for the current image.</summary>
    /// <param name="reset">true = fresh image, restart at analysisTimeLimit;
    /// false = participant returned to the same image, keep the remaining time.</param>
    private void StartAnalysisTimer(bool reset = true)
    {
        if (!useAnalysisTimer) return;

        if (reset)
        {
            analysisTimeRemaining = analysisTimeLimit;
            analysisElapsed = 0f;
            analysisTimerExpired = false;
        }

        if (analysisTimerExpired) return;

        analysisTimerRunning = true;
    }

    /// <summary>Pauses the countdown without closing the phase (fades, confirmations, pauses).
    /// The countdown is silent and invisible to the participant — nothing is displayed at any point.</summary>
    private void PauseAnalysisTimer()
    {
        analysisTimerRunning = false;
    }

    /// <summary>Closes the analysis phase for the current image and reports the search time.</summary>
    private void EndAnalysisTimer(bool byTimeout)
    {
        if (!analysisTimerRunning && !byTimeout && analysisElapsed <= 0f) return;

        analysisTimerRunning = false;

        OnAnalysisPhaseEnded?.Invoke(currentImageIndex, analysisElapsed, byTimeout);
        Debug.Log($"RX-Ray: analysis phase of image {currentImageIndex + 1} ended after " +
                  $"{analysisElapsed:F2}s ({(byTimeout ? "TIMER EXPIRED" : "participant right-click")}).");
    }

    private void TickAnalysisTimer()
    {
        if (!useAnalysisTimer || !analysisTimerRunning) return;

        // Only count while genuinely in the analysis phase of an image.
        if (!isOnRxRayScreen || isOnEllipseScreen || isOnConfirmationScreen || isOnAnalysisConfirmationScreen || isOnPauseScreen)
            return;

        // Do not count while eye-tracking calibration is still running on the RX-Ray canvas.
        if (eyeCalibration != null && !eyeCalibration.finished && eyeCalibration.imageRXRay != null && eyeCalibration.imageRXRay.activeSelf)
            return;

        analysisTimeRemaining -= Time.deltaTime;
        analysisElapsed += Time.deltaTime;

        if (analysisTimeRemaining <= 0f)
        {
            analysisTimeRemaining = 0f;
            analysisTimerExpired = true;
            EndAnalysisTimer(true);
            StartCoroutine(AutoEnterEllipseRoutine());
        }
    }

    /// <summary>
    /// Timer expiry path: skip the "analysis concluded" confirmation and drop the participant
    /// straight into the annotation (ellipse) phase on the SAME image, with no fade, so the
    /// image never disappears and nothing is lost.
    /// </summary>
    private IEnumerator AutoEnterEllipseRoutine()
    {
        isTransitioning = true;

        eyeTrackingDisc?.SetActive(false);
        isOnEllipseScreen = true;
        isOnRxRayScreen = true;

        if (calibrationManager != null)
        {
            UpdateRxRayCanvasZ(calibrationManager.distanceFromHead);
            newZ = calibrationManager.distanceFromHead;
        }

        SetEllipsesLegend(true);

        Debug.Log($"RX-Ray: timer expired on image {currentImageIndex + 1} — auto-entering annotation phase.");

        yield return null;
        isTransitioning = false;
    }

    private void UpdateVRCursor()
    {
        if (vrCursorRect == null || mainCamera == null) return;

        Canvas canvas = rxRayImageCanvas.GetComponent<Canvas>();
        if (canvas == null) return;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            Mouse.current.position.ReadValue(),
            canvas.worldCamera != null ? canvas.worldCamera : mainCamera,
            out Vector2 localPoint
        );

        // Convert local canvas point to world position, slightly in front of canvas
        Vector3 worldPoint = canvas.transform.TransformPoint(new Vector3(localPoint.x, localPoint.y, 0f));
        vrCursorRect.transform.position = worldPoint + canvas.transform.forward * -0.01f;
    }

    public void RefreshCurrentImagePosition()
    {
        if (rxRayImagesParent == null) return;

        foreach (Transform child in rxRayImagesParent)
        {
            if (!child.gameObject.activeInHierarchy) continue;

            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.enabled = false;
                sr.enabled = true;
            }
        }

        VRCursor cursor = FindFirstObjectByType<VRCursor>();
        cursor?.RefreshSquares();
    }

    public void UpdateRxRayCanvasZ(float distance)
    {
        if (rxRayImageCanvas == null || mainCamera == null) return;

        Transform cam = mainCamera.transform;
        Vector3 fwdFlat = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;

        // place it 'distance' in front of the head, keep its current height
        Vector3 target = cam.position + fwdFlat * distance;
        target.y = rxRayImageCanvas.transform.position.y;   // preserve vertical offset
        rxRayImageCanvas.transform.position = target;

        // keep it facing you so it never shows its mirrored back face
        rxRayImageCanvas.transform.rotation = Quaternion.LookRotation(fwdFlat, Vector3.up);

        // Refresh sprite bounds and cursor squares after move
        RefreshCurrentImagePosition();
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
                int j = UnityEngine.Random.Range(0, i + 1);
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
        firstImageShown = false;
    }

    private void ShowNextImage()
    {
        if (shuffledImages.Count == 0) return;

        if (training && !firstImageShown)
        {
            firstImageShown = true;
            StartCoroutine(AdvanceImageDirectly());  // first image: skip all confirmations
        }
        else
            StartCoroutine(FadeToAnalysisConfirmation());  // all others: normal flow
    }

    // Fade out the current image and show the confirmation screen.
    private IEnumerator FadeToConfirmation()
    {
        isTransitioning = true;
        isOnAnalysisConfirmationScreen = false;
        PauseAnalysisTimer();
        SetEllipsesLegend(false);
        if (currentImageIndex < shuffledImages.Count && shuffledImages[currentImageIndex].activeSelf)
        {
            CanvasGroup imgCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
            yield return StartCoroutine(Fade(imgCG, 1f, 0f));
            shuffledImages[currentImageIndex].SetActive(false);
        }

        // Fade in next image confirmation
        nextImageConfirmation.SetActive(true);
        CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
        confirmCG.alpha = 0f;
        yield return StartCoroutine(Fade(confirmCG, 0f, 1f));

        isOnConfirmationScreen = true;
        isTransitioning = false;
    }

    private IEnumerator FadeToAnalysisConfirmation()
    {
        isTransitioning = true;
        isOnRxRayScreen = false;
        PauseAnalysisTimer();
        SetEllipsesLegend(isOnEllipseScreen);
        CanvasGroup currentCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        yield return StartCoroutine(Fade(currentCG, 1f, 0f));
        shuffledImages[currentImageIndex].SetActive(false);

        // Show confirmation canvas
        rxRayImageCanvas.SetActive(true);   // keep the parent canvas active as backdrop
        analysisConcludedConfirmation.SetActive(true);
        CanvasGroup confirmCG = GetOrAddCanvasGroup(analysisConcludedConfirmation);
        confirmCG.alpha = 0f;
        yield return StartCoroutine(Fade(confirmCG, 0f, 1f));

        isOnAnalysisConfirmationScreen = true;

        isTransitioning = false;
    }

    private IEnumerator GoBackToEllipseRoutine()
    {
        eyeTrackingDisc?.SetActive(false);

        isTransitioning = true;
        isOnConfirmationScreen = false;

        CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
        yield return StartCoroutine(Fade(confirmCG, 1f, 0f));
        nextImageConfirmation.SetActive(false);

        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup imgCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        imgCG.alpha = 0f;
        yield return StartCoroutine(Fade(imgCG, 0f, 1f));
        SetEllipsesLegend(true);
        // Ellipse image is already visible behind — just restore state
        isOnEllipseScreen = true;
        isOnRxRayScreen = true;

        isTransitioning = false;
    }

    private IEnumerator AdvanceImageDirectly()
    {
        isTransitioning = true;
        isOnRxRayScreen = false;
        PauseAnalysisTimer();

        // Fade out current image
        CanvasGroup currentCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        yield return StartCoroutine(Fade(currentCG, 1f, 0f));
        shuffledImages[currentImageIndex].SetActive(false);

        currentImageIndex++;
        imagesShownSinceLastPause++;

        // All images shown -> go to Trial Results
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
        StartAnalysisTimer();               // fresh 2 minutes for the new image
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

        // All images shown -> go to Trial Results
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
        eyeTrackingDisc?.SetActive(true);
        SetEllipsesLegend(false);
        StartAnalysisTimer();               // fresh 2 minutes for the new image
        isTransitioning = false;
    }

    // Left-click on confirmation: go back to the image that was just dismissed.
    private void GoBackToPrevImage()
    {
        StartCoroutine(GoBackToPrevImageRoutine());
    }

    private IEnumerator GoBackToPrevImageRoutine()
    {
        eyeTrackingDisc?.SetActive(!isOnEllipseScreen);
        SetEllipsesLegend(isOnEllipseScreen);
        isTransitioning = true;
        isOnConfirmationScreen = false;
        isOnAnalysisConfirmationScreen = false;

        // Fade out whichever confirmation is active
        if (nextImageConfirmation.activeSelf)
        {
            CanvasGroup confirmCG = GetOrAddCanvasGroup(nextImageConfirmation);
            yield return StartCoroutine(Fade(confirmCG, 1f, 0f));
            nextImageConfirmation.SetActive(false);
        }

        if (analysisConcludedConfirmation.activeSelf)
        {
            CanvasGroup analysisCG = GetOrAddCanvasGroup(analysisConcludedConfirmation);
            yield return StartCoroutine(Fade(analysisCG, 1f, 0f));
            analysisConcludedConfirmation.SetActive(false);
        }

        // Restore the current image
        shuffledImages[currentImageIndex].SetActive(true);
        CanvasGroup imgCG = GetOrAddCanvasGroup(shuffledImages[currentImageIndex]);
        imgCG.alpha = 0f;
        yield return StartCoroutine(Fade(imgCG, 0f, 1f));

        Debug.Log($"RX-Ray: Back to image {currentImageIndex + 1} of {shuffledImages.Count}");
        isOnRxRayScreen = true;

        // Participant chose to keep looking at the SAME image: resume the countdown with the
        // time that was left, never a fresh 2 minutes.
        if (!isOnEllipseScreen)
            StartAnalysisTimer(false);

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
        StartAnalysisTimer();               // new image after the pause
        isTransitioning = false;
    }

    // -------------------------------------------------------
    // Fade Transition
    // -------------------------------------------------------

    private IEnumerator FadeTransition(GameObject from, GameObject to, bool enteringRxRay = false)
    {
        isTransitioning = true;
        isOnRxRayScreen = false;
        PauseAnalysisTimer();

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
            eyeTrackingDisc?.SetActive(true);
            SetEllipsesLegend(false);

            if (calibrationManager != null)
            {
                float distance = PlayerPrefs.GetFloat("CalibratedDistance", 1.5f);
                calibrationManager.SetDistanceFromHead(distance);
                calibrationManager.Recenter();
            }

            // First image of the block. TickAnalysisTimer() holds the countdown at 2:00
            // until eye-tracking calibration reports finished.
            StartAnalysisTimer();
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

    private void SetEllipsesLegend(bool active)
    {
        ellipsesLegend.SetActive(active);
        if (vrCursorRect != null)
            vrCursorRect.gameObject.SetActive(active);

#if !UNITY_ANDROID
                    UnityEngine.Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
                    UnityEngine.Cursor.visible = false;
#endif

        // Recenter the RX-Ray canvas in front of the headset when entering ellipse screen
        if (active && calibrationManager != null)
            calibrationManager.Recenter();
    }
}