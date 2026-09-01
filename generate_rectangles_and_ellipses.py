"""
generate_rectangles_and_ellipses.py
====================================
Like the ellipse script, but for every stroke it draws AXIS-ALIGNED shapes:

  * a horizontal bounding rectangle around the stroke, and
  * an ellipse inscribed in that rectangle, with its axes horizontal/vertical
    (angle = 0) — i.e. "according to the axis", not rotated to the stroke.

It can also re-draw the original painting (thick brush) underneath, and it
writes a metrics CSV describing every shape.

Usage
-----
python generate_rectangles_and_ellipses.py \
    --csv     PaintingData/painting.csv \
    --img_dir "Assets/RX-Ray Images" \
    --out_dir rect_ellipse_output \
    --metrics rect_ellipse_metrics.csv \
    --shapes  both          # both | rect | ellipse

Flags
-----
--shapes            Which shapes to draw: both / rect / ellipse. Default both
--pad               Extra padding (px) added around each bounding box. Default 0
--rect_thickness    Rectangle border thickness (px). Default 8
--ellipse_thickness Ellipse border thickness (px). Default 8
--brush_size        Brush thickness (px) for the redrawn painting. Default 16
--no_redraw         Skip redrawing; only add shapes (source already drawn on).
--no_outline        Don't add the black contrast halo around shapes.
--gap_seconds       Pause (s) that splits strokes of the same label. Default 1.0
--eps_space         Spatial radius (normalised 0-1) for stroke clustering. 0.08
--img_ext           Image extension hint (jpg/png/...). Default: auto-detect
--no_label          Hide the text labels next to shapes.
"""

import argparse
import sys
from pathlib import Path

import cv2
import numpy as np
import pandas as pd
from sklearn.cluster import DBSCAN


# ─────────────────────────────────────────────────────────────────────────────
# Colour palette for labels (BGR for OpenCV)
# ─────────────────────────────────────────────────────────────────────────────
LABEL_COLORS_BGR = [
    (0,   114, 189), (217, 83,  25), (237, 177, 32), (126, 47,  142),
    (119, 172, 48), (77,  190, 238), (162, 20,  47), (0,   128, 128),
    (128, 0,   128), (255, 153, 51),
]


def hex_to_bgr(hex_str: str):
    hex_str = hex_str.strip().lstrip("#")
    r = int(hex_str[0:2], 16)
    g = int(hex_str[2:4], 16)
    b = int(hex_str[4:6], 16)
    return (b, g, r)


def find_image(img_dir: Path, stem: str, ext_hint: str | None = None):
    exts = ([ext_hint] if ext_hint else []) + [
        "jpg", "jpeg", "png", "bmp", "tiff", "tif"]
    for ext in exts:
        p = img_dir / f"{stem}.{ext}"
        if p.exists():
            return p
    matches = list(img_dir.glob(f"{stem}.*"))
    return matches[0] if matches else None


def to_pixels(points_norm, img_w, img_h):
    """Normalised (x, y) -> pixel coords (y flipped)."""
    return np.column_stack([
        points_norm[:, 0] * img_w,
        (1.0 - points_norm[:, 1]) * img_h,
    ])


# ─────────────────────────────────────────────────────────────────────────────
# Re-draw the painting from the raw CSV points (thick rounded brush)
# ─────────────────────────────────────────────────────────────────────────────
def draw_painting(canvas, group, color_bgr, img_w, img_h,
                  brush_size=16, max_gap_norm=0.04, gap_seconds=1.0):
    g = group.sort_values("Timestamp").reset_index(drop=True)
    if len(g) == 0:
        return
    pts_px = to_pixels(g[["NormalizedX", "NormalizedY"]].values,
                       img_w, img_h).astype(np.int32)
    norm = g[["NormalizedX", "NormalizedY"]].values
    t_sec = (g["Timestamp"] - g["Timestamp"].min()).dt.total_seconds().values
    radius = max(1, int(round(brush_size / 2.0)))

    cv2.circle(canvas, tuple(pts_px[0]), radius, color_bgr, -1, cv2.LINE_AA)
    for i in range(1, len(pts_px)):
        cv2.circle(canvas, tuple(pts_px[i]), radius, color_bgr, -1, cv2.LINE_AA)
        space_gap = np.hypot(norm[i, 0] - norm[i - 1, 0],
                             norm[i, 1] - norm[i - 1, 1])
        time_gap = t_sec[i] - t_sec[i - 1]
        if space_gap > max_gap_norm or time_gap > gap_seconds:
            continue
        cv2.line(canvas, tuple(pts_px[i - 1]), tuple(pts_px[i]),
                 color_bgr, brush_size, cv2.LINE_AA)


# ─────────────────────────────────────────────────────────────────────────────
# Split a label's points into individual strokes (space + time clustering)
# ─────────────────────────────────────────────────────────────────────────────
def split_strokes_by_position(group, eps_space=0.08, gap_seconds=1.0,
                               min_samples=3):
    group = group.sort_values("Timestamp").reset_index(drop=True)
    if len(group) < min_samples:
        return [group] if len(group) >= 5 else []

    pts_space = group[["NormalizedX", "NormalizedY"]].values
    t_sec = (group["Timestamp"] - group["Timestamp"].min()).dt.total_seconds().values
    time_scale = eps_space / gap_seconds
    pts_combined = np.column_stack([pts_space, t_sec * time_scale])

    labels = DBSCAN(eps=eps_space, min_samples=min_samples,
                    metric="euclidean").fit(pts_combined).labels_
    strokes = []
    for cid in sorted(set(labels)):
        if cid == -1:
            continue
        subset = group[labels == cid]
        if len(subset) >= 5:
            strokes.append(subset)
    return strokes


# ─────────────────────────────────────────────────────────────────────────────
# Axis-aligned bounding box of a stroke (in pixel coords)
# ─────────────────────────────────────────────────────────────────────────────
def bbox_from_points(points_norm, img_w, img_h, pad=0):
    """Return the horizontal bounding box (x_min, y_min, x_max, y_max) in px."""
    pts_px = to_pixels(points_norm, img_w, img_h)
    if len(pts_px) < 2:
        return None
    x_min = max(0, int(round(pts_px[:, 0].min())) - pad)
    y_min = max(0, int(round(pts_px[:, 1].min())) - pad)
    x_max = min(img_w - 1, int(round(pts_px[:, 0].max())) + pad)
    y_max = min(img_h - 1, int(round(pts_px[:, 1].max())) + pad)
    if x_max <= x_min or y_max <= y_min:
        return None
    return x_min, y_min, x_max, y_max


def draw_rectangle(canvas, box, color_bgr, thickness=8, outline=True):
    x_min, y_min, x_max, y_max = box
    if outline:
        cv2.rectangle(canvas, (x_min, y_min), (x_max, y_max),
                      (0, 0, 0), thickness + 4, cv2.LINE_AA)
    cv2.rectangle(canvas, (x_min, y_min), (x_max, y_max),
                  color_bgr, thickness, cv2.LINE_AA)


def draw_axis_ellipse(canvas, box, color_bgr, thickness=8, outline=True):
    """Ellipse inscribed in the bbox, axes horizontal/vertical (angle = 0)."""
    x_min, y_min, x_max, y_max = box
    cx = (x_min + x_max) // 2
    cy = (y_min + y_max) // 2
    ax = max(1, (x_max - x_min) // 2)   # horizontal semi-axis
    ay = max(1, (y_max - y_min) // 2)   # vertical semi-axis
    if outline:
        cv2.ellipse(canvas, (cx, cy), (ax, ay), 0, 0, 360,
                    (0, 0, 0), thickness + 4, cv2.LINE_AA)
    cv2.ellipse(canvas, (cx, cy), (ax, ay), 0, 0, 360,
                color_bgr, thickness, cv2.LINE_AA)
    return cx, cy, ax, ay


def draw_label(canvas, box, text, color_bgr, img_w, img_h):
    font, scale, thick = cv2.FONT_HERSHEY_SIMPLEX, 1.4, 2
    (tw, th), baseline = cv2.getTextSize(text, font, scale, thick)
    x_min, y_min, x_max, y_max = box
    cx = (x_min + x_max) // 2
    cy = (y_min + y_max) // 2
    tx = max(0, min(cx - tw // 2, img_w - tw))
    margin = 8
    if cy < img_h / 2:
        ty = y_max + th + margin      # box in top half -> label below
    else:
        ty = y_min - margin           # box in bottom half -> label above
    ty = max(th, min(ty, img_h - baseline))
    # cv2.putText(canvas, text, (tx, ty), font, scale, (0, 0, 0),
    #             thick + 4, cv2.LINE_AA)
    cv2.putText(canvas, text, (tx, ty), font, scale, color_bgr,
                thick, cv2.LINE_AA)


# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────
def main():
    p = argparse.ArgumentParser(
        description="Draw axis-aligned rectangles and ellipses per stroke.")
    p.add_argument("--csv",     default="PaintingData/6_painting_20260527_162312.csv")
    p.add_argument("--img_dir", default="Assets/RX-Ray Images")
    p.add_argument("--out_dir", default="rect_ellipse_output")
    p.add_argument("--metrics", default="6_painting_20260527_162312_rect_ellipse_metrics.csv")
    p.add_argument("--shapes",  default="both",
                   choices=["both", "rect", "ellipse"])
    p.add_argument("--pad", type=int, default=0)
    p.add_argument("--rect_thickness", type=int, default=15)
    p.add_argument("--ellipse_thickness", type=int, default=15)
    p.add_argument("--brush_size", type=int, default=40)
    p.add_argument("--no_redraw", action="store_true")
    p.add_argument("--no_outline", action="store_true")
    p.add_argument("--gap_seconds", type=float, default=1.0)
    p.add_argument("--eps_space", type=float, default=0.08)
    p.add_argument("--img_ext", default=None)
    p.add_argument("--no_label", action="store_true")
    args = p.parse_args()

    csv_path, img_dir, out_dir = Path(args.csv), Path(args.img_dir), Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    if not csv_path.exists():
        sys.exit(f"ERROR: CSV not found: {csv_path}")
    if not img_dir.exists():
        sys.exit(f"ERROR: Image directory not found: {img_dir}")

    print(f"Reading CSV: {csv_path}")
    df = pd.read_csv(csv_path)
    df["Timestamp"] = pd.to_datetime(df["Timestamp"])
    required = {"Timestamp", "ImageName", "NormalizedX", "NormalizedY", "Label"}
    missing = required - set(df.columns)
    if missing:
        sys.exit(f"ERROR: CSV is missing columns: {missing}")

    draw_outline = not args.no_outline
    do_rect = args.shapes in ("both", "rect")
    do_ell = args.shapes in ("both", "ellipse")

    all_labels = sorted(df["Label"].dropna().unique())
    label_color = {}
    for i, lbl in enumerate(all_labels):
        if "ColorHex" in df.columns:
            try:
                label_color[lbl] = hex_to_bgr(
                    str(df[df["Label"] == lbl].iloc[0]["ColorHex"]))
                continue
            except Exception:
                pass
        label_color[lbl] = LABEL_COLORS_BGR[i % len(LABEL_COLORS_BGR)]

    metrics_rows = []

    for image_name, img_group in df.groupby("ImageName"):
        img_path = find_image(img_dir, image_name, args.img_ext)
        if img_path is None:
            print(f"  [WARN] image not found for '{image_name}', skipping")
            continue
        canvas = cv2.imread(str(img_path))
        if canvas is None:
            print(f"  [WARN] could not read '{img_path}', skipping")
            continue

        img_h, img_w = canvas.shape[:2]
        print(f"\nImage: {image_name}  ({img_w}x{img_h})")

        for label, lbl_group in img_group.groupby("Label"):
            color = label_color.get(label, (0, 255, 0))
            if not args.no_redraw:
                draw_painting(canvas, lbl_group, color, img_w, img_h,
                              brush_size=args.brush_size,
                              gap_seconds=args.gap_seconds)

            strokes = split_strokes_by_position(
                lbl_group, eps_space=args.eps_space, gap_seconds=args.gap_seconds)
            print(f"  Label: {label!r:40s}  strokes={len(strokes)}")

            for idx, stroke_df in enumerate(strokes):
                pts = stroke_df[["NormalizedX", "NormalizedY"]].values
                box = bbox_from_points(pts, img_w, img_h, pad=args.pad)
                if box is None:
                    print(f"    stroke #{idx+1}: degenerate box, skipped")
                    continue

                if do_rect:
                    draw_rectangle(canvas, box, color,
                                   args.rect_thickness, draw_outline)
                if do_ell:
                    draw_axis_ellipse(canvas, box, color,
                                      args.ellipse_thickness, draw_outline)

                if not args.no_label:
                    text = f"{label} #{idx+1}" if len(strokes) > 1 else str(label)
                    draw_label(canvas, box, text, color, img_w, img_h)

                x_min, y_min, x_max, y_max = box
                metrics_rows.append({
                    "image_name": image_name, "label": label, "stroke": idx + 1,
                    "x_min": x_min, "y_min": y_min, "x_max": x_max, "y_max": y_max,
                    "width": x_max - x_min, "height": y_max - y_min,
                    "center_x": (x_min + x_max) // 2,
                    "center_y": (y_min + y_max) // 2,
                    "semi_axis_x": (x_max - x_min) // 2,
                    "semi_axis_y": (y_max - y_min) // 2,
                    "img_width": img_w, "img_height": img_h,
                })

        out_path = out_dir / f"{image_name}_shapes{img_path.suffix}"
        cv2.imwrite(str(out_path), canvas)
        print(f"  -> saved: {out_path}")

    if metrics_rows:
        cols = ["image_name", "label", "stroke",
                "x_min", "y_min", "x_max", "y_max", "width", "height",
                "center_x", "center_y", "semi_axis_x", "semi_axis_y",
                "img_width", "img_height"]
        pd.DataFrame(metrics_rows, columns=cols).to_csv(args.metrics, index=False)
        print(f"\nMetrics CSV written: {args.metrics}  ({len(metrics_rows)} shapes)")
    else:
        print("\nNo shapes were drawn - check image paths and CSV content.")
    print("Done.")


if __name__ == "__main__":
    main()