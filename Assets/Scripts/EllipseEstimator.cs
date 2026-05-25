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
    }

    public struct EllipseResult
    {
        public string ImageName;
        public string Label;
        public float XMin;
        public float XMax;
        public float YMin;
        public float YMax;
        public Vector2 Center;
    }

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

                if (tokens.Length >= 6)
                {
                    string imageName = tokens[1].Trim();
                    float x = float.Parse(tokens[2], CultureInfo.InvariantCulture);
                    float y = float.Parse(tokens[3], CultureInfo.InvariantCulture);
                    string label = tokens[5].Trim();

                    points.Add(new PaintPoint { ImageName = imageName, X = x, Y = y, Label = label });
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[EllipseEstimator] Error parsing CSV: {e.Message}");
            return;
        }

        if (points.Count == 0) return;

        // Group points by both ImageName AND Label to keep different overlays distinct
        var groupedPoints = points.GroupBy(p => new { p.ImageName, p.Label });
        List<EllipseResult> results = new List<EllipseResult>();

        foreach (var group in groupedPoints)
        {
            List<Vector2> uniquePoints = group.Select(p => new Vector2(p.X, p.Y)).Distinct().ToList();
            if (uniquePoints.Count < 3) continue;

            List<Vector2> hull = ComputeConvexHull(uniquePoints);
            if (hull.Count == 0) continue;

            float xMin = hull.Min(p => p.x);
            float xMax = hull.Max(p => p.x);
            float yMin = hull.Min(p => p.y);
            float yMax = hull.Max(p => p.y);
            Vector2 center = new Vector2((xMin + xMax) / 2f, (yMin + yMax) / 2f);

            results.Add(new EllipseResult
            {
                ImageName = group.Key.ImageName,
                Label = group.Key.Label,
                XMin = xMin,
                XMax = xMax,
                YMin = yMin,
                YMax = yMax,
                Center = center
            });
        }

        if (results.Count > 0)
        {
            string directory = Path.GetDirectoryName(rawCsvPath);
            string fileName = Path.GetFileNameWithoutExtension(rawCsvPath);
            string outputPath = Path.Combine(directory, $"{fileName}_ellipses.csv");

            StringBuilder sb = new StringBuilder();
            // Header updated to include ImageName
            sb.AppendLine("ImageName,Label,X_Min,X_Max,Y_Min,Y_Max,Center_X,Center_Y");

            foreach (var res in results)
            {
                sb.AppendLine($"{res.ImageName},{res.Label}," +
                              $"{res.XMin.ToString("F6", CultureInfo.InvariantCulture)}," +
                              $"{res.XMax.ToString("F6", CultureInfo.InvariantCulture)}," +
                              $"{res.YMin.ToString("F6", CultureInfo.InvariantCulture)}," +
                              $"{res.YMax.ToString("F6", CultureInfo.InvariantCulture)}," +
                              $"{res.Center.x.ToString("F6", CultureInfo.InvariantCulture)}," +
                              $"{res.Center.y.ToString("F6", CultureInfo.InvariantCulture)}");
            }

            File.WriteAllText(outputPath, sb.ToString());
            Debug.Log($"[EllipseEstimator] Generated output: {outputPath}");
        }
    }

    private static List<Vector2> ComputeConvexHull(List<Vector2> points)
    {
        List<Vector2> sorted = points.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        int n = sorted.Count;
        List<Vector2> hull = new List<Vector2>(new Vector2[2 * n]);
        int k = 0;

        for (int i = 0; i < n; ++i)
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