// 复用 ArrowSmash 图片色阶识别，适配 TripleMaze 编辑器。
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public enum ColorGradeRole
    {
        Fill,        // 有效色块：参与遮罩生成、轮廓提取、填充
        Outline,     // 深色描边：自动剔除，不参与任何后续处理
        Background,  // 白色背景：自动剔除
    }

    public class ColorGrade
    {
        public Color32        Representative;
        public int            PixelCount;
        public ColorGradeRole Role;
        public string         Label;
    }
}