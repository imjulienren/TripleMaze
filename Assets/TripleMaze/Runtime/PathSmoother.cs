// 复用 ArrowSmash PathSmoother，命名空间适配为 TripleMaze。
using System.Collections.Generic;
using UnityEngine;

namespace TripleMaze
{
    /*
     * ── CircleArc 拐角圆弧计算原理 ────────────────────────────────────────
     *
     *  以路径 a → b → c 在 b 处拐角为例（θ = 两段方向夹角）：
     *
     *         c
     *         │
     *     O───q     ← O 在拐角内侧
     *     │   │       p、q 为切点，距顶点 b 均为 tLen（切线长）
     *     │   │       圆心 O 到 p、q 的距离均为 R（圆弧半径）
     * a───p───b  且 O→p ⊥ 线段 ab，O→q ⊥ 线段 bc
     *      ←→
     *        tLen
     *
     *  在直角三角形 b-p-O 中（直角在切点 p）：
     *
     *    · 内角 θ = Vector3.Angle(ab, bc)（两方向向量夹角）
     *    · 内切圆的"内角"= 两射线 b→a 与 b→c 夹角 = 180° - θ
     *    · b 处角 = (180° - θ) / 2 = 90° - θ/2
     *
     *     O
     *     │╲
     *  R  │  ╲  斜边 = |bO|
     *     │    ╲
     *     p─────b
     *      tLen
     *     （直角在 p）
     *
     *    tan(90° - θ/2) = R / tLen
     *    → cot(θ/2)    = R / tLen
     *    → R            = tLen / tan(θ/2)          ← 圆弧半径
     *
     *    cos(90° - θ/2) = tLen / |bO|
     *    → sin(θ/2)     = tLen / |bO|
     *    → |bO|         = tLen / sin(θ/2)          ← 圆心到顶点距离
     *
     *  圆心方向（内分角，即弯曲内侧）：
     *    bisector = normalize(-dirIn + dirOut)
     *    O        = b + bisector * (tLen / sin(θ/2))
     *
     * ─────────────────────────────────────────────────────────────────── */
    public static class PathSmoother
    {
        public enum CornerMode
        {
            /// <summary>二次 Bezier 弧，拐角"软"，曲率不均匀</summary>
            Bezier,
            /// <summary>真实圆弧，等曲率，内外边缘均为同心圆弧</summary>
            CircleArc,
        }

        private const float Eps = 1e-6f;

        // 单线程复用缓冲区，避免每次调用产生 GC 分配
        private static readonly List<Vector3> SSegStart     = new List<Vector3>();
        private static readonly List<Vector3> SSegEnd       = new List<Vector3>();
        private static readonly List<bool>    SHasCorner    = new List<bool>();
        private static readonly List<Vector3> SCornerP      = new List<Vector3>(); // 入切点
        private static readonly List<Vector3> SCornerQ      = new List<Vector3>(); // 出切点
        private static readonly List<Vector3> SCornerCtrl   = new List<Vector3>(); // Bezier 控制点（原拐角顶点）
        private static readonly List<Vector3> SCornerCenter = new List<Vector3>(); // 圆弧圆心
        private static readonly List<float>   SCornerRadius = new List<float>();   // 圆弧半径

        /// <summary>
        /// 将折线 points 平滑化后写入 path。
        /// 直线段只输出端点，拐角处按 mode 插入弧线采样点。
        /// </summary>
        public static void SmoothPath(ref List<Vector3> path, List<Vector3> points,float lineWidth = 0f,
            float cornerRadius = 0.25f, int arcSamples = 6, CornerMode mode = CornerMode.CircleArc)
        {
            path.Clear();
            if (points == null || points.Count < 2)
                return;

            ComputeCorners(points, cornerRadius, lineWidth);
            BuildSimplePath(path, points, arcSamples, mode);
        }

        // ── Phase 1：裁剪直线段，计算每个拐角的切点、Bezier 控制点和圆弧圆心/半径 ──
        private static void ComputeCorners(List<Vector3> points, float cornerRadius, float minR = 0f)
        {
            int segCount   = points.Count - 1;
            int pointCount = points.Count;

            SSegStart.Clear();
            SSegEnd.Clear();
            SHasCorner.Clear();
            SCornerP.Clear();
            SCornerQ.Clear();
            SCornerCtrl.Clear();
            SCornerCenter.Clear();
            SCornerRadius.Clear();

            for (int i = 0; i < segCount; i++)
            {
                SSegStart.Add(points[i]);
                SSegEnd.Add(points[i + 1]);
            }
            for (int j = 0; j < pointCount; j++)
            {
                SHasCorner.Add(false);
                SCornerP.Add(default);
                SCornerQ.Add(default);
                SCornerCtrl.Add(default);
                SCornerCenter.Add(default);
                SCornerRadius.Add(0f);
            }

            if (cornerRadius <= 0f || pointCount < 3)
                return;

            for (int j = 1; j < pointCount - 1; j++)
            {
                Vector3 a = points[j - 1], b = points[j], c = points[j + 1];
                Vector3 ab = b - a, bc = c - b;
                float lenAb = ab.magnitude, lenBc = bc.magnitude;
                if (lenAb < Eps || lenBc < Eps) continue;

                float angle = Vector3.Angle(ab, bc);
                if (angle < 0.5f || angle > 179.5f) continue;

                float tangentLen = Mathf.Min(cornerRadius, lenAb * 0.5f, lenBc * 0.5f);
                if (tangentLen <= Eps) continue;

                Vector3 dirIn  = ab.normalized;
                Vector3 dirOut = bc.normalized;
                Vector3 p      = b - dirIn  * tangentLen;
                Vector3 q      = b + dirOut * tangentLen;
                if ((p - q).sqrMagnitude < Eps * Eps) continue;

                // 圆弧数据：在 b 处直角三角形中，b 角 = 90° - θ/2
                // → R = tangentLen / tan(θ/2)，|b→center| = tangentLen / sin(θ/2)
                float halfAngleRad = angle * 0.5f * Mathf.Deg2Rad;
                float tanHalf      = Mathf.Tan(halfAngleRad);
                float R            = tangentLen / tanHalf;

                // 保证内边缘半径 ≥ lineWidth/2，避免锐角拐角（如135°偏转）内角塌陷成尖点
                // minR = lineWidth，内边缘半径 = R - lineWidth/2 ≥ lineWidth/2
                if (minR > 0f && R < minR)
                {
                    // 受线段半长约束，取能达到的最大 R
                    float maxTangentLen = Mathf.Min(lenAb * 0.5f, lenBc * 0.5f);
                    float maxR          = maxTangentLen / tanHalf;
                    R          = Mathf.Min(minR, maxR);
                    tangentLen = R * tanHalf;
                    p          = b - dirIn  * tangentLen;
                    q          = b + dirOut * tangentLen;
                    if ((p - q).sqrMagnitude < Eps * Eps) continue;
                }

                Vector3 bisector = (-dirIn + dirOut).normalized;
                Vector3 center   = b + bisector * (tangentLen / Mathf.Sin(halfAngleRad));

                SSegEnd[j - 1]   = p;
                SSegStart[j]     = q;
                SHasCorner[j]    = true;
                SCornerP[j]      = p;
                SCornerQ[j]      = q;
                SCornerCtrl[j]   = b;      // Bezier 控制点 = 原顶点
                SCornerCenter[j] = center;
                SCornerRadius[j] = R;
            }
        }

        // ── Phase 2：直线只输出端点，拐角按 mode 插入弧线采样点 ──
        private static void BuildSimplePath(List<Vector3> path, List<Vector3> points,
            int arcSamples, CornerMode mode)
        {
            int segCount = points.Count - 1;
            path.Add(SSegStart[0]);

            for (int i = 0; i < segCount; i++)
            {
                AddToPath(path, SSegEnd[i]);

                int corner = i + 1;
                if (corner >= points.Count - 1 || !SHasCorner[corner])
                    continue;

                Vector3 p = SCornerP[corner];
                Vector3 q = SCornerQ[corner];
                int steps = Mathf.Max(1, arcSamples);

                if (mode == CornerMode.CircleArc)
                {
                    Vector3 center = SCornerCenter[corner];
                    float   R      = SCornerRadius[corner];
                    for (int k = 1; k <= steps; k++)
                        AddToPath(path, EvalCircleArc(center, p, q, R, (float)k / steps));
                }
                else
                {
                    Vector3 ctrl = SCornerCtrl[corner];
                    for (int k = 1; k <= steps; k++)
                        AddToPath(path, EvalQuadBezier(p, ctrl, q, (float)k / steps));
                }
            }
        }

        // 真实圆弧：在圆上从 from 沿最短弧 Slerp 到 to
        private static Vector3 EvalCircleArc(Vector3 center, Vector3 from, Vector3 to, float radius, float t)
        {
            Vector3 fromDir = (from - center).normalized;
            Vector3 toDir   = (to   - center).normalized;
            return center + Vector3.Slerp(fromDir, toDir, t) * radius;
        }

        // 二次 Bezier 弧
        private static Vector3 EvalQuadBezier(Vector3 p, Vector3 ctrl, Vector3 q, float t)
        {
            float u = 1f - t;
            return u * u * p + 2f * u * t * ctrl + t * t * q;
        }

        private static void AddToPath(List<Vector3> path, Vector3 pt)
        {
            if ((pt - path[^1]).sqrMagnitude > 1e-8f)
                path.Add(pt);
        }
    }
}