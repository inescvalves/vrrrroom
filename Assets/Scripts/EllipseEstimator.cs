using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public static class EllipseEstimator
{
    public struct PaintPoint
    {
        public string ImageName;
        public float X;
        public float Y;
        public string Label;
        public DateTime Timestamp;
    }

    public struct EllipseResult
    {
        public string ImageName;
        public string Label;
        public int Stroke;
        public float CenterX;
        public float CenterY;
        public float SemiMajor;
        public float SemiMinor;
        public float AngleDeg;
        public float XMin;
        public float XMax;
        public float YMin;
        public float YMax;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public static void ProcessDrawingToEllipses(string rawCsvPath)
    {
        if (!File.Exists(rawCsvPath)) return;

        List<PaintPoint> points = new List<PaintPoint>();

        try
        {
            string[] lines = File.ReadAllLines(rawCsvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] tokens = lines[i].Split(',');
                if (tokens.Length < 6) continue;

                DateTime ts;
                if (!DateTime.TryParse(tokens[0].Trim(), CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out ts))
                    ts = DateTime.MinValue;

                string imageName = tokens[1].Trim();
                float x, y;
                if (!float.TryParse(tokens[2].Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out x)) continue;
                if (!float.TryParse(tokens[3].Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out y)) continue;
                string label = tokens[5].Trim();

                points.Add(new PaintPoint
                {
                    Timestamp = ts,
                    ImageName = imageName,
                    X = x,
                    Y = y,
                    Label = label
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[EllipseEstimator] Error parsing CSV: {e.Message}");
            return;
        }

        if (points.Count == 0) return;

        List<EllipseResult> results = new List<EllipseResult>();

        // Group by (ImageName, Label) — same as Python's groupby
        var grouped = points.GroupBy(p => new { p.ImageName, p.Label });

        foreach (var group in grouped)
        {
            List<PaintPoint> groupList = group.OrderBy(p => p.Timestamp).ToList();

            // Split into strokes using time + space gap (mirrors Python's split_strokes_by_position)
            List<List<PaintPoint>> strokes = SplitStrokes(groupList);

            for (int si = 0; si < strokes.Count; si++)
            {
                List<Vector2> pts2d = strokes[si]
                    .Select(p => new Vector2(p.X, p.Y))
                    .ToList();

                EllipseResult? ellipse = FitEllipseFromHull(
                    pts2d, group.Key.ImageName, group.Key.Label, si);

                if (ellipse.HasValue)
                    results.Add(ellipse.Value);
            }
        }

        if (results.Count == 0)
        {
            Debug.LogWarning("[EllipseEstimator] No ellipses fitted — check CSV content.");
            return;
        }

        // Write output CSV next to the raw CSV
        string directory = Path.GetDirectoryName(rawCsvPath);
        string fileName = Path.GetFileNameWithoutExtension(rawCsvPath);
        string outputPath = Path.Combine(directory, $"{fileName}_ellipses.csv");

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("image_name,label,stroke,center_x,center_y," +
                      "x_min,x_max,y_min,y_max,semi_major,semi_minor,angle_deg");

        foreach (EllipseResult res in results)
        {
            sb.AppendLine(string.Join(",",
                res.ImageName,
                res.Label,
                res.Stroke.ToString(CultureInfo.InvariantCulture),
                res.CenterX.ToString("F2", CultureInfo.InvariantCulture),
                res.CenterY.ToString("F2", CultureInfo.InvariantCulture),
                res.XMin.ToString("F2", CultureInfo.InvariantCulture),
                res.XMax.ToString("F2", CultureInfo.InvariantCulture),
                res.YMin.ToString("F2", CultureInfo.InvariantCulture),
                res.YMax.ToString("F2", CultureInfo.InvariantCulture),
                res.SemiMajor.ToString("F2", CultureInfo.InvariantCulture),
                res.SemiMinor.ToString("F2", CultureInfo.InvariantCulture),
                res.AngleDeg.ToString("F2", CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(outputPath, sb.ToString());
        Debug.Log($"[EllipseEstimator] Generated output: {outputPath}  ({results.Count} ellipses)");
    }

    // ── Stroke splitting ──────────────────────────────────────────────────────
    // Greedy sequential equivalent of Python's DBSCAN-based split_strokes_by_position.
    // A new stroke begins whenever consecutive points exceed epsSpace in normalised
    // coords OR gapSeconds in time — whichever occurs first.

    private static List<List<PaintPoint>> SplitStrokes(
        List<PaintPoint> sorted,
        float epsSpace = 0.08f,
        float gapSeconds = 1.0f,
        int minSamples = 3)
    {
        var clusters = new List<List<PaintPoint>>();
        if (sorted.Count == 0) return clusters;

        var current = new List<PaintPoint> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            PaintPoint prev = sorted[i - 1];
            PaintPoint curr = sorted[i];

            float dx = curr.X - prev.X;
            float dy = curr.Y - prev.Y;
            float spatialDist = Mathf.Sqrt(dx * dx + dy * dy);
            float timeDist = (float)(curr.Timestamp - prev.Timestamp).TotalSeconds;

            if (spatialDist > epsSpace || timeDist > gapSeconds)
            {
                if (current.Count >= minSamples)
                    clusters.Add(current);
                current = new List<PaintPoint>();
            }
            current.Add(curr);
        }

        if (current.Count >= minSamples)
            clusters.Add(current);

        return clusters;
    }

    // ── Ellipse fitting ───────────────────────────────────────────────────────
    // Fits an ellipse to the convex hull of the stroke points via covariance
    // eigenvector decomposition - matches the intent of Python's cv2.fitEllipse.
    // Coordinates stay in normalised [0,1] space (no pixel conversion needed
    // here; the Python conversion is only for rendering).

    private static EllipseResult? FitEllipseFromHull(
        List<Vector2> points, string imageName, string label, int strokeIdx)
    {
        List<Vector2> unique = points.Distinct().ToList();
        if (unique.Count < 5) return null;

        List<Vector2> hull = ComputeConvexHull(unique);
        if (hull.Count < 5) return null;

        // Mean center
        float mx = hull.Average(p => p.x);
        float my = hull.Average(p => p.y);

        // 2×2 covariance matrix of hull vertices
        float cxx = 0f, cyy = 0f, cxy = 0f;
        foreach (Vector2 p in hull)
        {
            float dx = p.x - mx;
            float dy = p.y - my;
            cxx += dx * dx;
            cyy += dy * dy;
            cxy += dx * dy;
        }
        int n = hull.Count;
        cxx /= n; cyy /= n; cxy /= n;

        // Eigenvalues of the covariance matrix -> semi-axes lengths
        float trace = cxx + cyy;
        float det = cxx * cyy - cxy * cxy;
        float disc = Mathf.Sqrt(Mathf.Max(0f, trace * trace * 0.25f - det));
        float l1 = trace * 0.5f + disc;   // major
        float l2 = trace * 0.5f - disc;   // minor

        // Scale factor 2 gives a ~1-sigma ellipse similar to cv2.fitEllipse
        float semiMajor = 2f * Mathf.Sqrt(Mathf.Max(0f, l1));
        float semiMinor = 2f * Mathf.Sqrt(Mathf.Max(0f, l2));

        // Angle of the major axis (degrees, matching cv2 convention)
        float angleDeg = Mathf.Rad2Deg * Mathf.Atan2(2f * cxy, cxx - cyy) * 0.5f;

        // Tight axis-aligned bounding box of the rotated ellipse
        float ar = Mathf.Deg2Rad * angleDeg;
        float cosA = Mathf.Cos(ar);
        float sinA = Mathf.Sin(ar);
        float ddx = Mathf.Sqrt(semiMajor * semiMajor * cosA * cosA
                               + semiMinor * semiMinor * sinA * sinA);
        float ddy = Mathf.Sqrt(semiMajor * semiMajor * sinA * sinA
                               + semiMinor * semiMinor * cosA * cosA);

        return new EllipseResult
        {
            ImageName = imageName,
            Label = label,
            Stroke = strokeIdx + 1,
            CenterX = mx,
            CenterY = my,
            SemiMajor = semiMajor,
            SemiMinor = semiMinor,
            AngleDeg = angleDeg,
            XMin = mx - ddx,
            XMax = mx + ddx,
            YMin = my - ddy,
            YMax = my + ddy,
        };
    }

    // ── Convex hull (Andrew's monotone chain) ─────────────────────────────────

    private static List<Vector2> ComputeConvexHull(List<Vector2> points)
    {
        List<Vector2> sorted = points.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        int n = sorted.Count;
        Vector2[] hull = new Vector2[2 * n];
        int k = 0;

        for (int i = 0; i < n; i++)
        {
            while (k >= 2 && CrossProduct(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }
        for (int i = n - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && CrossProduct(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
            hull[k++] = sorted[i];
        }

        return hull.Take(k - 1).ToList();
    }

    private static float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}