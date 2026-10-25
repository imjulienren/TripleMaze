// 复用 ArrowSmash 图片色阶识别，适配 TripleMaze 编辑器。
using System.Collections.Generic;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public static class ColorGradeDetector
    {
        /// <summary>
        /// 从图片中识别色阶。返回全部角色（Background / Outline / Fill），调用方按 Role 过滤。
        /// </summary>
        public static List<ColorGrade> Detect(
            Texture2D source,
            int gridW, int gridH,
            int maxGrades,
            float whiteThreshold,
            float outlineThreshold,
            float mergeDistance,
            out List<string> warnings)
        {
            warnings = new List<string>();

            // ── Step 1: 采样彩色像素（已混白底）─────────────────────────
            var pixels = TemplateImageProcessor.SamplePixelsColor(source, gridW, gridH);

            // ── Step 2: 分离背景像素（亮度 > whiteThreshold）─────────────
            var nonBg = new List<Color32>(gridW * gridH);
            int bgCount = 0;
            for (int y = 0; y < gridH; y++)
            {
                for (int x = 0; x < gridW; x++)
                {
                    if (Luminance(pixels[x, y]) > whiteThreshold)
                        bgCount++;
                    else
                        nonBg.Add(pixels[x, y]);
                }
            }

            if (nonBg.Count == 0)
            {
                warnings.Add("图片中未检测到非白色区域");
                return BuildResultWithOnlyBackground(bgCount);
            }

            // ── Step 3: K-Means 聚类（Lab 空间，10 次迭代）───────────────
            int k = Mathf.Min(maxGrades, nonBg.Count);
            var centers = InitCenters(nonBg, k);
            var labels  = new int[nonBg.Count];

            for (int iter = 0; iter < 10; iter++)
            {
                bool changed = false;
                for (int i = 0; i < nonBg.Count; i++)
                {
                    int best = NearestCenter(nonBg[i], centers);
                    if (best != labels[i]) { labels[i] = best; changed = true; }
                }
                if (!changed && iter > 0) break;

                // 重新计算聚类中心（Lab 空间加权均值）
                var sumL = new float[k];
                var sumA = new float[k];
                var sumB = new float[k];
                var cnt  = new int[k];
                for (int i = 0; i < nonBg.Count; i++)
                {
                    ToLab(nonBg[i], out float L, out float a, out float b);
                    sumL[labels[i]] += L;
                    sumA[labels[i]] += a;
                    sumB[labels[i]] += b;
                    cnt[labels[i]]++;
                }
                for (int j = 0; j < k; j++)
                {
                    if (cnt[j] == 0) continue;
                    centers[j] = FromLab(sumL[j] / cnt[j], sumA[j] / cnt[j], sumB[j] / cnt[j]);
                }
            }

            // 统计每个聚类的像素数
            var clusterCounts = new int[k];
            for (int i = 0; i < nonBg.Count; i++)
                clusterCounts[labels[i]]++;

            // ── Step 4: 以 mergeDistance 为主驱动，合并近似色阶 ──────────
            // 工作列表：(代表色, 像素数)
            var grades = new List<(Color32 center, int count)>(k);
            for (int j = 0; j < k; j++)
                if (clusterCounts[j] > 0)
                    grades.Add((centers[j], clusterCounts[j]));

            bool anyMerged = true;
            while (anyMerged && grades.Count > 1)
            {
                anyMerged = false;
                FindClosestPair(grades, out int mi, out int mj, out float minDist);
                if (minDist < mergeDistance)
                {
                    MergeInto(grades, mi, mj);
                    anyMerged = true;
                }
            }

            // ── Step 5: 安全上限——若仍超过 maxGrades，强制继续合并 ──────
            while (grades.Count > maxGrades)
            {
                FindClosestPair(grades, out int mi, out int mj, out _);
                MergeInto(grades, mi, mj);
                warnings.Add($"Fill 色阶数超出上限 {maxGrades}，已强制合并相近色阶");
            }

            // ── Step 6: 分配角色，构建结果 ────────────────────────────────
            var result = new List<ColorGrade>(grades.Count + 1);

            if (bgCount > 0)
            {
                result.Add(new ColorGrade
                {
                    Representative = new Color32(255, 255, 255, 255),
                    PixelCount     = bgCount,
                    Role           = ColorGradeRole.Background,
                    Label          = "Background",
                });
            }

            int fillIndex = 0;
            foreach (var (center, count) in grades)
            {
                bool isOutline = Luminance(center) < outlineThreshold;
                result.Add(new ColorGrade
                {
                    Representative = center,
                    PixelCount     = count,
                    Role           = isOutline ? ColorGradeRole.Outline : ColorGradeRole.Fill,
                    Label          = isOutline ? "Outline" : $"Grade_{fillIndex++}",
                });
            }

            result.Sort((x, y) => y.PixelCount.CompareTo(x.PixelCount));
            return result;
        }

        // ── Lab 转换 ─────────────────────────────────────────────────────

        private static void ToLab(Color32 c, out float L, out float a, out float b)
        {
            float r  = SRGBToLinear(c.r / 255f);
            float g  = SRGBToLinear(c.g / 255f);
            float bl = SRGBToLinear(c.b / 255f);

            // Linear RGB → XYZ (D65)
            float X = 0.4124564f * r + 0.3575761f * g + 0.1804375f * bl;
            float Y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * bl;
            float Z = 0.0193339f * r + 0.1191920f * g + 0.9503041f * bl;

            // XYZ → Lab (D65 白点: 0.95047, 1.0, 1.08883)
            float fx = LabF(X / 0.95047f);
            float fy = LabF(Y / 1.00000f);
            float fz = LabF(Z / 1.08883f);

            L = 116f * fy - 16f;
            a = 500f * (fx - fy);
            b = 200f * (fy - fz);
        }

        private static Color32 FromLab(float L, float a, float b)
        {
            float fy = (L + 16f) / 116f;
            float fx = a / 500f + fy;
            float fz = fy - b / 200f;

            float X = 0.95047f * LabFInv(fx);
            float Y = 1.00000f * LabFInv(fy);
            float Z = 1.08883f * LabFInv(fz);

            // XYZ → Linear RGB
            float r  =  3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z;
            float g  = -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z;
            float bl =  0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(LinearToSRGB(r)  * 255), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(LinearToSRGB(g)  * 255), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(LinearToSRGB(bl) * 255), 0, 255),
                255);
        }

        private static float LabF(float t)
        {
            const float delta = 6f / 29f;
            return t > delta * delta * delta
                ? Mathf.Pow(t, 1f / 3f)
                : t / (3f * delta * delta) + 4f / 29f;
        }

        private static float LabFInv(float t)
        {
            const float delta = 6f / 29f;
            return t > delta ? t * t * t : 3f * delta * delta * (t - 4f / 29f);
        }

        private static float SRGBToLinear(float c) =>
            c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        private static float LinearToSRGB(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.0031308f ? 12.92f * c : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        }

        // ── 距离与辅助 ───────────────────────────────────────────────────

        // 归一化 Lab 欧氏距离（L/100, a/100, b/100），与文档 mergeDistance 单位一致
        private static float LabDist(Color32 c1, Color32 c2)
        {
            ToLab(c1, out float L1, out float a1, out float b1);
            ToLab(c2, out float L2, out float a2, out float b2);
            float dL = (L1 - L2) / 100f;
            float da = (a1 - a2) / 100f;
            float db = (b1 - b2) / 100f;
            return Mathf.Sqrt(dL * dL + da * da + db * db);
        }

        private static float Luminance(Color32 c) =>
            (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private static int NearestCenter(Color32 pixel, Color32[] centers)
        {
            int   best     = 0;
            float bestDist = float.MaxValue;
            for (int j = 0; j < centers.Length; j++)
            {
                float d = LabDist(pixel, centers[j]);
                if (d < bestDist) { bestDist = d; best = j; }
            }
            return best;
        }

        // 固定种子保证同一图片每次结果一致
        private static Color32[] InitCenters(List<Color32> pixels, int k)
        {
            var rnd     = new System.Random(42);
            var used    = new System.Collections.Generic.HashSet<int>();
            var centers = new Color32[k];
            for (int j = 0; j < k; j++)
            {
                int idx;
                do { idx = rnd.Next(pixels.Count); } while (used.Contains(idx));
                used.Add(idx);
                centers[j] = pixels[idx];
            }
            return centers;
        }

        private static void FindClosestPair(
            List<(Color32 center, int count)> list,
            out int mi, out int mj, out float minDist)
        {
            mi = 0; mj = 1; minDist = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    float d = LabDist(list[i].center, list[j].center);
                    if (d < minDist) { minDist = d; mi = i; mj = j; }
                }
            }
        }

        // 将 mj 合并入 mi（像素数加权均值），然后移除 mj
        private static void MergeInto(List<(Color32 center, int count)> list, int mi, int mj)
        {
            var (ca, na) = list[mi];
            var (cb, nb) = list[mj];
            int total = na + nb;
            var merged = new Color32(
                (byte)((ca.r * na + cb.r * nb) / total),
                (byte)((ca.g * na + cb.g * nb) / total),
                (byte)((ca.b * na + cb.b * nb) / total),
                255);
            list[mi] = (merged, total);
            list.RemoveAt(mj);
        }

        private static List<ColorGrade> BuildResultWithOnlyBackground(int bgCount)
        {
            return new List<ColorGrade>
            {
                new ColorGrade
                {
                    Representative = new Color32(255, 255, 255, 255),
                    PixelCount     = bgCount,
                    Role           = ColorGradeRole.Background,
                    Label          = "Background",
                }
            };
        }
    }
}