"""
heatmapsAndEllipses.py
======================
Standalone script (no notebook state required).

For each image it:
  1. Loads gaze data and converts normalised (0-1) coords -> pixels
  2. Builds the IMAGE-SPECIFIC heatmap H_{p,i} following the required process:
       Step 1  zero matrix with the ORIGINAL image size (W_i x H_i, no resize)
       Step 2  sigma_x = r_x, sigma_y = r_y  (pixels per degree, per axis),
               derived from the displayed image size in Bounds.csv
       Step 3  one 2-D Gaussian per gaze sample, equal peak amplitude
       Step 4  sum all Gaussians pixel by pixel
       Step 5  divide by the total sum so the heatmap sums to 1
  3. Overlays the axis-aligned ellipses from the painting-metrics CSV
  4. Reports the intersection rate: the fraction of gaze samples that fall
     inside each ellipse (~= proportion of dwell time on that region), plus
     a combined "inside ANY ellipse" rate.

Implementation note on steps 3-4: summing identical unit-peak Gaussians
centred on each sample is mathematically identical (up to a constant factor,
removed by the step-5 normalisation) to accumulating +1 per sample into a
count image and convolving once with the same Gaussian. The script uses the
convolution form for speed, with mode="constant" so the kernel is truncated
at the image border exactly as explicit per-sample Gaussians would be.
"""

import os

import glob
import numpy as np
import pandas as pd
import matplotlib
# Headless backend: render straight to files, no blocking pop-up windows.
# Flip SHOW_PLOTS to True below if you also want them displayed interactively.
SHOW_PLOTS = False
if not SHOW_PLOTS:
    matplotlib.use("Agg")
import matplotlib.pyplot as plt

plt.rcParams.update({
    "font.size": 20,
    "axes.titlesize": 20,
    "axes.labelsize": 20,
    "xtick.labelsize": 20,
    "ytick.labelsize": 20,
})

import matplotlib.patches as mpatches
from matplotlib.colors import LinearSegmentedColormap
from scipy.ndimage import gaussian_filter
from PIL import Image

# ── USER SETTINGS (match the notebook) ───────────────────────────────────────
CSV_PATH    = "CSVLoggers/6_20260527_154521_gaze.csv"                 # gaze CSV
IMAGES_DIR  = "Assets/RX-Ray Images"                                  # image folder
METRICS_CSV = "6_painting_20260527_162312_rect_ellipse_metrics.csv"  # ellipse metrics
BOUNDS_CSV  = "Bounds.csv"          # per-image shown bounds (world units)

IMAGE_EXTS  = [".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".dcm"]
OUTPUT_DIR  = "heatmaps_ellipses_P1_figures/User 6"   # set to None to skip saving

# Distance from the participant's eyes to the displayed image, per gaze
# sample:  distance = BASE_VIEW_DISTANCE_M + ZoomOut - ZoomIn
# (same units as Bounds.csv: metres / Unity world units). Needed to turn the
# physical displayed size into a size in DEGREES of visual angle.
BASE_VIEW_DISTANCE_M = 1.5
# Fallback sigma (px) only for images that are missing from Bounds.csv
FALLBACK_SIGMA_PX = 30

# Sigmas are rounded to this many px before grouping samples for the
# convolution (smaller = more precise, slower). 1 px is visually negligible.
SIGMA_ROUND_PX = 1.0

# How to handle the zoom-dependent distance when computing the 1-degree sigma:
#   "per_sample" -> one sigma per gaze sample (distance of THAT sample)
#   "median"     -> one sigma per image, using the MEDIAN distance of all
#                   samples on that image (simpler; single convolution)
SIGMA_MODE = "per_sample"

# Column names in the gaze CSV
COL_IMAGE    = "ImageName"
COL_GAZE_X   = "EyeGazeX (normalized)"   # 0..1 left->right
COL_GAZE_Y   = "EyeGazeY (normalized)"   # 0..1 top->bottom
COL_TIME     = "Timestamp"
COL_ZOOM_IN  = "ZoomIn"
COL_ZOOM_OUT = "ZoomOut"

# Which AOI shape to test gaze against: "ellipse" or "rect"
AOI_SHAPE = "ellipse"
# ─────────────────────────────────────────────────────────────────────────────


# Heatmap colourmap (transparent -> cyan -> green -> yellow -> red)
HEATMAP_CMAP = LinearSegmentedColormap.from_list(
    "medical_gaze",
    [
        (0.00, (0.00, 0.00, 0.50, 0.00)),
        (0.10, (0.00, 0.20, 0.80, 0.55)),
        (0.30, (0.00, 0.70, 0.90, 0.65)),
        (0.55, (0.10, 0.90, 0.20, 0.75)),
        (0.75, (1.00, 1.00, 0.00, 0.85)),
        (1.00, (1.00, 0.00, 0.00, 0.95)),
    ],
    N=512,
)


def find_image_file(image_name, images_dir, extensions):
    stem = os.path.splitext(image_name)[0]
    for ext in extensions:
        cand = os.path.join(images_dir, stem + ext)
        if os.path.isfile(cand):
            return cand
    matches = glob.glob(os.path.join(images_dir, stem + "*"))
    return matches[0] if matches else None


def load_image_as_rgb(path):
    """Load any image as uint8 RGB (PIL only). Handles 16-bit + DICOM."""
    ext = os.path.splitext(path)[1].lower()
    if ext == ".dcm":
        import pydicom
        ds = pydicom.dcmread(path)
        arr = ds.pixel_array.astype(np.float32)
        arr = (arr - arr.min()) / (arr.max() - arr.min() + 1e-8) * 255
        arr = arr.astype(np.uint8)
        if arr.ndim == 2:
            arr = np.stack([arr] * 3, axis=-1)
        return arr
    with Image.open(path) as img:
        if img.mode in ("I;16", "I;16B", "I"):
            arr = np.array(img, dtype=np.float32)
            arr = (arr - arr.min()) / (arr.max() - arr.min() + 1e-8) * 255
            img = Image.fromarray(arr.astype(np.uint8), mode="L")
        return np.array(img.convert("RGB"))


def norm_to_pixels(df_img, width, height):
    out = df_img.copy()
    out["px"] = (out[COL_GAZE_X] * (width - 1)).round().astype(int)
    out["py"] = ((1.0 - out[COL_GAZE_Y]) * (height - 1)).round().astype(int)  # flip Y
    return out


def _stem(s):
    return os.path.splitext(str(s))[0]


# ── Step 2: convert 1 degree of visual angle into pixels, per image ─────────
def load_bounds(bounds_csv):
    """Read Bounds.csv and index it by image-name stem."""
    b = pd.read_csv(bounds_csv)
    b.columns = b.columns.str.strip()
    b["_stem"] = b[COL_IMAGE].map(_stem)
    return b.set_index("_stem")


def physical_size_to_degrees(size, distance):
    """Full angular extent (degrees) subtended by `size` at `distance`."""
    return np.degrees(2.0 * np.arctan((size / 2.0) / distance))


def sigmas_per_sample(image_name, width_px, height_px, bounds, distances):
    """
    sigma_x = r_x = image width  in pixels / displayed image width  in degrees
    sigma_y = r_y = image height in pixels / displayed image height in degrees
    i.e. the Gaussian standard deviation equals 1 degree of visual angle,
    expressed in pixels, independently per axis.

    `distances` is one eye-to-image distance PER GAZE SAMPLE
    (BASE_VIEW_DISTANCE_M + ZoomOut - ZoomIn), so zooming in/out changes the
    displayed size in degrees and therefore the sigma of that sample's
    Gaussian. Returns (sigma_x_arr, sigma_y_arr, found_in_bounds).
    """
    distances = np.asarray(distances, dtype=np.float64)
    stem = _stem(image_name)
    if stem not in bounds.index:
        fb = np.full_like(distances, FALLBACK_SIGMA_PX)
        return fb, fb.copy(), False
    r = bounds.loc[stem]
    shown_w = float(r["xmaxShownImage"]) - float(r["xminShownImage"])
    shown_h = float(r["ymaxShownImage"]) - float(r["yminShownImage"])
    deg_w = physical_size_to_degrees(shown_w, distances)
    deg_h = physical_size_to_degrees(shown_h, distances)
    sigma_x = width_px / deg_w    # r_x: horizontal pixels per degree
    sigma_y = height_px / deg_h   # r_y: vertical  pixels per degree
    return sigma_x, sigma_y, True


# ── Steps 1, 3, 4, 5: build the normalised image-specific heatmap ───────────
def build_density_map(px, py, width, height, sigma_x_arr, sigma_y_arr):
    """
    Step 1: zero matrix with exactly the original image size (no resize).
    Step 3: one Gaussian per gaze sample, all with equal peak amplitude and
            a 1-degree sigma converted to pixels for THAT sample's displayed
            geometry (zoom changes the distance, so sigma varies per sample).
    Step 4: sum the Gaussians pixel by pixel.
    Step 5: divide by the total so the heatmap sums to exactly 1.

    Samples are grouped by (rounded) sigma pair and each group is convolved
    once — mathematically identical to placing one unit-peak Gaussian per
    sample, up to per-group constant factors of 1/(2*pi*sx*sy) which are
    re-applied below so every sample keeps the SAME peak amplitude.
    """
    heat = np.zeros((height, width), dtype=np.float64)            # Step 1
    inb = (px >= 0) & (px < width) & (py >= 0) & (py < height)
    px, py = px[inb], py[inb]
    sx = np.maximum(np.round(sigma_x_arr[inb] / SIGMA_ROUND_PX)
                    * SIGMA_ROUND_PX, 1e-6)
    sy = np.maximum(np.round(sigma_y_arr[inb] / SIGMA_ROUND_PX)
                    * SIGMA_ROUND_PX, 1e-6)

    for (gsx, gsy) in set(zip(sx, sy)):                           # Steps 3+4
        sel = (sx == gsx) & (sy == gsy)
        canvas = np.zeros((height, width), dtype=np.float64)
        np.add.at(canvas, (py[sel], px[sel]), 1.0)
        g = gaussian_filter(canvas, sigma=(gsy, gsx),
                            mode="constant", cval=0.0)
        # gaussian_filter kernels integrate to 1 (peak 1/(2*pi*sx*sy));
        # rescale so each sample's Gaussian has peak amplitude exactly 1,
        # as required by Step 3, regardless of its sigma.
        heat += g * (2.0 * np.pi * gsx * gsy)

    total = heat.sum()
    if total > 0:
        heat /= total                                             # Step 5
    return heat


def inside_ellipse(x, y, cx, cy, ax, ay):
    ax = max(ax, 1); ay = max(ay, 1)           # guard zero-length axes
    return ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 <= 1.0


def inside_rect(x, y, x_min, y_min, x_max, y_max):
    return (x >= x_min) & (x <= x_max) & (y >= y_min) & (y <= y_max)


def place_labels_no_overlap(ax, fig, specs, h, pad_px=3, max_iter=100):
    """Draw the AOI labels, then nudge them vertically until their real text
    boxes no longer overlap. A thin line links each moved label to its AOI.

    specs: list of dicts with keys x, y, va, edge_y, text.
    """
    if not specs:
        return
    inv = ax.transData.inverted()
    texts = [ax.text(s["x"], s["y"], s["text"], color="white", fontsize=30,
                     ha="center", va=s["va"], fontweight="bold", zorder=7)
             for s in specs]

    fig.canvas.draw()                       # renderer needed for text extents
    rend = fig.canvas.get_renderer()

    def nudge(t, dy_px):
        x, y = t.get_position()
        dx0, dy0 = ax.transData.transform((x, y))
        t.set_position(inv.transform((dx0, dy0 + dy_px)))

    for _ in range(max_iter):
        boxes = [t.get_window_extent(renderer=rend) for t in texts]
        moved = False
        for i in range(len(texts)):
            for j in range(i + 1, len(texts)):
                bi, bj = boxes[i], boxes[j]
                ox = min(bi.x1, bj.x1) - max(bi.x0, bj.x0)     # x-overlap (px)
                oy = min(bi.y1, bj.y1) - max(bi.y0, bj.y0)     # y-overlap (px)
                if ox > 0 and oy > -pad_px:                    # boxes collide
                    shift = (oy + pad_px) / 2.0
                    ci = 0.5 * (bi.y0 + bi.y1)
                    cj = 0.5 * (bj.y0 + bj.y1)
                    hi, lo = (i, j) if ci >= cj else (j, i)    # display y: up = +
                    nudge(texts[hi], +shift)
                    nudge(texts[lo], -shift)
                    boxes[i] = texts[i].get_window_extent(renderer=rend)
                    boxes[j] = texts[j].get_window_extent(renderer=rend)
                    moved = True
        if not moved:
            break

    # keep inside the image, and connect any label that drifted from its AOI
    for t, s in zip(texts, specs):
        x, y = t.get_position()
        y = min(max(y, 2), h - 2)
        t.set_position((x, y))
        if abs(y - s["edge_y"]) > 4:
            ax.plot([s["x"], x], [s["edge_y"], y],
                    color="white", lw=0.6, alpha=0.5, zorder=6)


def heatmap_vs_aois(image_name, info, df_px, metrics, bounds,
                    shape=AOI_SHAPE, save_path=None):
    w, h = info["width"], info["height"]
    sub = df_px[df_px[COL_IMAGE] == image_name]
    px, py = sub["px"].values, sub["py"].values
    if len(px) == 0:
        print(f"  (no gaze samples for {image_name})")
        return pd.DataFrame()

    aois = metrics[metrics["image_name"].map(_stem) == _stem(image_name)]

    # eye-to-image distance per sample: base + ZoomOut - ZoomIn
    if COL_ZOOM_IN in sub.columns and COL_ZOOM_OUT in sub.columns:
        dist = (BASE_VIEW_DISTANCE_M
                + sub[COL_ZOOM_OUT].fillna(0).values
                - sub[COL_ZOOM_IN].fillna(0).values)
    else:
        print(f"  [WARN] zoom columns missing, using base distance only")
        dist = np.full(len(px), BASE_VIEW_DISTANCE_M)
    dist = np.maximum(dist, 1e-3)   # guard non-positive distances

    # optional simpler mode: one sigma per image, from the MEDIAN distance
    if SIGMA_MODE == "median":
        dist = np.full(len(px), float(np.median(dist)))

    # Steps 1-5: normalised image-specific heatmap H_{p,i} (sums to 1)
    sigma_x, sigma_y, ok = sigmas_per_sample(image_name, w, h, bounds, dist)
    tag = "" if ok else "  [WARN: not in Bounds.csv, fallback sigma]"
    if SIGMA_MODE == "median":
        print(f"  [median mode] distance: {dist[0]:.2f} m | "
              f"sigma_x: {sigma_x[0]:.1f} px | sigma_y: {sigma_y[0]:.1f} px{tag}")
    else:
        print(f"  distance: {dist.min():.2f}-{dist.max():.2f} m | "
              f"sigma_x: {sigma_x.min():.1f}-{sigma_x.max():.1f} px | "
              f"sigma_y: {sigma_y.min():.1f}-{sigma_y.max():.1f} px{tag}")
    density = build_density_map(px, py, w, h, sigma_x, sigma_y)

    # For DISPLAY only, rescale to [0, 1]; the sum-to-1 values are tiny and
    # would otherwise be invisible. This does not alter H_{p,i} itself.
    density_disp = density / density.max() if density.max() > 0 else density

    img_rgb = load_image_as_rgb(info["path"]).astype(np.float32) / 255.0
    img_tinted = np.clip(img_rgb * 0.45 + np.array([0.05, 0.10, 0.45]) * 0.55, 0, 1)

    fig, ax = plt.subplots(figsize=(14, 11 * h / w))
    fig.patch.set_facecolor("black")
    ax.imshow(img_tinted, extent=[0, w, h, 0], aspect="equal")
    hm = ax.imshow(density_disp, cmap=HEATMAP_CMAP, vmin=0, vmax=1,
                   extent=[0, w, h, 0], aspect="equal")

    # colour scale (relative density) beside the image
    cbar = fig.colorbar(hm, ax=ax, fraction=0.046, pad=0.02)
    cbar.set_label("Eye gaze density (relative)", color="white", fontsize=30)
    cbar.ax.yaxis.set_tick_params(color="white")
    cbar.outline.set_edgecolor("white")
    plt.setp(cbar.ax.yaxis.get_ticklabels(), color="white", fontsize=25)

    union = np.zeros(len(px), dtype=bool)
    rows = []
    label_specs = []
    for _, r in aois.iterrows():
        cx, cy = r["center_x"], r["center_y"]
        ax_, ay_ = r["semi_axis_x"], r["semi_axis_y"]

        if shape == "rect":
            m = inside_rect(px, py, r["x_min"], r["y_min"], r["x_max"], r["y_max"])
            ax.add_patch(mpatches.Rectangle(
                (r["x_min"], r["y_min"]), r["width"], r["height"],
                fill=False, edgecolor="white", lw=2.0, zorder=5))
        else:
            m = inside_ellipse(px, py, cx, cy, ax_, ay_)
            ax.add_patch(mpatches.Ellipse(
                (cx, cy), 2 * ax_, 2 * ay_, angle=0,
                fill=False, edgecolor="white", lw=2.0, zorder=5))

        union |= m
        rate = m.mean()
        # share of total heat inside the AOI (uses the sum-to-1 heatmap)
        yy, xx = np.mgrid[0:h, 0:w]
        if shape == "rect":
            mask = inside_rect(xx, yy, r["x_min"], r["y_min"], r["x_max"], r["y_max"])
        else:
            mask = inside_ellipse(xx, yy, cx, cy, ax_, ay_)
        heat_share = float(density[mask].sum())
        rows.append({"label": r["label"], "stroke": int(r["stroke"]),
                     "n_inside": int(m.sum()), "dwell_rate": round(rate, 3),
                     "heat_share": round(heat_share, 3)})
        # AOI in TOP half -> label below; in BOTTOM half -> label above
        # (y increases downward here: extent=[0, w, h, 0])
        if cy < h / 2:
            ty, va, edge_y = cy + ay_ + 6, "top", cy + ay_
        else:
            ty, va, edge_y = cy - ay_ - 6, "bottom", cy - ay_
        label_specs.append({
            "x": cx, "y": ty, "va": va, "edge_y": edge_y,
            "text": f'{r["label"]} #{int(r["stroke"])}: {rate:.0%}',
        })

    ax.set_xlim(0, w); ax.set_ylim(h, 0); ax.axis("off")
    ax.set_title(f"image-specific H | inside ANY {shape}: {union.mean():.1%}",
                 color="white", fontweight="bold", fontsize=30)
    plt.tight_layout()
    # place labels after layout is fixed, then de-overlap them
    place_labels_no_overlap(ax, fig, label_specs, h)
    if save_path:
        fig.savefig(save_path, dpi=150, bbox_inches="tight", facecolor="black")
        print(f"  saved -> {save_path}")
    if SHOW_PLOTS:
        plt.show()
    plt.close(fig)

    out = pd.DataFrame(rows)
    if not out.empty:
        print(out.to_string(index=False))
    print(f"  inside ANY {shape}: {union.mean():.1%}  ({union.sum()}/{len(px)} samples)")
    return out


def main():
    if OUTPUT_DIR:
        os.makedirs(OUTPUT_DIR, exist_ok=True)
        print(f"Saving figures to: {os.path.abspath(OUTPUT_DIR)}")

    # --- load gaze data ---
    df_raw = pd.read_csv(CSV_PATH)
    df_raw.columns = df_raw.columns.str.strip()
    cols = [COL_IMAGE, COL_GAZE_X, COL_GAZE_Y, COL_TIME]
    for zc in (COL_ZOOM_IN, COL_ZOOM_OUT):
        if zc in df_raw.columns:
            cols.append(zc)
        else:
            print(f"[WARN] column '{zc}' not found in gaze CSV")
    df = df_raw[cols].copy()
    df = df.dropna(subset=[COL_GAZE_X, COL_GAZE_Y])
    df[COL_GAZE_X] = df[COL_GAZE_X].clip(0.0, 1.0)
    df[COL_GAZE_Y] = df[COL_GAZE_Y].clip(0.0, 1.0)

    # --- load ellipse/rect metrics + displayed-image bounds ---
    metrics = pd.read_csv(METRICS_CSV)
    bounds = load_bounds(BOUNDS_CSV)

    # --- discover images + sizes ---
    image_info = {}
    for name in df[COL_IMAGE].unique():
        path = find_image_file(name, IMAGES_DIR, IMAGE_EXTS)
        if path is None:
            print(f"[WARN] image not found: {name}")
            continue
        with Image.open(path) as im:
            wdt, hgt = im.size
        image_info[name] = {"path": path, "width": wdt, "height": hgt}

    # --- normalised -> pixels (per image, because size differs) ---
    frames = []
    for name, info in image_info.items():
        s = norm_to_pixels(df[df[COL_IMAGE] == name].copy(),
                           info["width"], info["height"])
        frames.append(s)
    df_px = pd.concat(frames).reset_index(drop=True)

    # --- run per image ---
    saved = 0
    for name, info in image_info.items():
        print(f"\n===== {name} ({info['width']}x{info['height']}) =====")
        save = None
        if OUTPUT_DIR:
            safe = name.replace("/", "_")
            save = os.path.join(OUTPUT_DIR, f"{safe}_heatmap_{AOI_SHAPE}.png")
        heatmap_vs_aois(name, info, df_px, metrics, bounds, save_path=save)
        if save and os.path.isfile(save):
            saved += 1

    if OUTPUT_DIR:
        print(f"\nDone. {saved} figure(s) saved in {os.path.abspath(OUTPUT_DIR)}")


if __name__ == "__main__":
    main()