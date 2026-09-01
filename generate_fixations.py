#!/usr/bin/env python3
"""
generate_fixations.py

Parse a VRRROOM / HMD gaze export into REFLACX-style ``fixations.csv`` tables,
one per displayed image.

Background
----------
REFLACX fixations were produced by the *EyeLink 1000* hardware parser running on
the Host PC, using a saccade velocity threshold of 35 deg/s, a saccade motion
threshold of 0.2 deg, and a saccade acceleration threshold of 9500 deg/s^2, on a
1000 Hz raw sample stream that includes pupil area and blink events.

The VR export handled here is different in three ways that make an exact
reproduction of the EyeLink algorithm impossible, and shape the method below:

  1. It is sampled at ~70 Hz (HMD-integrated tracker), far below 250-1000 Hz.
     At this rate, acceleration is too noisy to threshold reliably and the
     finest saccades are under-resolved. This is the well-known temporal-fidelity
     limit of HMD trackers; the sound response is to detect the quantities that
     remain robust at this rate -- fixation *locations* and *dwell* -- rather
     than fine saccade metrics.
  2. It carries no pupil-area channel and no explicit blink flag, so
     ``pupil_area_normalized`` cannot be filled and blinks must be inferred from
     gaps / invalid samples.
  3. Gaze is given in normalized image coordinates [0,1] (raw and calibration-
     corrected), not screen coordinates.

Detection method
----------------
A velocity-threshold identification (I-VT) parser is used, which is the
appropriate low-rate analogue of the EyeLink parser and is expressed in the same
physical unit (deg/s). Point-to-point angular velocity is computed in *degrees
of visual angle* using the per-sample angular resolution (pixels-per-degree),
samples below ``sacc_vel_thresh`` deg/s are labelled fixation, contiguous
fixation runs are merged (bridging blinks / brief losses up to
``max_blink_ms``), and runs shorter than ``min_fix_ms`` are dropped. Each
retained run is summarized into one REFLACX fixation row.

Blinks / invalid samples are treated as the paper describes blinks: moments the
tracker returns no usable gaze. They break a fixation only if the gap exceeds
``max_blink_ms``.

The output schema matches REFLACX ``fixations.csv`` except that
``pupil_area_normalized`` is omitted, since this export carries no pupil
channel.

Usage
-----
    python parse_fixations.py GAZE.csv --img-width 2544 --img-height 3056 \
        --outdir out/

If native per-image pixel dimensions are unknown, pass a single --img-width /
--img-height pair (applied to all images) or a JSON map via --img-sizes.
Positions are then in the pixel space of the displayed image, (0,0) = top-left,
matching REFLACX ``average_x_position`` / ``average_y_position``.
"""

import argparse
import json
import os

import numpy as np
import pandas as pd


# ---------------------------------------------------------------------------
# Configuration defaults (all overridable on the command line)
# ---------------------------------------------------------------------------
DEFAULTS = dict(
    method="idt",           # 'idt' (dispersion, default) or 'ivt' (velocity)
    sacc_vel_thresh=35.0,   # deg/s  -- I-VT threshold, same unit as EyeLink parser
    disp_thresh_deg=1.0,    # deg    -- I-DT dispersion threshold (typical 0.5-1.0)
    deg_per_frame=35.0,     # deg    -- visual angle spanned by the full image frame
    min_fix_ms=100.0,       # drop runs shorter than this
    max_blink_ms=100.0,     # bridge losses/blinks up to this within one fixation
    use_corrected=True,     # use calibration-corrected gaze if present
    screen_w=3840,          # HMD panel / virtual display width  (screen coords out)
    screen_h=2160,          # HMD panel / virtual display height
)

# REFLACX fixations.csv column order
OUT_COLS = [
    "image",
    "timestamp_start_fixation", "timestamp_end_fixation",
    "x_position", "y_position",
    "angular_resolution_x_pixels_per_degree",
    "angular_resolution_y_pixels_per_degree",
    "window_width", "window_level",
    "xmin_shown_from_image", "ymin_shown_from_image",
    "xmax_shown_from_image", "ymax_shown_from_image",
    "xmin_in_screen_coordinates", "ymin_in_screen_coordinates",
    "xmax_in_screen_coordinates", "ymax_in_screen_coordinates",
]


def load_gaze(path):
    """Read the export and normalize its slightly messy header."""
    g = pd.read_csv(path)
    g.columns = [c.strip() for c in g.columns]          # strip leading spaces
    return g


def pick_gaze_columns(g, use_corrected):
    """Return (x_norm, y_norm) series in [0,1], preferring corrected gaze."""
    if use_corrected and "EyeGazeX (corrected)" in g.columns:
        x = pd.to_numeric(g["EyeGazeX (corrected)"], errors="coerce")
        y = pd.to_numeric(g["EyeGazeY (corrected)"], errors="coerce")
    else:
        x = pd.to_numeric(g["EyeGazeX (normalized)"], errors="coerce")
        y = pd.to_numeric(g["EyeGazeY (normalized)"], errors="coerce")
    return x, y


def angular_velocity(xdeg, ydeg, t):
    """Point-to-point angular speed (deg/s), NaN-safe, same length as inputs."""
    dt = np.diff(t)
    dt[dt <= 0] = np.nan
    dx = np.diff(xdeg)
    dy = np.diff(ydeg)
    step = np.hypot(dx, dy)          # degrees moved between consecutive samples
    v = step / dt
    # align to samples: velocity[i] describes motion arriving at sample i
    return np.concatenate([[np.nan], v])


def parse_one_image(df, img_w, img_h, cfg):
    """
    Parse a single-image block (already time-sorted) into fixation rows.

    Two detectors are available (cfg['method']):
      * 'idt' (default) -- dispersion-threshold (I-DT). Robust at ~70 Hz and does
        not depend on an accurate pixels-per-degree channel, which this export
        lacks. Dispersion is thresholded in *degrees* using the sample angular
        resolution when present, else in a normalized-gaze proxy.
      * 'ivt' -- velocity-threshold, expressed in deg/s like the EyeLink parser.
        Faithful in unit but sensitive to the angular-resolution estimate.
    """
    t = df["t"].to_numpy(float)
    xn = df["xn"].to_numpy(float)
    yn = df["yn"].to_numpy(float)
    xpix = xn * img_w
    ypix = yn * img_h

    if "ang_res_x" in df:
        arx = df["ang_res_x"].to_numpy(float)
        ary = df["ang_res_y"].to_numpy(float)
    else:
        arx = np.full(len(df), cfg["fallback_ppd"])
        ary = np.full(len(df), cfg["fallback_ppd"])

    valid = ~np.isnan(xn) & ~np.isnan(yn)

    if cfg["method"] == "ivt":
        segs = _detect_ivt(t, xpix, ypix, arx, ary, valid, cfg)
    else:
        segs = _detect_idt(t, xn, yn, valid, cfg)

    rows = [_summarize(df, s, valid[s], xpix, ypix, arx, ary, cfg) for s in segs]
    return pd.DataFrame(rows, columns=OUT_COLS)


def _detect_idt(t, xn, yn, valid, cfg):
    """
    Dispersion-threshold identification (Salvucci & Goldberg I-DT).

    A window is a fixation while its spatial dispersion stays below
    ``disp_thresh_deg`` (converted to a normalized-coordinate span via the
    image's degrees-per-frame). The window grows until dispersion is exceeded,
    then the fixation is emitted and the search restarts after it. Requires the
    fixation to last at least ``min_fix_ms``. Invalid/blink samples inside a
    window are tolerated as long as they do not create a gap longer than
    ``max_blink_ms``.
    """
    n = len(t)
    # dispersion threshold in normalized units: disp_deg * (frac of frame per deg)
    thr = cfg["disp_thresh_deg"] / cfg["deg_per_frame"]
    min_s = cfg["min_fix_ms"] / 1000.0
    max_gap_s = cfg["max_blink_ms"] / 1000.0

    segs = []
    i = 0
    while i < n:
        if not valid[i]:
            i += 1
            continue
        # grow window [i, j]
        j = i
        while j + 1 < n:
            k = j + 1
            # tolerate a short invalid gap
            if not valid[k]:
                m = k
                while m < n and not valid[m]:
                    m += 1
                if m >= n or (t[m] - t[j]) > max_gap_s:
                    break
                k = m
            wv = valid[i:k + 1]
            wx = xn[i:k + 1][wv]
            wy = yn[i:k + 1][wv]
            disp = (wx.max() - wx.min()) + (wy.max() - wy.min())   # I-DT dispersion
            if disp <= thr:
                j = k
            else:
                break
        if (t[j] - t[i]) >= min_s:
            segs.append(slice(i, j + 1))
            i = j + 1
        else:
            i += 1
    return segs


def _detect_ivt(t, xpix, ypix, arx, ary, valid, cfg):
    """Velocity-threshold identification (deg/s), EyeLink-style unit."""
    xdeg = xpix / arx
    ydeg = ypix / ary
    vel = angular_velocity(xdeg, ydeg, t)
    is_fix = valid & (np.nan_to_num(vel, nan=0.0) < cfg["sacc_vel_thresh"])
    max_gap_s = cfg["max_blink_ms"] / 1000.0
    min_s = cfg["min_fix_ms"] / 1000.0

    segs = []
    n = len(t)
    i = 0
    while i < n:
        if not is_fix[i]:
            i += 1
            continue
        last_good = i
        j = i
        while j + 1 < n:
            nxt = j + 1
            if is_fix[nxt]:
                j = nxt; last_good = nxt
            else:
                k = nxt
                while k < n and not is_fix[k]:
                    k += 1
                if k < n and (t[min(k, n - 1)] - t[last_good]) <= max_gap_s:
                    j = k; last_good = k
                else:
                    break
        if (t[last_good] - t[i]) >= min_s:
            segs.append(slice(i, last_good + 1))
        i = last_good + 1
    return segs


def _summarize(df, seg, seg_valid, xpix, ypix, arx, ary, cfg):
    """Collapse one fixation run into a single REFLACX row."""
    t = df["t"].to_numpy(float)[seg]
    xp = xpix[seg][seg_valid]
    yp = ypix[seg][seg_valid]

    # windowing / geometry: value at the *start* of the fixation, as REFLACX does
    first = df.iloc[seg.start]

    row = {
        "image": cfg["image_name"],
        "timestamp_start_fixation": round(float(t[0]), 3),
        "timestamp_end_fixation":   round(float(t[-1]), 3),
        "x_position": int(round(float(np.mean(xp)))),
        "y_position": int(round(float(np.mean(yp)))),
        "angular_resolution_x_pixels_per_degree": int(round(float(np.mean(arx[seg][seg_valid])))),
        "angular_resolution_y_pixels_per_degree": int(round(float(np.mean(ary[seg][seg_valid])))),
        "window_width":  round(float(first.get("WindowWidth", cfg["screen_w"])), 5),
        "window_level":  round(float(first.get("WindowLevel", 0.5)), 5),
        # Whole image shown at fixation start (static viewing mode: no pan/zoom state
        # is exported here, so the shown region is the full image extent).
        "xmin_shown_from_image": 0,
        "ymin_shown_from_image": 0,
        "xmax_shown_from_image": int(cfg["img_w"]),
        "ymax_shown_from_image": int(cfg["img_h"]),
        # Screen-space placement of that image region on the virtual panel.
        "xmin_in_screen_coordinates": int(cfg["scr_xmin"]),
        "ymin_in_screen_coordinates": 0,
        "xmax_in_screen_coordinates": int(cfg["scr_xmax"]),
        "ymax_in_screen_coordinates": int(cfg["screen_h"]),
    }
    return row


def image_screen_box(img_w, img_h, screen_w, screen_h):
    """
    Letterbox the image into the screen preserving aspect ratio, returning the
    horizontal screen span it occupies (vertical assumed full height, as in the
    sample target file where y spans 0..2160).
    """
    img_ar = img_w / img_h
    scr_ar = screen_w / screen_h
    if img_ar <= scr_ar:
        # image is relatively taller -> full height, centered horizontally
        disp_w = screen_h * img_ar
        xmin = (screen_w - disp_w) / 2.0
        xmax = xmin + disp_w
    else:
        disp_w = screen_w
        xmin, xmax = 0.0, screen_w
    return int(round(xmin)), int(round(xmax))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("gaze_csv", help="input VR gaze export")
    ap.add_argument("--outdir", default="fixations_out", help="output directory")
    ap.add_argument("--bounds", help="Bounds.csv mapping ImageName -> world-space "
                    "image extent (xmin/xmax/ymin/ymaxShownImage); the width:height "
                    "ratio sets each image's pixel aspect. Preferred over --img-width.")
    ap.add_argument("--ref-px", type=float, default=3000.0,
                    help="pixel length assigned to the longer image side when using "
                    "--bounds (default 3000). Only sets the coordinate scale.")
    ap.add_argument("--img-width", type=int, help="native image width (px), applied to all images")
    ap.add_argument("--img-height", type=int, help="native image height (px), applied to all images")
    ap.add_argument("--img-sizes", help="JSON file mapping ImageName -> [width,height]")
    ap.add_argument("--method", choices=["idt", "ivt"], default=DEFAULTS["method"],
                    help="fixation detector: idt=dispersion (default), ivt=velocity")
    ap.add_argument("--sacc-vel-thresh", type=float, default=DEFAULTS["sacc_vel_thresh"])
    ap.add_argument("--disp-thresh-deg", type=float, default=DEFAULTS["disp_thresh_deg"])
    ap.add_argument("--deg-per-frame", type=float, default=DEFAULTS["deg_per_frame"],
                    help="visual angle (deg) spanned by the full image frame; ~27-38 here")
    ap.add_argument("--min-fix-ms", type=float, default=DEFAULTS["min_fix_ms"])
    ap.add_argument("--max-blink-ms", type=float, default=DEFAULTS["max_blink_ms"])
    ap.add_argument("--use-raw", action="store_true", help="use raw (uncorrected) gaze")
    ap.add_argument("--screen-w", type=int, default=DEFAULTS["screen_w"])
    ap.add_argument("--screen-h", type=int, default=DEFAULTS["screen_h"])
    args = ap.parse_args()

    g = load_gaze(args.gaze_csv)
    xn, yn = pick_gaze_columns(g, use_corrected=not args.use_raw)
    t_abs = pd.to_numeric(g["SessionTime"], errors="coerce")

    img_sizes = {}
    if args.img_sizes:
        with open(args.img_sizes) as fh:
            img_sizes = {k: tuple(v) for k, v in json.load(fh).items()}

    # Preferred: derive each image's pixel grid from world-space bounds. The
    # width:height ratio of the shown extent equals the image's pixel aspect
    # ratio, so this yields a correctly-shaped, per-image coordinate space
    # without needing native pixel counts (which are not in the gaze export).
    if args.bounds:
        bdf = pd.read_csv(args.bounds)
        bdf.columns = [c.strip() for c in bdf.columns]
        for _, r in bdf.iterrows():
            w = float(r["xmaxShownImage"]) - float(r["xminShownImage"])
            h = float(r["ymaxShownImage"]) - float(r["yminShownImage"])
            if w >= h:
                W, H = args.ref_px, args.ref_px * h / w
            else:
                W, H = args.ref_px * w / h, args.ref_px
            img_sizes[str(r["ImageName"])] = (int(round(W)), int(round(H)))

    os.makedirs(args.outdir, exist_ok=True)
    manifest = []
    all_out = []

    for img, idx in _image_blocks(g):
        block = g.loc[idx].copy()
        # relative session time so timestamps start near 0 (REFLACX convention)
        tb = t_abs.loc[idx].to_numpy(float)
        block["t"] = tb - tb[0]
        block["xn"] = xn.loc[idx].to_numpy(float)
        block["yn"] = yn.loc[idx].to_numpy(float)

        w, h = _resolve_size(img, img_sizes, args)
        sxmin, sxmax = image_screen_box(w, h, args.screen_w, args.screen_h)
        cfg = dict(
            method=args.method,
            sacc_vel_thresh=args.sacc_vel_thresh,
            disp_thresh_deg=args.disp_thresh_deg,
            deg_per_frame=args.deg_per_frame,
            min_fix_ms=args.min_fix_ms,
            max_blink_ms=args.max_blink_ms,
            img_w=w, img_h=h,
            screen_w=args.screen_w, screen_h=args.screen_h,
            scr_xmin=sxmin, scr_xmax=sxmax,
            fallback_ppd=_fallback_ppd(w, args.screen_w),
            image_name=img,
        )

        out = parse_one_image(block, w, h, cfg)
        all_out.append(out)
        manifest.append((img, len(out)))
        print(f"{img[:12]}...  {len(block):6d} samples -> {len(out):4d} fixations")

    # One combined table for the whole session, named by UserID.
    combined = pd.concat(all_out, ignore_index=True) if all_out else pd.DataFrame(columns=OUT_COLS)
    user_id = str(g["UserID"].iloc[0]) if len(g) else "session"
    out_path = os.path.join(args.outdir, f"{user_id}_fixations.csv")
    combined.to_csv(out_path, index=False)

    print(f"\nDone. {len(manifest)} image(s), {len(combined)} fixations -> {out_path}")


def _image_blocks(g):
    """Yield (ImageName, index) for each contiguous image block, in view order."""
    names = g["ImageName"].to_numpy()
    start = 0
    for i in range(1, len(g) + 1):
        if i == len(g) or names[i] != names[start]:
            yield names[start], g.index[start:i]
            start = i


def _resolve_size(img, img_sizes, args):
    if img in img_sizes:
        return img_sizes[img]
    if args.img_width and args.img_height:
        return args.img_width, args.img_height
    raise SystemExit(
        f"No pixel size for image {img}. Provide --bounds Bounds.csv (preferred), "
        f"--img-sizes JSON, or --img-width/--img-height. A per-image size is needed "
        f"to convert normalized gaze to image pixels (REFLACX average_x/y_position)."
    )


def _fallback_ppd(img_w, screen_w):
    """Rough pixels-per-degree if the export lacks angular resolution.
    Placeholder: assumes ~35 px/deg scaled by image/screen ratio. Only used
    when no ang-res channel exists; prefer supplying real values."""
    return max(1.0, 35.0 * (img_w / screen_w))


if __name__ == "__main__":
    main()