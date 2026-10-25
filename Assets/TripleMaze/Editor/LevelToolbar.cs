using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TripleMaze.Editor
{
    [InitializeOnLoad]
    public static class LevelToolbar
    {
        const string ElementName="TripleMaze-Level-Control";
        static VisualElement controls;
        static ToolbarButton winButton;
        static double nextCheck;
        static LevelToolbar(){EditorApplication.update+=Attach;}
        static void Attach()
        {
            if(EditorApplication.timeSinceStartup<nextCheck)return;
            nextCheck=EditorApplication.timeSinceStartup+1;
            if(winButton!=null)winButton.SetEnabled(LevelControl.CanWinCurrentLevel());
            if(controls!=null && controls.panel!=null)return;
            var type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
            if(type==null)return;
            var instances=Resources.FindObjectsOfTypeAll(type);
            if(instances.Length==0)return;
            var field=type.GetField("m_Root",BindingFlags.Instance|BindingFlags.NonPublic);
            var root=field?.GetValue(instances[0]) as VisualElement;
            var zone=root?.Q("ToolbarZoneRightAlign");
            if(zone==null)return;
            // 脚本重载后替换遗留控件，确保旧的 IMGUI 输入框不会继续留在工具栏。
            zone.Q(ElementName)?.RemoveFromHierarchy();
            controls=new VisualElement{name=ElementName};
            controls.style.flexDirection=FlexDirection.Row;controls.style.alignItems=Align.Center;controls.style.marginLeft=10;controls.style.marginRight=10;
            var previous=new ToolbarButton(()=>Jump(-1)){text="◀",tooltip="上一关"};controls.Add(previous);
            var next=new ToolbarButton(()=>Jump(1)){text="▶",tooltip="下一关"};controls.Add(next);
            winButton=new ToolbarButton(LevelControl.WinCurrentLevel){text="一键通关",tooltip="快速连续飞出箭头并加速三消，目标约 4 秒清空当前关卡"};
            winButton.SetEnabled(LevelControl.CanWinCurrentLevel());controls.Add(winButton);
            zone.Insert(0,controls);
        }
        static void Jump(int delta)
        {
            int current=EditorPrefs.GetInt("TripleMaze.DebugLevel",1);
            if(EditorApplication.isPlaying){var game=Object.FindObjectOfType<TripleMazeGame>();if(game)current=game.LevelNumber;}
            LevelControl.SwitchTo(Mathf.Clamp(current+delta,1,LevelControl.LevelCount));
        }
    }
}
