# VRRRRoom

**Virtual reality for radiologists in the reading room**

VRRRRoom is a Unity application for VR headsets with eye tracking. Radiologists read chest X‑rays (CXRs) in a virtual reading room while the app records their gaze, head position, image adjustments and spoken dictation. They then mark the abnormalities they found by painting over the image. The repository also includes a Python pipeline that turns these recordings into **REFLACX‑style** fixations, gaze heatmaps and lesion ellipses, and compares where readers looked with what they annotated.

---

## Table of contents

- [How a session works](#how-a-session-works)
- [Repository structure](#repository-structure)
- [Requirements](#requirements)
- [Getting started (Unity app)](#getting-started-unity-app)
- [Controls](#controls)
- [Data recorded by the app](#data-recorded-by-the-app)
- [Analysis pipeline (Python)](#analysis-pipeline-python)
- [Annotation labels](#annotation-labels)
- [Reference files](#reference-files)
- [Acknowledgements](#acknowledgements)

---

## How a session works

Each participant goes through these steps:

1. **Head / distance calibration.** The participant's preferred viewing distance to the image is measured (default 1.5 m) and saved.
2. **Eye‑tracking calibration.** Targets move across the image area. The offset between measured gaze and each target is recorded and later used to correct the gaze data.
3. **Analysis phase (per image).** A CXR is shown and the participant reads it freely. They can change brightness and contrast and dictate findings aloud. Gaze, head pose, zoom and window level/width are logged continuously, and audio is recorded. By default this phase ends after **120 s**, or earlier if the participant ends it.
4. **Annotation phase (per image).** The participant picks a label from the legend and paints over each abnormality. When the image is done, the strokes are saved and ellipses are fitted to them automatically.
5. Images are shown in random order, with a pause every *N* images (`imagesBetweenPause`, default 3).

There are two scenes:

| Scene | Purpose |
|---|---|
| `Assets/Scenes/VRRRRoom Training.unity` | Practice session with on‑screen instructions (`UIManager.training = true`) |
| `Assets/Scenes/VRRRRoom Static.unity` | Main study session |

Both scenes are in the build settings. `VRRRRoom.unity`, `VRRRRoom Training (Hands).unity` and `Distance Calibration.unity` are development and experimental variants.

---

## Repository structure

```
.
├── Assets/
│   ├── Scripts/                 # Study logic (C#), see table below
│   ├── Scenes/                  # Training, Static (study) and experimental scenes
│   ├── RX-Ray Images/           # Chest X-rays shown during the study (.jpg)
│   ├── Prefabs/, Materials/, Text Fonts/, VRTemplateAssets/, ...
│   └── Plugins/Android/         # AndroidManifest (eye-tracking permission)
├── Packages/                    # Unity package manifest
├── ProjectSettings/
│
├── generate_fixations.py                    # Raw gaze  -> REFLACX-style fixations
├── heatmapsAndEllipses.py                   # Process 1: per-image heatmaps + ellipse overlap (raw gaze)
├── heatmapsAndEllipsesP2.py                 # Process 2: participant-average heatmap + NCC (raw gaze)
├── heatmapsAndEllipsesFixations.py          # Process 1 using fixations
├── heatmapsAndEllipsesFixationsP2.py        # Process 2 using fixations
├── generate_rectangles_and_ellipses.py      # Painting strokes -> axis-aligned boxes/ellipses + metrics CSV
├── ellipses.py                              # Draw fitted ellipses on the CXRs
├── painting.py / checkVRPainter.py          # Redraw raw painting points on the CXRs (for checking)
├── audio_transcript_to_csv.py               # Dictation transcript -> word-level timestamp CSV
├── eye_gaze_visualization.ipynb             # Interactive heatmap + scanpath notebook
│
├── Bounds.csv                   # Displayed size of each image in Unity world units
├── chest_bounding_boxes.csv     # Chest bounding box per CXR (pixels), used by Process 2
├── fixations_output/            # Example fixation tables (User4–User6)
├── EllipsesCSV/                 # Example ellipse / rectangle metrics
└── AudioPostProcessing/         # Example transcripts and word-timestamp CSVs
```

### Unity scripts (`Assets/Scripts`)

| Script | Role |
|---|---|
| `UIManager.cs` | Runs the study: moves between screens, shuffles images, times the analysis phase, handles pauses and switches between analysis and annotation |
| `CSVLogger.cs` | Logs gaze, head pose, zoom and window level/width to CSV |
| `EyeCalibration.cs` | Moving‑target eye‑tracking calibration and gaze offset correction |
| `HeadCalibrationManager.cs` | Measures and stores the participant's viewing distance |
| `CanvasHeadsetAligner.cs` | Places the canvases relative to the headset |
| `EyeTrackingDisc.cs` | Combines both eyes' `OVREyeGaze` into a single gaze cursor |
| `EyeTrackingPermission.cs` | Requests the Quest eye‑tracking permission at runtime |
| `ImageEffectsController.cs` | Brightness (luminance) and contrast adjustment, exposed as window level/width |
| `VRCursor.cs` / `VRCursorPainter.cs` | Annotation cursor, label picking, painting, undo and writing the painting CSV |
| `EllipseEstimator.cs` | Splits strokes and fits an ellipse to each one when a painting is saved |
| `GazeHeatmap.cs` | Live gaze heatmap that fades over time, shown on the active CXR |
| `SpeechRecognitionTest.cs` | Records dictation during the analysis phase and transcribes it with Whisper (Hugging Face Inference API) |

---

## Requirements

### Unity app

- **Unity 6000.3.8f1** (Unity 6.3) with the **Android Build Support** module
- A **Meta Quest headset with eye tracking** (e.g. Quest Pro). Android min SDK 32, target SDK 34
- Main packages (resolved automatically from `Packages/manifest.json`): Meta XR SDK 201, OpenXR 1.16, XR Interaction Toolkit 3.4, XR Hands 1.7, URP 17.3, Input System 1.18, and the Hugging Face Unity API
- A **Hugging Face access token**, only if you want live speech transcription

### Python analysis

- Python **3.10+**

```bash
pip install numpy pandas matplotlib scipy pillow opencv-python scikit-learn
# optional
pip install faster-whisper   # word-level timestamps from audio (audio_transcript_to_csv.py)
pip install pydicom          # load .dcm images in the heatmap scripts / notebook
pip install jupyter          # eye_gaze_visualization.ipynb
```

---

## Getting started (Unity app)

1. **Clone** the repository and open the folder in **Unity Hub** (Add → select the folder). Use Unity 6000.3.8f1.
2. Wait for Unity to import the project and download the packages.
3. Open `Assets/Scenes/VRRRRoom Training.unity` or `VRRRRoom Static.unity`.
4. Select the **CSVLogger** GameObject and set **`userID`** to the participant ID. Every output file is named with this ID.
5. *(Optional, for speech transcription)* Enter your Hugging Face token on the `SpeechRecognitionTest` component or in `Assets/Resources/HuggingFaceAPIConfig.asset`.
   > **Do not commit tokens.** Keep them out of version control, for example by loading them from a local file listed in `.gitignore`.
6. **Run in the Editor** with Quest Link, or **build an APK**: *File → Build Profiles → Android → Switch Platform → Build And Run* with the headset connected over USB.
7. When the app first starts on the headset, allow the **eye‑tracking permission**.

---

## Controls

The study is controlled with a mouse (or an equivalent paired input device):

| Action | Input |
|---|---|
| Go to the next screen / end the analysis of the current image | **Right click** |
| Return to the previous step on a confirmation screen | **Left click** |
| Adjust brightness (analysis phase) | **Hold left button + drag up/down** |
| Adjust contrast (analysis phase) | **Hold left button + drag left/right** |
| Reset brightness and contrast | **Short left click** without moving |
| Move the image closer or farther (annotation phase) | **Scroll wheel** |
| Choose a label / paint a lesion (annotation phase) | **Left click** on the legend / **left click + drag** on the image |
| Undo stroke | Undo button in the annotation legend |

---

## Data recorded by the app

When running in the Editor, files are written to folders next to `Assets/`. On the headset, they are written under the app's `Application.persistentDataPath`, in folders with the same names.

| Folder | File | Contents |
|---|---|---|
| `CSVLoggers/` | `{userID}_{yyyyMMdd_HHmmss}_gaze.csv` | Gaze log (see below) |
| `PaintingData/` | `{userID}_painting_{timestamp}.csv` | Painted points: `Timestamp, ImageName, NormalizedX, NormalizedY, ColorHex, Label` |
| `PaintingData/` | `{userID}_painting_{timestamp}_ellipses.csv` | One fitted (rotated) ellipse per stroke: centre, semi‑axes, angle and bounding box, in image pixels |
| `CalibrationData/` | `{userID}_eyecalibration.csv` | Target positions vs. measured gaze, and the offset between them |
| `CalibrationData/` | `{userID}_distance.csv` | Measured viewing distance |
| `AudioRecording/` | `User{userID}_{timestamp}.wav` / `.txt` | Dictation audio and its Whisper transcript |

These folders are listed in `.gitignore` so that participant data is not committed by accident.

### Gaze log columns

Rows are written only while an image is shown, the study is not paused, and the gaze is on the image.

| Column | Description |
|---|---|
| `UserID`, `Timestamp`, `SessionTime` | Participant ID, wall‑clock time (ms precision), Unity time since start (s) |
| `ImageName` | ID of the CXR on screen |
| `WindowLevel`, `WindowWidth` | Current brightness/contrast settings |
| `ZoomIn`, `ZoomOut` | How far the head has moved toward or away from the calibrated position (m) |
| `HeadGaze*` | Head position (x, y, z) in world space |
| `EyeGazeX`, `EyeGazeY` | Gaze cursor position in world space |
| `EyeGazeX/Y (normalized)` | Gaze in image coordinates `[0,1]` |
| `EyeGazeX/Y (corrected)` | The same, after the eye‑calibration offset is applied |

> Column names in the header have leading spaces. All the Python scripts strip them when reading the file.

---

## Analysis pipeline (Python)

Most scripts are configured by editing the **`USER SETTINGS`** block at the top of the file (input CSV, image folder, output folder). `generate_fixations.py`, `generate_rectangles_and_ellipses.py` and `audio_transcript_to_csv.py` take command‑line arguments instead. Run all scripts **from the repository root** so the relative paths (`Assets/RX-Ray Images`, `Bounds.csv`, …) work.

```
gaze.csv ──► generate_fixations.py ──► {user}_fixations.csv ─┐
    │                                                        ├─► heatmapsAndEllipses*.py  (P1: per image)
    └────────────────────────────────────────────────────────┤
painting.csv ──► generate_rectangles_and_ellipses.py ────────┘   heatmapsAndEllipses*P2.py (P2: participant average + NCC)
audio.wav / transcript.txt ──► audio_transcript_to_csv.py ──► word-level timestamps CSV
```

### 1. Fixations: `generate_fixations.py`

Converts the raw headset gaze stream (about 70 Hz) into **REFLACX‑style `fixations.csv`** tables. Fixations are detected with **I‑DT** (dispersion, the default) or **I‑VT** (velocity, 35 °/s as in REFLACX), measured in degrees of visual angle. Gaps from blinks or tracking loss of up to `--max-blink-ms` are bridged, and runs shorter than `--min-fix-ms` are dropped. The output has the same columns as REFLACX, except `pupil_area_normalized`, because the headset does not record pupil size.

```bash
python generate_fixations.py CSVLoggers/6_20260527_154521_gaze.csv \
    --bounds Bounds.csv --outdir fixations_output/User6
# options: --method {idt,ivt} --disp-thresh-deg 1.0 --sacc-vel-thresh 35
#          --min-fix-ms 100 --max-blink-ms 100 --use-raw ...
```

### 2. Annotations: strokes → boxes and ellipses

```bash
python generate_rectangles_and_ellipses.py \
    --csv PaintingData/6_painting_20260527_162312.csv \
    --img_dir "Assets/RX-Ray Images" \
    --out_dir rect_ellipse_output \
    --metrics 6_painting_20260527_162312_rect_ellipse_metrics.csv \
    --shapes both
```

Strokes are separated by pauses in time (`--gap_seconds`) and by spatial clustering (DBSCAN, `--eps_space`). For each stroke, the script draws an axis‑aligned bounding box and the ellipse inscribed in it, and writes the shape metrics to a CSV. The heatmap scripts use this CSV.

To check the results visually:
- `painting.py` and `checkVRPainter.py` draw the raw painted points on the CXRs.
- `ellipses.py` draws the fitted ellipses, coloured by label.

### 3. Process 1: per‑image heatmaps (`heatmapsAndEllipses.py`)

For each image *i* and participant *p*, the script builds a heatmap **H₍p,i₎** at the image's original resolution:

- Every gaze sample adds a 2‑D Gaussian with **σ = 1° of visual angle**. σ is calculated from the displayed size of the image (`Bounds.csv`) and the viewing distance for that sample (1.5 m + `ZoomOut` − `ZoomIn`).
- The heatmap is normalised so that it sums to 1.

The heatmap is drawn over the CXR together with the participant's ellipses. The script also reports the **intersection rate**, which is the share of gaze that falls inside each ellipse and inside any ellipse.

`heatmapsAndEllipsesFixations.py` does the same from a fixations CSV. Each fixation's Gaussian is weighted by its **duration**, and the intersection rate is measured as **time inside the ellipse / total time**.

### 4. Process 2: participant‑average heatmap and NCC (`heatmapsAndEllipsesP2.py`)

This script reuses the Process 1 functions (it imports `heatmapsAndEllipses.py`, which must be in the same folder). Then it:

1. Aligns each image's heatmap to a common chest bounding box (`chest_bounding_boxes.csv`). Each heatmap is scaled and shifted onto a shared canvas.
2. Averages the aligned heatmaps into the participant average **A₍p₎**, counting only the pixels each image actually covers.
3. Crops and resizes **A₍p₎** back to each image's frame, which gives the baseline **B₍p,i₎**.
4. Turns the ellipses into a binary mask **E₍p,i₎** and compares **NCC(H, E)** with **NCC(B, E)**, following the REFLACX method. This tests whether gaze on a given image matches its annotations better than the participant's average gaze pattern does.

Outputs (`.png`, `.npy` and `user{id}_intersection_ncc.csv`) are saved to `heatmaps_ellipses_P2_figures/User {id}/`. `heatmapsAndEllipsesFixationsP2.py` is the version that works from fixations.

### 5. Dictation: `audio_transcript_to_csv.py`

Produces a **REFLACX‑style word CSV** (`word, timestamp_start_word, timestamp_end_word`):

```bash
# real word timestamps from the audio (faster-whisper)
python audio_transcript_to_csv.py --audio AudioRecording/User6_20260527_154531.wav \
    --out 6_timestamps_transcription.csv --model small --language pt

# from text only (timings estimated at a uniform --wps words per second)
python audio_transcript_to_csv.py --text AudioPostProcessing/User6_20260527_154531.txt \
    --out 6_timestamps_transcription.csv --drop-fillers
```

### 6. Exploratory notebook

`eye_gaze_visualization.ipynb` loads one gaze CSV and draws a **heatmap** and a numbered **scanpath** for each image. Set `CSV_PATH` and `IMAGES_DIR` in the first code cell.

---

## Annotation labels

The annotation legend uses the REFLACX abnormality labels. The Python scripts use these colour codes to map each painted colour back to its label:

| Label | Hex | Label | Hex |
|---|---|---|---|
| Airway wall thickening | `8B0000` | Hiatal hernia | `FFA500` |
| Atelectasis | `FF0000` | Mass | `C71585` |
| Consolidation | `DC3132` | Nodule | `82BE08` |
| Emphysema & High lung volume/emphysema | `006400` | Lung nodule or mass | `8B4513` |
| Enlarged cardiac silhouette | `00FF00` | Pleural abnormality | `FFAEA9` |
| Enlarged hilum | `B2FF59` | Pleural effusion | `FFE4C4` |
| Fibrosis & Interstitial lung disease | `0000FF` | Pleural thickening | `00BFFF` |
| Fracture & Acute fracture | `4169E1` | Pneumothorax | `4B0082` |
| Groundglass opacity | `00FFFF` | Pulmonary edema | `808000` |
| Quality issue | `008B8B` | Support devices | `8A2BE2` |
| Wide mediastinum & Abnormal mediastinal contour | `FF00FF` | | |

---

## Reference files

| File | Columns | Used by |
|---|---|---|
| `Bounds.csv` | `ImageName, xminShownImage, xmaxShownImage, yminShownImage, ymaxShownImage` (Unity world units) | `generate_fixations.py`, all heatmap scripts (to compute σ = 1°) |
| `chest_bounding_boxes.csv` | `ImageName, xmin, ymin, xmax, ymax` (CXR pixels) | Process 2 scripts (chest alignment) |
| `Assets/RX-Ray Images/` | `{ImageName}.jpg` | All scripts that draw on the images |

---

## Acknowledgements

This work was developed within the project XAVIER – eXplainable Artificial intelligence and Virtual reality for Enhanced Radiology (INESC‑ID Lisboa), funded by FCT – Fundação para a Ciência e a Tecnologia, in collaboration with Instituto Superior Técnico and the University of Technology Sydney.

The data formats and analysis methods follow **REFLACX**:

> Bigolin Lanfredi, R., Zhang, M., Auffermann, W. F., et al. *REFLACX, a dataset of reports and eye‑tracking data for localization of abnormalities in chest x‑rays.* Scientific Data 9, 350 (2022). https://doi.org/10.1038/s41597-022-01441-z
