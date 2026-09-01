"""
heatmapsAndEllipsesP2_fixations.py
==================================
Process 2 — Participant-Average Heatmap, fixations edition.

Same as heatmapsAndEllipsesP2.py, but the per-image heatmaps H_{p,i} are built
from a REFLACX-style **fixations** CSV instead of raw gaze. Process 1 is not
re-implemented: this script imports heatmapsAndEllipses_fixations (the adapted
P1 script) and calls its functions, so H_{p,i} is produced by exactly the same
fixation code you run for P1 — including the duration weighting.

Only two things differ from the raw-gaze P2:
  * input is the fixations CSV (columns image/x_position/y_position/
    timestamp_start_fixation/timestamp_end_fixation/...); coordinates are
    recovered onto each loaded image and each fixation carries a duration;
  * H_{p,i} weights every fixation's Gaussian by its duration (REFLACX
    "intensity proportional to the fixation duration").

Everything downstream — chest-box alignment (Steps 1-9), the participant
average A_p, per-image B_{p,i}, ellipse masks, intersection rate and NCC(H,E)
vs NCC(B,E) — is unchanged, because it operates on the sum-to-1 heatmaps.

heatmapsAndEllipses_fixations.py must sit in the same folder (or on PYTHONPATH).
"""

import os
import numpy as np
import pandas as pd
import matplotlib
SHOW_PLOTS = False
if not SHOW_PLOTS:
    matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from PIL import Image

# Import the adapted Process 1 (fixations) script and reuse ITS functions.
try:
    import heatmapsAndEllipsesFixations as p1
except ImportError as e:
    raise SystemExit(
        "Could not import heatmapsAndEllipses_fixations.py — it must be in the "
        "same folder as this script (or on PYTHONPATH).\n"
        f"Original error: {e}")

# ── USER SETTINGS ────────────────────────────────────────────────────────────
FIXATIONS_CSV   = "fixations_output/User4/4_fixations.csv"                                  # <-- fixations, not gaze
IMAGES_DIR      = "Assets/RX-Ray Images"
METRICS_CSV     = "ellipses_User4.csv"
BOUNDS_CSV      = "Bounds.csv"
CHEST_BOUND_CSV = "chest_bounding_boxes.csv"   # ImageName,xmin,ymin,xmax,ymax (CXR px)

IMAGE_EXTS  = [".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".dcm"]
OUTPUT_DIR_TEMPLATE = "P2_fixations_heatmaps_ellipses_figures/User {user_id}"

# These must match the adapted P1 script; they are pushed onto the imported
# p1 module below so its functions behave identically here.
BASE_VIEW_DISTANCE_M = 1.5
FALLBACK_SIGMA_PX    = 30
SIGMA_ROUND_PX       = 1.0
SIGMA_SOURCE         = "bounds"    # "bounds" or "angres" (see P1)
FLIP_Y               = True

# Column names in the FIXATIONS CSV
COL_IMAGE     = "image"
COL_X         = "x_position"
COL_Y         = "y_position"
COL_TSTART    = "timestamp_start_fixation"
COL_TEND      = "timestamp_end_fixation"
COL_GRID_XMAX = "xmax_shown_from_image"
COL_GRID_YMAX = "ymax_shown_from_image"

# Chest-box CSV uses its own ImageName column (original CXR pixel coords)
COL_BOX_IMAGE = "ImageName"

COMPUTE_NCC = True
# ─────────────────────────────────────────────────────────────────────────────

# ── Keep the P1 module's config in sync, then reuse its functions ────────────
for _name in ("COL_IMAGE", "COL_X", "COL_Y", "COL_TSTART", "COL_TEND",
              "COL_GRID_XMAX", "COL_GRID_YMAX", "BASE_VIEW_DISTANCE_M",
              "FALLBACK_SIGMA_PX", "SIGMA_ROUND_PX", "SIGMA_SOURCE", "FLIP_Y"):
    setattr(p1, _name, globals()[_name])

_stem              = p1._stem
find_image_file    = p1.find_image_file
load_image_as_rgb  = p1.load_image_as_rgb
fixations_to_pixels = p1.fixations_to_pixels
load_bounds        = p1.load_bounds
sigma_for_image    = p1.sigma_for_image
build_density_map  = p1.build_density_map
HEATMAP_CMAP       = p1.HEATMAP_CMAP


# ═════════════════════════════════════════════════════════════════════════════
# Process 2 — participant-average heatmap  (unchanged from raw-gaze P2)
# ═════════════════════════════════════════════════════════════════════════════

def resize_bilinear(arr, new_w, new_h):
    im = Image.fromarray(arr.astype(np.float32), mode="F")
    return np.array(im.resize((int(new_w), int(new_h)), Image.BILINEAR),
                    dtype=np.float64)


def load_chest_boxes(path):
    """P2 Step 1: one chest box per unique CXR (duplicate rows averaged)."""
    df = pd.read_csv(path)
    df.columns = df.columns.str.strip()
    df["_stem"] = df[COL_BOX_IMAGE].map(_stem)
    agg = df.groupby("_stem")[["xmin", "ymin", "xmax", "ymax"]].mean()
    return {k: (float(r["xmin"]), float(r["ymin"]),
                float(r["xmax"]), float(r["ymax"]))
            for k, r in agg.iterrows()}


def participant_average(heatmaps, sizes, boxes):
    keys = [k for k in heatmaps if k in boxes]
    for k in heatmaps:
        if k not in boxes:
            print(f"[WARN] no chest box for '{k}' — excluded from the average")
    if not keys:
        raise RuntimeError("No images have both a heatmap and a chest box.")

    w_bar = (np.mean([boxes[k][2] for k in keys])
             - np.mean([boxes[k][0] for k in keys]))
    h_bar = (np.mean([boxes[k][3] for k in keys])
             - np.mean([boxes[k][1] for k in keys]))
    print(f"[P2] common chest box: w_bar={w_bar:.1f}px  h_bar={h_bar:.1f}px "
          f"({len(keys)} images)")

    placed = {}
    for k in keys:
        W, H = sizes[k]
        xmin, ymin, xmax, ymax = boxes[k]
        w_i, h_i = xmax - xmin, ymax - ymin
        sx, sy = w_bar / w_i, h_bar / h_i

        newW = max(1, int(round(W * sx)))
        newH = max(1, int(round(H * sy)))
        hm = resize_bilinear(heatmaps[k], newW, newH)
        t = hm.sum()
        if t > 0:
            hm /= t

        placed[k] = dict(
            heat=hm,
            left=xmin * sx, top=ymin * sy,
            right=(W - xmax) * sx, bottom=(H - ymax) * sy,
            newW=newW, newH=newH, sx=sx, sy=sy,
        )

    L = max(p["left"]   for p in placed.values())
    T = max(p["top"]    for p in placed.values())
    R = max(p["right"]  for p in placed.values())
    B = max(p["bottom"] for p in placed.values())
    canvasW = int(np.ceil(L + w_bar + R))
    canvasH = int(np.ceil(T + h_bar + B))
    chest_x0, chest_y0 = L, T
    print(f"[P2] canvas: {canvasW} x {canvasH} px "
          f"(chest anchored at {chest_x0:.1f}, {chest_y0:.1f})")

    S = np.zeros((canvasH, canvasW), dtype=np.float64)
    C = np.zeros((canvasH, canvasW), dtype=np.float64)
    for k, p in placed.items():
        x0 = int(round(chest_x0 - p["left"]))
        y0 = int(round(chest_y0 - p["top"]))
        x1, y1 = x0 + p["newW"], y0 + p["newH"]
        cx0, cy0 = max(x0, 0), max(y0, 0)
        cx1, cy1 = min(x1, canvasW), min(y1, canvasH)
        S[cy0:cy1, cx0:cx1] += p["heat"][cy0 - y0:cy1 - y0, cx0 - x0:cx1 - x0]
        C[cy0:cy1, cx0:cx1] += 1.0
        p["rect"] = (x0, y0, x1, y1)

    A = np.zeros_like(S)
    np.divide(S, C, out=A, where=C > 0)

    Bp = {}
    for k, p in placed.items():
        x0, y0, x1, y1 = p["rect"]
        cx0, cy0 = max(x0, 0), max(y0, 0)
        cx1, cy1 = min(x1, canvasW), min(y1, canvasH)
        crop = A[cy0:cy1, cx0:cx1]
        W, H = sizes[k]
        Bp[k] = resize_bilinear(crop, W, H)
    return A, Bp, placed


# ═════════════════════════════════════════════════════════════════════════════
# Masks, intersection rate, NCC  (unchanged)
# ═════════════════════════════════════════════════════════════════════════════

def ellipse_union_mask(aois, w, h):
    yy, xx = np.mgrid[0:h, 0:w]
    mask = np.zeros((h, w), dtype=bool)
    for _, r in aois.iterrows():
        ax = max(float(r["semi_axis_x"]), 1.0)
        ay = max(float(r["semi_axis_y"]), 1.0)
        cx, cy = float(r["center_x"]), float(r["center_y"])
        mask |= ((xx - cx) / ax) ** 2 + ((yy - cy) / ay) ** 2 <= 1.0
    return mask


def single_ellipse_mask(r, w, h):
    yy, xx = np.mgrid[0:h, 0:w]
    ax = max(float(r["semi_axis_x"]), 1.0)
    ay = max(float(r["semi_axis_y"]), 1.0)
    cx, cy = float(r["center_x"]), float(r["center_y"])
    return ((xx - cx) / ax) ** 2 + ((yy - cy) / ay) ** 2 <= 1.0


def heat_share(heat, mask):
    total = heat.sum()
    return float(heat[mask].sum() / total) if total > 0 else 0.0


def ncc(a, b):
    a = a.astype(np.float64).ravel(); b = b.astype(np.float64).ravel()
    a = a - a.mean(); b = b - b.mean()
    denom = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / denom) if denom > 0 else float("nan")


# ═════════════════════════════════════════════════════════════════════════════
# Figures  (unchanged)
# ═════════════════════════════════════════════════════════════════════════════

def save_mask(mask, title, out_png, img_path=None, plain=True):
    h, w = mask.shape
    if plain:
        fig = plt.figure(figsize=(6, 6 * h / w))
        ax = fig.add_axes([0, 0, 1, 1])
        ax.imshow(mask.astype(np.uint8), cmap="gray", vmin=0, vmax=1,
                  extent=[0, w, h, 0], aspect="equal", interpolation="nearest")
        ax.set_xlim(0, w); ax.set_ylim(h, 0); ax.axis("off")
        fig.savefig(out_png, dpi=150, facecolor="black")
        plt.close(fig)
        print(f"  saved -> {out_png}")
        return

    fig, ax = plt.subplots(figsize=(12, 10 * h / w))
    fig.patch.set_facecolor("black")
    if img_path is not None:
        img_rgb = load_image_as_rgb(img_path).astype(np.float32) / 255.0
        img_tinted = np.clip(img_rgb * 0.45
                             + np.array([0.05, 0.10, 0.45]) * 0.55, 0, 1)
        ax.imshow(img_tinted, extent=[0, w, h, 0], aspect="equal")
        overlay = np.zeros((h, w, 4), dtype=np.float32)
        overlay[mask] = (1.0, 1.0, 1.0, 0.45)
        ax.imshow(overlay, extent=[0, w, h, 0], aspect="equal")
    else:
        ax.imshow(mask.astype(np.uint8), cmap="gray", vmin=0, vmax=1,
                  extent=[0, w, h, 0], aspect="equal")
    ax.set_xlim(0, w); ax.set_ylim(h, 0); ax.axis("off")
    ax.set_title(title, color="white", fontweight="bold")
    plt.tight_layout()
    fig.savefig(out_png, dpi=150, bbox_inches="tight", facecolor="black")
    if SHOW_PLOTS:
        plt.show()
    plt.close(fig)
    print(f"  saved -> {out_png}")


def save_overlay(img_path, heat, title, out_png, aois=None):
    img_rgb = load_image_as_rgb(img_path).astype(np.float32) / 255.0
    img_tinted = np.clip(img_rgb * 0.45
                         + np.array([0.05, 0.10, 0.45]) * 0.55, 0, 1)
    h, w = heat.shape
    disp = heat / heat.max() if heat.max() > 0 else heat

    fig, ax = plt.subplots(figsize=(12, 10 * h / w))
    fig.patch.set_facecolor("black")
    ax.imshow(img_tinted, extent=[0, w, h, 0], aspect="equal")
    hm = ax.imshow(disp, cmap=HEATMAP_CMAP, vmin=0, vmax=1,
                   extent=[0, w, h, 0], aspect="equal")
    cbar = fig.colorbar(hm, ax=ax, fraction=0.046, pad=0.02)
    cbar.set_label("Fixation dwell density (relative)", color="white", fontsize=13)
    cbar.ax.yaxis.set_tick_params(color="white")
    cbar.outline.set_edgecolor("white")
    plt.setp(cbar.ax.yaxis.get_ticklabels(), color="white")

    union_mask = np.zeros((h, w), dtype=bool)
    if aois is not None:
        for _, r in aois.iterrows():
            cx, cy = float(r["center_x"]), float(r["center_y"])
            ax_, ay_ = float(r["semi_axis_x"]), float(r["semi_axis_y"])
            m = single_ellipse_mask(r, w, h)
            union_mask |= m
            share = heat_share(heat, m)
            ax.add_patch(mpatches.Ellipse(
                (cx, cy), 2 * ax_, 2 * ay_, angle=0,
                fill=False, edgecolor="white", lw=2.0, zorder=5))
            lbl = (f'{r["label"]} #{int(r["stroke"])}'
                   if "label" in r and "stroke" in r else "ellipse")
            ax.text(cx, cy - ay_ - 6, f"{lbl}: {share:.0%}",
                    color="white", fontsize=12, ha="center", va="bottom",
                    fontweight="bold", zorder=6)
        union_share = heat_share(heat, union_mask)
    else:
        union_share = float("nan")

    ax.set_xlim(0, w); ax.set_ylim(h, 0); ax.axis("off")
    if aois is not None and not np.isnan(union_share):
        ax.set_title(f"{title}  |  dwell inside ANY ellipse: {union_share:.0%}",
                     color="white", fontweight="bold")
    else:
        ax.set_title(title, color="white", fontweight="bold")
    plt.tight_layout()
    fig.savefig(out_png, dpi=150, bbox_inches="tight", facecolor="black")
    if SHOW_PLOTS:
        plt.show()
    plt.close(fig)
    print(f"  saved -> {out_png}")


# ═════════════════════════════════════════════════════════════════════════════
# Main
# ═════════════════════════════════════════════════════════════════════════════

def main():
    # ── load FIXATIONS ──────────────────────────────────────────────────────
    df = pd.read_csv(FIXATIONS_CSV)
    df.columns = df.columns.str.strip()
    need = [COL_IMAGE, COL_X, COL_Y, COL_TSTART, COL_TEND,
            COL_GRID_XMAX, COL_GRID_YMAX]
    missing = [c for c in need if c not in df.columns]
    if missing:
        raise SystemExit(f"fixations CSV missing columns: {missing}")
    df = df.dropna(subset=[COL_X, COL_Y])

    # user id from the fixations filename prefix (e.g. 6_fixations.csv -> "6")
    user_id = os.path.basename(FIXATIONS_CSV).split("_")[0]

    OUTPUT_DIR = (OUTPUT_DIR_TEMPLATE.format(user_id=user_id)
                  if OUTPUT_DIR_TEMPLATE else None)
    if OUTPUT_DIR:
        os.makedirs(OUTPUT_DIR, exist_ok=True)
        print(f"User {user_id} — saving outputs to: "
              f"{os.path.abspath(OUTPUT_DIR)}")

    bounds = load_bounds(BOUNDS_CSV)
    boxes = load_chest_boxes(CHEST_BOUND_CSV)

    # ── discover images + sizes ─────────────────────────────────────────────
    image_info = {}
    for name in df[COL_IMAGE].unique():
        path = find_image_file(name, IMAGES_DIR, IMAGE_EXTS)
        if path is None:
            print(f"[WARN] image not found: {name}")
            continue
        with Image.open(path) as im:
            wdt, hgt = im.size
        image_info[name] = {"path": path, "width": wdt, "height": hgt}

    # ── P1 (via adapted fixations script): H_{p,i} for every viewed image ────
    heatmaps, sizes = {}, {}
    for name, info in image_info.items():
        w, h = info["width"], info["height"]
        sub = fixations_to_pixels(df[df[COL_IMAGE] == name].copy(), w, h)
        px, py = sub["px"].values, sub["py"].values
        dur = sub["duration"].values.astype(float)
        if len(px) == 0:
            print(f"[skip] no fixations for {name}")
            continue

        sigma_x, sigma_y, ok = sigma_for_image(name, w, h, bounds, sub)
        tag = "" if ok else "  [WARN: not in Bounds.csv, fallback sigma]"
        print(f"[P1] {name}: {len(px)} fixations | dwell {dur.sum():.2f}s | "
              f"sigma_x {sigma_x[0]:.1f} px | sigma_y {sigma_y[0]:.1f} px{tag}")

        Hp = build_density_map(px, py, w, h, sigma_x, sigma_y, dur)
        if Hp.sum() == 0:
            print(f"[skip] {name}: empty heatmap")
            continue
        heatmaps[_stem(name)] = Hp
        sizes[_stem(name)] = (w, h)

    if not heatmaps:
        raise SystemExit("No image-specific heatmaps produced.")

    # ── P2: participant-average heatmap ─────────────────────────────────────
    A, Bp, placed = participant_average(heatmaps, sizes, boxes)
    stem_to_name = {_stem(n): n for n in image_info}

    # ── ellipse metrics (overlays + NCC) ────────────────────────────────────
    if os.path.isfile(METRICS_CSV):
        metrics = pd.read_csv(METRICS_CSV)
        metrics.columns = metrics.columns.str.strip()
    else:
        print(f"[WARN] metrics CSV not found ({METRICS_CSV}) — "
              f"overlays will have no ellipses and NCC is skipped")
        metrics = None

    def aois_for(stem):
        if metrics is None:
            return None
        return metrics[metrics["image_name"].map(_stem) == stem]

    if OUTPUT_DIR:
        np.save(os.path.join(
            OUTPUT_DIR, f"user{user_id}_participant_average_canvas.npy"), A)
        disp = A / A.max() if A.max() > 0 else A
        fig, ax = plt.subplots(figsize=(10, 10 * A.shape[0] / A.shape[1]))
        fig.patch.set_facecolor("black")
        ax.imshow(disp, cmap=HEATMAP_CMAP, vmin=0, vmax=1)
        ax.axis("off")
        ax.set_title(f"User {user_id} — participant-average heatmap A_p "
                     f"(chest-aligned canvas)",
                     color="white", fontweight="bold")
        plt.tight_layout()
        fig.savefig(os.path.join(
            OUTPUT_DIR, f"user{user_id}_participant_average_canvas.png"),
            dpi=150, bbox_inches="tight", facecolor="black")
        plt.close(fig)

        for stem, B in Bp.items():
            name = stem_to_name.get(stem, stem)
            info = image_info[name]
            np.save(os.path.join(OUTPUT_DIR,
                                 f"user{user_id}_{stem}_Hp.npy"),
                    heatmaps[stem])
            np.save(os.path.join(OUTPUT_DIR,
                                 f"user{user_id}_{stem}_Bp.npy"), B)
            save_overlay(info["path"], B,
                         f"User {user_id} — {name[:30]} — "
                         f"participant-average B",
                         os.path.join(OUTPUT_DIR,
                                      f"user{user_id}_{stem}_Bp_overlay.png"),
                         aois=aois_for(stem))

    # ── Intersection rates + REFLACX-style NCC comparison ───────────────────
    if COMPUTE_NCC and metrics is not None:
        img_rows, aoi_rows = [], []
        for stem, B in Bp.items():
            name = stem_to_name.get(stem, stem)
            w, h = sizes[stem]
            aois = aois_for(stem)
            if aois is None or aois.empty:
                print(f"[rate] {name}: no ellipses in metrics CSV — skipped")
                continue

            for _, r in aois.iterrows():
                m = single_ellipse_mask(r, w, h)
                aoi_rows.append({
                    "user": user_id, "image": name,
                    "label": r.get("label", ""),
                    "stroke": int(r["stroke"]) if "stroke" in r else -1,
                    "rate_H": round(heat_share(heatmaps[stem], m), 4),
                    "rate_B": round(heat_share(B, m), 4),
                })

            E = ellipse_union_mask(aois, w, h)
            if OUTPUT_DIR:
                np.save(os.path.join(
                    OUTPUT_DIR, f"user{user_id}_{stem}_Emask.npy"),
                    E.astype(np.uint8))
                save_mask(E, f"User {user_id} — {name[:30]} — "
                          f"ellipse union mask E",
                          os.path.join(OUTPUT_DIR,
                                       f"user{user_id}_{stem}_Emask.png"),
                          plain=True)
            rate_H = heat_share(heatmaps[stem], E)
            rate_B = heat_share(B, E)
            Ef = E.astype(np.float64)
            ncc_H = ncc(heatmaps[stem], Ef)
            ncc_B = ncc(B, Ef)
            img_rows.append({
                "user": user_id, "image": name,
                "rate_H_union": round(rate_H, 4),
                "rate_B_union": round(rate_B, 4),
                "ncc_H": round(ncc_H, 4),
                "ncc_B": round(ncc_B, 4),
                "H_greater_than_B": ncc_H > ncc_B,
            })

        if img_rows:
            res = pd.DataFrame(img_rows)
            print(f"\n=== User {user_id} — intersection rate & NCC "
                  f"(H = this CXR, B = participant average) ===")
            print(res.to_string(index=False))
            print("rate_* = fraction of dwell heat inside the abnormality "
                  "ellipses; H > B (NCC) means this reading localises the "
                  "abnormalities better than the participant's usual pattern.")
            if OUTPUT_DIR:
                res.to_csv(os.path.join(
                    OUTPUT_DIR, f"user{user_id}_intersection_ncc.csv"),
                    index=False)
                pd.DataFrame(aoi_rows).to_csv(os.path.join(
                    OUTPUT_DIR, f"user{user_id}_intersection_per_ellipse.csv"),
                    index=False)
                print(f"saved -> user{user_id}_intersection_ncc.csv "
                      f"(+ per-ellipse CSV)")


if __name__ == "__main__":
    main()