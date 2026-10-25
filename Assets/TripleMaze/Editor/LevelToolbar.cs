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
        static int selectedLevel;
        static double nextCheck;
        static LevelToolbar(){EditorApplication.update+=Attach;}
        static void Attach()
        {
            if(EditorApplication.timeSinceStartup<nextCheck)return;
            nextCheck=EditorApplication.timeSinceStartup+1;
            if(controls!=null && controls.panel!=null)return;
            var type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
            if(type==null)return;
            var instances=Resources.FindObjectsOfTypeAll(type);
            if(instances.Length==0)return;
            var field=type.GetField("m_Root",BindingFlags.Instance|BindingFlags.NonPublic);
            var root=field?.GetValue(instances[0]) as VisualElement;
            var zone=root?.Q("ToolbarZoneRightAlign");
            if(zone==null || zone.Q(ElementName)!=null)return;
            controls=new VisualElement{name=ElementName};
            controls.style.flexDirection=FlexDirection.Row;controls.style.alignItems=Align.Center;controls.style.marginLeft=10;controls.style.marginRight=10;
            var label=new Label("关卡");label.style.marginRight=4;controls.Add(label);
            selectedLevel=EditorPrefs.GetInt("TripleMaze.DebugLevel",6);
            var input=new IMGUIContainer(()=>
            {
                GUI.SetNextControlName("TripleMazeLevelNumber");
                selectedLevel=EditorGUI.IntField(new Rect(0,0,52,20),selectedLevel);
                if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Return)
                {Apply();GUIUtility.keyboardControl=0;Event.current.Use();}
            }){focusable=true};
            input.style.width=52;input.style.height=20;input.style.flexShrink=0;
            input.RegisterCallback<MouseDownEvent>(e=>input.Focus());
            controls.Add(input);
            var go=new ToolbarButton(()=>Apply()){text="切换",tooltip="播放时立即切关，停止时设置启动关卡"};controls.Add(go);
            var previous=new ToolbarButton(()=>Jump(-1)){text="◀",tooltip="上一关"};controls.Add(previous);
            var next=new ToolbarButton(()=>Jump(1)){text="▶",tooltip="下一关"};controls.Add(next);
            zone.Insert(0,controls);
        }
        static void Apply(){selectedLevel=Mathf.Clamp(selectedLevel,1,LevelControl.LevelCount);LevelControl.SwitchTo(selectedLevel);}
        static void Jump(int delta){selectedLevel=Mathf.Clamp(selectedLevel+delta,1,LevelControl.LevelCount);Apply();}
    }
}
