// Cuts individual sprites out of a concept/asset sheet with a flat light background.
// Compiled by slice.ps1 via Add-Type (Windows PowerShell 5.1, so C# 5 syntax only).
//
// For each box: flood-fills the background from the box border (colour-distance threshold),
// feathers the edge, drops small detached islands (stray label text), trims and saves a PNG.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class SpriteSlicer
{
    public static string Slice(string sheetPath, string boxesPath, string outDir, double bgTolerance, double featherRange)
    {
        var log = new System.Text.StringBuilder();
        using (var sheet = new Bitmap(sheetPath))
        {
            int sw = sheet.Width, sh = sheet.Height;
            int[] src = ReadPixels(sheet);
            foreach (var raw in System.IO.File.ReadAllLines(boxesPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split(',');
                string name = p[0].Trim();
                int x0 = int.Parse(p[1]), y0 = int.Parse(p[2]), x1 = int.Parse(p[3]), y1 = int.Parse(p[4]);
                double tol = p.Length > 5 ? double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture) : bgTolerance;
                x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(sw, x1); y1 = Math.Min(sh, y1);
                int w = x1 - x0, h = y1 - y0;

                int[] px = new int[w * h];
                for (int y = 0; y < h; y++)
                    Array.Copy(src, (y0 + y) * sw + x0, px, y * w, w);

                int bg = EstimateBackground(px, w, h);
                byte[] alpha = Cutout(px, w, h, bg, tol, featherRange);
                KeepMainIslands(alpha, w, h, 0.03);
                if (p.Length > 6 && p[6].Trim() == "circle")
                    CircleMask(alpha, w, h);

                int tx0 = w, ty0 = h, tx1 = -1, ty1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (alpha[y * w + x] > 8)
                        {
                            if (x < tx0) tx0 = x; if (x > tx1) tx1 = x;
                            if (y < ty0) ty0 = y; if (y > ty1) ty1 = y;
                        }
                if (tx1 < 0) { log.AppendLine(name + ": EMPTY"); continue; }

                const int pad = 2;
                int ow = tx1 - tx0 + 1 + pad * 2, oh = ty1 - ty0 + 1 + pad * 2;
                using (var outBmp = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
                {
                    int[] o = new int[ow * oh];
                    for (int y = ty0; y <= ty1; y++)
                        for (int x = tx0; x <= tx1; x++)
                        {
                            int a = alpha[y * w + x];
                            int c = px[y * w + x] & 0x00FFFFFF;
                            o[(y - ty0 + pad) * ow + (x - tx0 + pad)] = a == 0 ? 0 : (a << 24) | c;
                        }
                    WritePixels(outBmp, o);
                    outBmp.Save(System.IO.Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                }
                log.AppendLine(string.Format("{0}: {1}x{2} bg=#{3:X6}", name, ow, oh, bg & 0xFFFFFF));
            }
        }
        return log.ToString();
    }

    /// <summary>Average of the brightest border pixels: the sheet background.</summary>
    private static int EstimateBackground(int[] px, int w, int h)
    {
        var border = new List<int>();
        for (int x = 0; x < w; x++) { border.Add(px[x]); border.Add(px[(h - 1) * w + x]); }
        for (int y = 0; y < h; y++) { border.Add(px[y * w]); border.Add(px[y * w + w - 1]); }
        border.Sort((a, b) => Lum(b).CompareTo(Lum(a)));
        long r = 0, g = 0, bl = 0; int n = Math.Max(1, border.Count / 2);
        for (int i = 0; i < n; i++) { r += (border[i] >> 16) & 255; g += (border[i] >> 8) & 255; bl += border[i] & 255; }
        return (int)(0xFF000000 | ((r / n) << 16) | ((g / n) << 8) | (bl / n));
    }

    private static byte[] Cutout(int[] px, int w, int h, int bg, double tol, double feather)
    {
        var isBg = new bool[w * h];
        var queue = new Queue<int>();
        for (int x = 0; x < w; x++) { Seed(px, isBg, queue, x, bg, tol); Seed(px, isBg, queue, (h - 1) * w + x, bg, tol); }
        for (int y = 0; y < h; y++) { Seed(px, isBg, queue, y * w, bg, tol); Seed(px, isBg, queue, y * w + w - 1, bg, tol); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(px, isBg, queue, i - 1, bg, tol);
            if (x < w - 1) Seed(px, isBg, queue, i + 1, bg, tol);
            if (y > 0) Seed(px, isBg, queue, i - w, bg, tol);
            if (y < h - 1) Seed(px, isBg, queue, i + w, bg, tol);
        }

        var alpha = new byte[w * h];
        for (int i = 0; i < alpha.Length; i++)
        {
            if (isBg[i]) continue;
            int x = i % w, y = i / w;
            bool edge = (x > 0 && isBg[i - 1]) || (x < w - 1 && isBg[i + 1]) || (y > 0 && isBg[i - w]) || (y < h - 1 && isBg[i + w]);
            if (edge)
            {
                double d = Dist(px[i], bg);
                double t = Math.Max(0, Math.Min(1, (d - tol) / feather));
                alpha[i] = (byte)(80 + 175 * t);
            }
            else alpha[i] = 255;
        }
        return alpha;
    }

    private static void Seed(int[] px, bool[] isBg, Queue<int> q, int i, int bg, double tol)
    {
        if (isBg[i] || Dist(px[i], bg) > tol) return;
        isBg[i] = true;
        q.Enqueue(i);
    }

    /// <summary>Removes opaque islands smaller than minFraction of the largest one (stray text, specks).</summary>
    private static void KeepMainIslands(byte[] alpha, int w, int h, double minFraction)
    {
        var label = new int[w * h];
        var sizes = new List<int> { 0 };
        var stack = new Stack<int>();
        for (int s = 0; s < label.Length; s++)
        {
            if (alpha[s] == 0 || label[s] != 0) continue;
            int id = sizes.Count, size = 0;
            label[s] = id; stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(); size++;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (alpha[j] != 0 && label[j] == 0) { label[j] = id; stack.Push(j); }
                    }
            }
            sizes.Add(size);
        }
        int max = 0;
        foreach (int s in sizes) max = Math.Max(max, s);
        for (int i = 0; i < label.Length; i++)
            if (label[i] != 0 && sizes[label[i]] < max * minFraction) alpha[i] = 0;
    }

    /// <summary>
    /// Keeps only a circle as tall as the opaque area, anchored at its left edge. For round icons
    /// that overlap something on their right (e.g. the coin over its counter pill).
    /// </summary>
    private static void CircleMask(byte[] alpha, int w, int h)
    {
        int x0 = w, y0 = h, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (alpha[y * w + x] > 8) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        if (y1 < 0) return;
        double r = (y1 - y0 + 1) / 2.0, cx = x0 + r, cy = y0 + r;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (d > r) alpha[y * w + x] = 0;
                else if (d > r - 1) alpha[y * w + x] = (byte)(alpha[y * w + x] * (r - d));
            }
    }

    private static double Dist(int a, int b)
    {
        int dr = ((a >> 16) & 255) - ((b >> 16) & 255);
        int dg = ((a >> 8) & 255) - ((b >> 8) & 255);
        int db = (a & 255) - (b & 255);
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static int Lum(int c) { return ((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255); }

    private static int[] ReadPixels(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * bmp.Width, bmp.Width);
        bmp.UnlockBits(data);
        return px;
    }

    private static void WritePixels(Bitmap bmp, int[] px)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < bmp.Height; y++)
            Marshal.Copy(px, y * bmp.Width, IntPtr.Add(data.Scan0, y * data.Stride), bmp.Width);
        bmp.UnlockBits(data);
    }

    /// <summary>Lays every PNG in a folder on a checkerboard so cut quality can be eyeballed.</summary>
    public static void ContactSheet(string dir, string outPath, int cell)
    {
        var files = System.IO.Directory.GetFiles(dir, "*.png");
        Array.Sort(files);
        int cols = 8, rows = (files.Length + cols - 1) / cols;
        using (var bmp = new Bitmap(cols * cell, rows * (cell + 16)))
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Arial", 9))
        {
            for (int y = 0; y < bmp.Height; y += 8)
                for (int x = 0; x < bmp.Width; x += 8)
                    g.FillRectangle(((x + y) / 8) % 2 == 0 ? Brushes.Magenta : Brushes.DarkCyan, x, y, 8, 8);
            for (int i = 0; i < files.Length; i++)
            {
                using (var img = Image.FromFile(files[i]))
                {
                    int cx = (i % cols) * cell, cy = (i / cols) * (cell + 16);
                    double s = Math.Min(1.0, Math.Min((cell - 4) / (double)img.Width, (cell - 4) / (double)img.Height));
                    g.DrawImage(img, cx + 2, cy + 2, (int)(img.Width * s), (int)(img.Height * s));
                    g.FillRectangle(Brushes.White, cx, cy + cell, cell, 16);
                    g.DrawString(System.IO.Path.GetFileNameWithoutExtension(files[i]), font, Brushes.Black, cx + 2, cy + cell + 1);
                }
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
    }
}
