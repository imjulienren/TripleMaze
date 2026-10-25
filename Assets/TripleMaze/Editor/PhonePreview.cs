using System;
using System.Reflection;
using UnityEditor;

namespace TripleMaze.Editor
{
    public static class PhonePreview
    {
        [MenuItem("TripleMaze/手机竖屏预览 720x1280")]
        public static void Select()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            var group = sizesType.GetProperty("currentGroup", flags).GetValue(sizes);
            var groupType = group.GetType();
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
            int count = (int)groupType.GetMethod("GetTotalCount", flags).Invoke(group, null);
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                var size = groupType.GetMethod("GetGameViewSize", flags).Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width", flags).GetValue(size) == 720 &&
                    (int)sizeType.GetProperty("height", flags).GetValue(size) == 1280 &&
                    sizeType.GetProperty("sizeType", flags).GetValue(size).ToString() == "FixedResolution")
                { index = i; break; }
            }
            if (index < 0)
            {
                var ctor = sizeType.GetConstructor(flags, null, new[] { kindType, typeof(int), typeof(int), typeof(string) }, null);
                var size = ctor.Invoke(new[] { Enum.Parse(kindType, "FixedResolution"), (object)720, 1280, "TripleMaze 手机竖屏" });
                groupType.GetMethod("AddCustomSize", flags).Invoke(group, new[] { size });
                index = count;
            }
            var viewType = assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", flags).SetValue(window, index);
            window.Show();
            window.Repaint();
        }
    }
}
