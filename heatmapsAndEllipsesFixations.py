"""
heatmapsAndEllipses_fixations.py
================================
Adaptation of heatmapsAndEllipses.py that consumes a REFLACX-style
**fixations** CSV (e.g. ``6_fixations.csv``) instead of the raw per-sample gaze
CSV. Everything about the figure (tinted CXR, density colourmap, AOI outlines,
de-overlapping labels) is unchanged; only the data ingestion and the two places
where "one gaze sample" was implicitly "one unit of dwell time" are changed.

WHY THE ADAPTATION IS NOT JUST A COLUMN RENAME
----------------------------------------------
1. Coordinates. Fixations carry pixel positions (x_position, y_position) in the
   *fixation grid* (xmax_shown_from_image x ymax_shown_from_image), not in the
   loaded image's pixel space, and there is no normalised-gaze column. We
   recover a normalised [0,1] position as x_position / xmax_shown_from_image and
   then map it onto the loaded image's real size, so any scale difference
   between the fixation grid and the image file cancels out. Y is flipped to
   match the original script (which flipped raw gaze Y), so fixations land in
   the same frame as the ellipse metrics.

2. Dwell = TIME, not count. With raw gaze, the fraction of samples inside an AOI
   approximates dwell time because samples are evenly spaced. Fixations are not
   evenly spaced -- each has a duration. So:
     * every fixation's Gaussian is weighted by its duration (REFLACX builds
       fixation heatmaps with "intensity proportional to the fixation
       duration"), and
     * the intersection rate becomes  time_inside / total_time , i.e. the
       share of dwell TIME on the AOI, which is what the count-fraction was
       standing in for.

3. Sigma (1 degree of visual angle in pixels). Same Bounds-based computation as
   the original. Fixations have no per-sample zoom, so the eye-to-image distance
   is the base value and sigma is constant per image. If your fixations file has
   *real* angular_resolution_* values (pixels-per-degree per fixation), set
   SIGMA_SOURCE = "angres" to use them directly and skip Bounds entirely.
"""

import os
import glob
import numpy as np
import pandas as pd
import matplotlib
SHOW_PLOTS = False
if not SHOW_PLOTS:
    matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.colors import LinearSegmentedColormap
from scipy.ndimage import gaussian_filter
from PIL import Image

# ── USER SETTINGS ────────────────────────────────────────────────────────────
FIXATIONS_CSV = "fixations_output/User4/4_fixations.csv"                                    # <-- fixations, not gaze
IMAGES_DIR    = "Assets/RX-Ray Images"
METRICS_CSV   = "ellipses_User4.csv"
BOUNDS_CSV    = "Bounds.csv"

IMAGE_EXTS  = [".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".dcm"]
OUTPUT_DIR  = "P1_fixations_heatmaps_ellipses_figures/User 4"   # None to skip saving

BASE_VIEW_DISTANCE_M = 1.5       # eye-to-image distance (Bounds/world units)
FALLBACK_SIGMA_PX    = 30        # only for images missing from Bounds.csv
SIGMA_ROUND_PX       = 1.0

# "bounds" -> compute 1-deg sigma from Bounds.csv + BASE_VIEW_DISTANCE_M
# "angres" -> use the fixations file's angular_resolution_* columns directly
#             (only sensible if those hold real pixels-per-degree, not a fallback)
SIGMA_SOURCE = "bounds"

# Flip fixation Y to match the ellipse-metric frame. The original raw-gaze
# script flipped normalised gaze Y; fixations were built without that flip, so
# we reapply it here. If an overlay looks vertically mirrored, set this False.
FLIP_Y = True

# Column names in the FIXATIONS CSV
COL_IMAGE   = "image"
COL_X       = "x_position"
COL_Y       = "y_position"
COL_TSTART  = "timestamp_start_fixation"
COL_TEND    = "timestamp_end_fixation"
COL_GRID_XMAX = "xmax_shown_from_image"    # fixation-grid width  (xmin is 0)
COL_GRID_YMAX = "ymax_shown_from_image"    # fixation-grid height
COL_ANGRES_X  = "angular_resolution_x_pixels_per_degree"
COL_ANGRES_Y  = "angular_resolution_y_pixels_per_degree"

AOI_SHAPE = "ellipse"            # "ellipse" or "rect"
# ─────────────────────────────────────────────────────────────────────────────


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


def _stem(s):
    return os.path.splitext(str(s))[0]


# ── Fixations -> loaded-image pixels (+ duration) ────────────────────────────
def fixations_to_pixels(df_img, width, height):
    """
    Recover a normalised [0,1] position from the fixation-grid pixels
    (x_position / xmax_shown_from_image) and map it onto the loaded image's real
    size. Y is flipped (if FLIP_Y) to match the ellipse-metric frame. Also
    carries each fixation's duration (seconds), used to weight the heatmap and
    the dwell rate.
    """
    out = df_img.copy()
    gx = out[COL_GRID_XMAX].astype(float).replace(0, np.nan)
    gy = out[COL_GRID_YMAX].astype(float).replace(0, np.nan)
    nx = (out[COL_X].astype(float) / gx).clip(0.0, 1.0)
    ny = (out[COL_Y].astype(float) / gy).clip(0.0, 1.0)
    if FLIP_Y:
        ny = 1.0 - ny
    out["px"] = (nx * (width - 1)).round().astype(int)
    out["py"] = (ny * (height - 1)).round().astype(int)
    out["duration"] = (out[COL_TEND].astype(float)
                       - out[COL_TSTART].astype(float)).clip(lower=0.0)
    return out


# ── 1 degree of visual angle in pixels, per image ───────────────────────────
def load_bounds(bounds_csv):
    b = pd.read_csv(bounds_csv)
    b.columns = b.columns.str.strip()
    b["_stem"] = b["ImageName"].map(_stem)
    return b.set_index("_stem")


def physical_size_to_degrees(size, distance):
    return np.degrees(2.0 * np.arctan((size / 2.0) / distance))


def sigma_for_image(image_name, width_px, height_px, bounds, sub):
    """
    Return (sigma_x_arr, sigma_y_arr, ok) with one value per fixation.

    SIGMA_SOURCE == "angres": use the file's pixels-per-degree columns directly
    (sigma == 1 deg in px). SIGMA_SOURCE == "bounds": derive from the displayed
    size in Bounds.csv at BASE_VIEW_DISTANCE_M (constant per image, since
    fixations carry no per-sample zoom).
    """
    n = len(sub)
    if SIGMA_SOURCE == "angres" and COL_ANGRES_X in sub.columns:
        sx = sub[COL_ANGRES_X].astype(float).to_numpy()
        sy = sub[COL_ANGRES_Y].astype(float).to_numpy()
        return sx, sy, True

    stem = _stem(image_name)
    if stem not in bounds.index:
        fb = np.full(n, FALLBACK_SIGMA_PX, dtype=float)
        return fb, fb.copy(), False
    r = bounds.loc[stem]
    shown_w = float(r["xmaxShownImage"]) - float(r["xminShownImage"])
    shown_h = float(r["ymaxShownImage"]) - float(r["yminShownImage"])
    deg_w = physical_size_to_degrees(shown_w, BASE_VIEW_DISTANCE_M)
    deg_h = physical_size_to_degrees(shown_h, BASE_VIEW_DISTANCE_M)
    sx = np.full(n, width_px / deg_w, dtype=float)
    sy = np.full(n, height_px / deg_h, dtype=float)
    return sx, sy, True


# ── Duration-weighted, sum-to-1 fixation heatmap ────────────────────────────
def build_density_map(px, py, width, height, sigma_x_arr, sigma_y_arr, weights):
    """
    Same five-step construction as the original, except each fixation's Gaussian
    is scaled by its DURATION (weights) rather than an equal unit peak, matching
    REFLACX fixation heatmaps. Fixations sharing a (rounded) sigma are convolved
    together for speed; the +weight is deposited per fixation before convolution.
    """
    heat = np.zeros((height, width), dtype=np.float64)
    inb = (px >= 0) & (px < width) & (py >= 0) & (py < height)
    px, py, w = px[inb], py[inb], np.asarray(weights, float)[inb]
    sx = np.maximum(np.round(sigma_x_arr[inb] / SIGMA_ROUND_PX) * SIGMA_ROUND_PX, 1e-6)
    sy = np.maximum(np.round(sigma_y_arr[inb] / SIGMA_ROUND_PX) * SIGMA_ROUND_PX, 1e-6)

    for (gsx, gsy) in set(zip(sx, sy)):
        sel = (sx == gsx) & (sy == gsy)
        canvas = np.zeros((height, width), dtype=np.float64)
        np.add.at(canvas, (py[sel], px[sel]), w[sel])       # +duration, not +1
        g = gaussian_filter(canvas, sigma=(gsy, gsx), mode="constant", cval=0.0)
        heat += g * (2.0 * np.pi * gsx * gsy)               # equal peak per unit weight

    total = heat.sum()
    if total > 0:
        heat /= total
    return heat


def inside_ellipse(x, y, cx, cy, ax, ay):
    ax = max(ax, 1); ay = max(ay, 1)
    return ((x - cx) / ax) ** 2 + ((y - cy) / ay) ** 2 <= 1.0


def inside_rect(x, y, x_min, y_min, x_max, y_max):
    return (x >= x_min) & (x <= x_max) & (y >= y_min) & (y <= y_max)


def place_labels_no_overlap(ax, fig, specs, h, pad_px=3, max_iter=100):
    if not specs:
        return
    inv = ax.transData.inverted()
    texts = [ax.text(s["x"], s["y"], s["text"], color="white", fontsize=14,
                     ha="center", va=s["va"], fontweight="bold", zorder=7)
             for s in specs]
    fig.canvas.draw()
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
                ox = min(bi.x1, bj.x1) - max(bi.x0, bj.x0)
                oy = min(bi.y1, bj.y1) - max(bi.y0, bj.y0)
                if ox > 0 and oy > -pad_px:
                    shift = (oy + pad_px) / 2.0
                    ci = 0.5 * (bi.y0 + bi.y1)
                    cj = 0.5 * (bj.y0 + bj.y1)
                    hi, lo = (i, j) if ci >= cj else (j, i)
                    nudge(texts[hi], +shift)
                    nudge(texts[lo], -shift)
                    boxes[i] = texts[i].get_window_extent(renderer=rend)
                    boxes[j] = texts[j].get_window_extent(renderer=rend)
                    moved = True
        if not moved:
            break

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
    dur = sub["duration"].values.astype(float)
    if len(px) == 0:
        print(f"  (no fixations for {image_name})")
        return pd.DataFrame()

    aois = metrics[metrics["image_name"].map(_stem) == _stem(image_name)]

    sigma_x, sigma_y, ok = sigma_for_image(image_name, w, h, bounds, sub)
    tag = "" if ok else "  [WARN: not in Bounds.csv, fallback sigma]"
    print(f"  {len(px)} fixations | total dwell {dur.sum():.2f}s | "
          f"sigma_x {sigma_x[0]:.1f}px sigma_y {sigma_y[0]:.1f}px{tag}")

    density = build_density_map(px, py, w, h, sigma_x, sigma_y, dur)
    density_disp = density / density.max() if density.max() > 0 else density

    img_rgb = load_image_as_rgb(info["path"]).astype(np.float32) / 255.0
    img_tinted = np.clip(img_rgb * 0.45 + np.array([0.05, 0.10, 0.45]) * 0.55, 0, 1)

    fig, ax = plt.subplots(figsize=(14, 11 * h / w))
    fig.patch.set_facecolor("black")
    ax.imshow(img_tinted, extent=[0, w, h, 0], aspect="equal")
    hm = ax.imshow(density_disp, cmap=HEATMAP_CMAP, vmin=0, vmax=1,
                   extent=[0, w, h, 0], aspect="equal")

    cbar = fig.colorbar(hm, ax=ax, fraction=0.046, pad=0.02)
    cbar.set_label("Fixation dwell density (relative)", color="white", fontsize=14)
    cbar.ax.yaxis.set_tick_params(color="white")
    cbar.outline.set_edgecolor("white")
    plt.setp(cbar.ax.yaxis.get_ticklabels(), color="white")

    total_dur = dur.sum() if dur.sum() > 0 else 1.0
    union = np.zeros(len(px), dtype=bool)
    rows, label_specs = [], []
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
        # dwell rate = share of TIME inside the AOI (duration-weighted)
        dwell_rate = float(dur[m].sum() / total_dur)

        yy, xx = np.mgrid[0:h, 0:w]
        if shape == "rect":
            mask = inside_rect(xx, yy, r["x_min"], r["y_min"], r["x_max"], r["y_max"])
        else:
            mask = inside_ellipse(xx, yy, cx, cy, ax_, ay_)
        heat_share = float(density[mask].sum())

        rows.append({"label": r["label"], "stroke": int(r["stroke"]),
                     "n_fix_inside": int(m.sum()),
                     "time_inside_s": round(float(dur[m].sum()), 3),
                     "dwell_rate": round(dwell_rate, 3),
                     "heat_share": round(heat_share, 3)})

        if cy < h / 2:
            ty, va, edge_y = cy + ay_ + 6, "top", cy + ay_
        else:
            ty, va, edge_y = cy - ay_ - 6, "bottom", cy - ay_
        label_specs.append({"x": cx, "y": ty, "va": va, "edge_y": edge_y,
                            "text": f'{r["label"]} #{int(r["stroke"])}: {dwell_rate:.0%}'})

    ax.set_xlim(0, w); ax.set_ylim(h, 0); ax.axis("off")
    union_rate = float(dur[union].sum() / total_dur)
    ax.set_title(f"{image_name[:30]} — dwell inside ANY {shape}: {union_rate:.1%}",
                 color="white", fontweight="bold")
    plt.tight_layout()
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
    print(f"  dwell inside ANY {shape}: {union_rate:.1%}  "
          f"({dur[union].sum():.2f}s / {total_dur:.2f}s over {int(union.sum())} fixations)")
    return out


def main():
    if OUTPUT_DIR:
        os.makedirs(OUTPUT_DIR, exist_ok=True)
        print(f"Saving figures to: {os.path.abspath(OUTPUT_DIR)}")

    # --- load FIXATIONS ---
    df = pd.read_csv(FIXATIONS_CSV)
    df.columns = df.columns.str.strip()
    need = [COL_IMAGE, COL_X, COL_Y, COL_TSTART, COL_TEND, COL_GRID_XMAX, COL_GRID_YMAX]
    missing = [c for c in need if c not in df.columns]
    if missing:
        raise SystemExit(f"fixations CSV missing columns: {missing}")
    df = df.dropna(subset=[COL_X, COL_Y])

    metrics = pd.read_csv(METRICS_CSV)
    bounds = load_bounds(BOUNDS_CSV)

    # --- discover images + real sizes ---
    image_info = {}
    for name in df[COL_IMAGE].unique():
        path = find_image_file(name, IMAGES_DIR, IMAGE_EXTS)
        if path is None:
            print(f"[WARN] image not found: {name}")
            continue
        with Image.open(path) as im:
            wdt, hgt = im.size
        image_info[name] = {"path": path, "width": wdt, "height": hgt}

    # --- fixations -> loaded-image pixels (+duration), per image ---
    frames = []
    for name, info in image_info.items():
        s = fixations_to_pixels(df[df[COL_IMAGE] == name].copy(),
                                info["width"], info["height"])
        frames.append(s)
    df_px = pd.concat(frames).reset_index(drop=True)

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