using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TripleMaze.Editor
{
    public class TripleMazeLevelEditor : EditorWindow
    {
        [SerializeField] string snapshot;
        LevelData level=new LevelData();
        List<Vector2Int> draft=new List<Vector2Int>();
        readonly Stack<string> undo=new Stack<string>(),redo=new Stack<string>();
        string path="",report="首点为箭头头部，逐点绘制折线，Enter 完成，Esc 取消。";
        int selected=-1,color=1,tool;float zoom=1;Vector2 pan,scroll;
        readonly string[] tools={"绘制","选择","移动","擦除"};
        [Serializable] class Wrapper {public LevelData levelConfig;}
        [MenuItem("TripleMaze/关卡编辑器")]
        public static void Open(){var w=GetWindow<TripleMazeLevelEditor>();w.titleContent=new GUIContent("TripleMaze 关卡编辑器");w.minSize=new Vector2(960,680);}
        void OnEnable(){if(!string.IsNullOrEmpty(snapshot))level=JsonUtility.FromJson<LevelData>(snapshot);}
        void OnDisable(){snapshot=JsonUtility.ToJson(level);}
        void Record(){undo.Push(JsonUtility.ToJson(level));redo.Clear();snapshot=JsonUtility.ToJson(level);}
        void Restore(Stack<string> from,Stack<string> to){if(from.Count==0)return;to.Push(JsonUtility.ToJson(level));level=JsonUtility.FromJson<LevelData>(from.Pop());selected=-1;draft.Clear();Repaint();}
        void OnGUI()
        {
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if(GUILayout.Button("新建",EditorStyles.toolbarButton)){Record();level=new LevelData();selected=-1;draft.Clear();path="";}
                if(GUILayout.Button("打开 JSON",EditorStyles.toolbarButton))Load();
                if(GUILayout.Button("保存 JSON",EditorStyles.toolbarButton))Save();
                if(GUILayout.Button("撤销",EditorStyles.toolbarButton))Restore(undo,redo);
                if(GUILayout.Button("重做",EditorStyles.toolbarButton))Restore(redo,undo);
                if(GUILayout.Button("图片 / 快速生成",EditorStyles.toolbarButton))Generation.LevelGenerationWindow.Open(this,level.Copy());
                if(GUILayout.Button("难度报告",EditorStyles.toolbarButton))Generation.LevelGenerationWindow.AnalyzeCurrent(this,level.Copy());
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("校验与求解",EditorStyles.toolbarButton))Validate();
                if(GUILayout.Button("试玩当前关卡",EditorStyles.toolbarButton))Preview();
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
                {
                    float oldLabelWidth=EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth=80;
                    GUILayout.Space(12);GUILayout.Label("关卡设置",EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    int id=EditorGUILayout.DelayedIntField("编号",level.levelId);
                    string desc=EditorGUILayout.TextField("标题",level.description);
                    int w=Mathf.Clamp(EditorGUILayout.DelayedIntField("宽",level.gridWidth),2,80);
                    int h=Mathf.Clamp(EditorGUILayout.DelayedIntField("高",level.gridHeight),2,80);
                    int slots=Mathf.Clamp(EditorGUILayout.DelayedIntField("收集槽",level.slotCapacity),3,9);
                    if(EditorGUI.EndChangeCheck()){Record();level.levelId=id;level.description=desc;level.gridWidth=w;level.gridHeight=h;level.slotCapacity=slots;}
                    GUILayout.Space(15);tool=GUILayout.SelectionGrid(tool,tools,2);GUILayout.Space(12);
                    GUILayout.Label("箭头颜色");
                    using(new EditorGUILayout.HorizontalScope())
                    {for(int i=1;i<=6;i++){var old=GUI.backgroundColor;GUI.backgroundColor=PuzzleModel.DisplayColor(level,i);if(GUILayout.Toggle(color==i,i.ToString(),"Button",GUILayout.Width(36)))color=i;GUI.backgroundColor=old;}}
                    if(selected>=0 && selected<level.initialSegments.Count)
                    {
                        GUILayout.Label($"已选中第 {selected+1} 枚箭头");
                        if(GUILayout.Button("应用颜色")){Record();level.initialSegments[selected].colorDisplayIndex=color;}
                        if(GUILayout.Button("反转头尾")){Record();level.initialSegments[selected].points.Reverse();}
                        if(GUILayout.Button("删除箭头")){Record();level.initialSegments.RemoveAt(selected);selected=-1;}
                    }
                    if(draft.Count>0 && GUILayout.Button("完成绘制 (Enter)"))Finish();
                    GUILayout.Space(12);zoom=EditorGUILayout.Slider("缩放",zoom,.35f,2f);
                    if(GUILayout.Button("居中视图")){pan=Vector2.zero;zoom=1;}
                    GUILayout.Label($"共 {level.initialSegments.Count} 枚箭头");
                    foreach(var group in level.initialSegments.GroupBy(a=>a.colorDisplayIndex).OrderBy(g=>g.Key))GUILayout.Label($"颜色 {group.Key}: {group.Count()} 枚");
                    GUILayout.Space(10);EditorGUILayout.HelpBox("首点是箭头头部。\n点击网格逐点绘制，Enter 完成。\nEsc 取消，Delete 删除选中箭头。\n移动工具拖动箭头，中键平移视图。\n编辑器兼容 ArrowSmash 正式 JSON 的 levelConfig 与 initialSegments。",MessageType.Info);
                    scroll=EditorGUILayout.BeginScrollView(scroll);GUILayout.Label(report,EditorStyles.wordWrappedLabel);EditorGUILayout.EndScrollView();
                    EditorGUIUtility.labelWidth=oldLabelWidth;
                }
                var rect=GUILayoutUtility.GetRect(400,400,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));DrawCanvas(rect);
            }
            if(Event.current.type==EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if(Event.current.keyCode==KeyCode.Return){Finish();Event.current.Use();}
                if(Event.current.keyCode==KeyCode.Escape){draft.Clear();Repaint();Event.current.Use();}
                if(Event.current.keyCode==KeyCode.Delete && selected>=0){Record();level.initialSegments.RemoveAt(selected);selected=-1;Event.current.Use();}
            }
            snapshot=JsonUtility.ToJson(level);
        }
        float cell;Vector2 origin;Vector2Int dragStart;bool moving;
        Vector2 ToScreen(Vector2Int p)=>origin+new Vector2(p.x,p.y)*cell;
        Vector2Int ToGrid(Vector2 p)=>new Vector2Int(Mathf.RoundToInt((p.x-origin.x)/cell),Mathf.RoundToInt((p.y-origin.y)/cell));
        void DrawCanvas(Rect r)
        {
            EditorGUI.DrawRect(r,new Color(.15f,.18f,.23f));
            cell=Mathf.Min((r.width-70)/level.gridWidth,(r.height-70)/level.gridHeight)*zoom;
            origin=r.center-new Vector2(level.gridWidth-1,level.gridHeight-1)*cell*.5f+pan;
            GUI.BeginClip(r);Handles.BeginGUI();
            Vector2 offset=r.position;
            for(int y=0;y<level.gridHeight;y++)for(int x=0;x<level.gridWidth;x++)
            {var p=ToScreen(new Vector2Int(x,y))-offset;Handles.color=new Color(1,1,1,.13f);Handles.DrawSolidDisc(p,Vector3.forward,1.4f);}
            for(int i=0;i<level.initialSegments.Count;i++)
            {var a=level.initialSegments[i];DrawArrow(a.points,i==selected?Color.white:PuzzleModel.DisplayColor(level,a.colorDisplayIndex),i==selected?5:3,offset);}
            DrawArrow(draft,PuzzleModel.DisplayColor(level,color),4,offset);
            Handles.EndGUI();GUI.EndClip();
            var e=Event.current;if(!r.Contains(e.mousePosition))return;
            if(e.type==EventType.ScrollWheel){zoom=Mathf.Clamp(zoom-e.delta.y*.04f,.35f,2);e.Use();Repaint();}
            if(e.type==EventType.MouseDrag && e.button==2){pan+=e.delta;e.Use();Repaint();}
            var grid=ToGrid(e.mousePosition);
            if(e.type==EventType.MouseDown && e.button==0)
            {
                if(tool==0)
                {
                    if(grid.x>=0 && grid.y>=0 && grid.x<level.gridWidth && grid.y<level.gridHeight)
                    {
                        if(draft.Count==0 || (draft[draft.Count-1]!=grid && (draft[draft.Count-1].x==grid.x || draft[draft.Count-1].y==grid.y)))draft.Add(grid);
                        else report="只支持水平或竖直线段。";
                    }
                }
                else
                {
                    selected=Pick(e.mousePosition);
                    if(selected>=0 && tool==3){Record();level.initialSegments.RemoveAt(selected);selected=-1;}
                    if(selected>=0 && tool==2){Record();dragStart=grid;moving=true;}
                }
                e.Use();Repaint();
            }
            if(e.type==EventType.MouseDrag && moving && selected>=0)
            {
                var delta=grid-dragStart;var a=level.initialSegments[selected];
                if(a.points.All(p=>p.x+delta.x>=0 && p.x+delta.x<level.gridWidth && p.y+delta.y>=0 && p.y+delta.y<level.gridHeight))
                {for(int j=0;j<a.points.Count;j++)a.points[j]+=delta;dragStart=grid;}
                e.Use();Repaint();
            }
            if(e.type==EventType.MouseUp)moving=false;
        }
        int Pick(Vector2 p)
        {
            float best=10;int result=-1;
            for(int i=0;i<level.initialSegments.Count;i++)
            {var a=level.initialSegments[i];for(int j=1;j<a.points.Count;j++){var x=ToScreen(a.points[j-1]);var d=ToScreen(a.points[j])-x;float t=Mathf.Clamp01(Vector2.Dot(p-x,d)/d.sqrMagnitude);float dist=Vector2.Distance(p,x+d*t);if(dist<best){best=dist;result=i;}}}
            return result;
        }
        void DrawArrow(List<Vector2Int> points,Color color,float width,Vector2 offset)
        {
            if(points.Count<2)return;Handles.color=color;var ps=points.Select(p=>(Vector3)(ToScreen(p)-offset)).ToArray();Handles.DrawAAPolyLine(width,ps);
            Vector3 dir=(ps[0]-ps[1]).normalized,side=new Vector3(-dir.y,dir.x);float s=Mathf.Clamp(cell*.35f,6,15);
            Handles.DrawAAConvexPolygon(ps[0]+dir*s*.3f,ps[0]-dir*s+side*s*.65f,ps[0]-dir*s-side*s*.65f);
        }
        public void AdoptGenerated(LevelData generated,string summary)
        {Record();level=generated.Copy();path="";selected=-1;draft.Clear();report=summary;snapshot=JsonUtility.ToJson(level);Repaint();}
        void Finish(){if(draft.Count<2)return;Record();level.initialSegments.Add(new ArrowData{colorDisplayIndex=color,points=new List<Vector2Int>(draft)});selected=level.initialSegments.Count-1;draft.Clear();Repaint();}
        void Load()
        {
            var p=EditorUtility.OpenFilePanel("打开关卡 JSON","Assets/TripleMaze/Resources/Levels","json");if(string.IsNullOrEmpty(p))return;
            try{var json=File.ReadAllText(p);var d=json.Contains("\"levelConfig\"")?JsonUtility.FromJson<Wrapper>(json).levelConfig:JsonUtility.FromJson<LevelData>(json);if(d==null || d.initialSegments==null)throw new Exception("没有 initialSegments");if(d.slotCapacity==0)d.slotCapacity=7;Record();level=d;path=p;selected=-1;draft.Clear();report="已载入："+p;}
            catch(Exception ex){report="读取失败："+ex.Message;}
        }
        void Save()
        {
            var errors=PuzzleModel.Validate(level);if(errors.Count>0){report=string.Join("\n",errors);return;}
            var p=EditorUtility.SaveFilePanel("保存关卡",string.IsNullOrEmpty(path)?Application.dataPath+"/TripleMaze/Resources/Levels":Path.GetDirectoryName(path),$"level_{level.levelId:00}","json");if(string.IsNullOrEmpty(p))return;
            File.WriteAllText(p,JsonUtility.ToJson(level,true));path=p;AssetDatabase.Refresh();report="已保存："+p;
        }
        void Validate()
        {
            var errors=PuzzleModel.Validate(level);if(errors.Count>0){report=string.Join("\n",errors);return;}
            bool limit;var solution=PuzzleModel.Solve(level,25000,out limit);
            report=solution!=null?"找到可通关序列（箭头序号）：\n"+string.Join(" → ",solution.Select(i=>(i+1).ToString())):limit?"搜索达到 25,000 状态预算，尚不能确认是否有解。":"已穷尽搜索，当前颜色与阻挡组合无解。";
        }
        void Preview()
        {
            var errors=PuzzleModel.Validate(level);if(errors.Count>0){report=string.Join("\n",errors);return;}
            if(EditorApplication.isPlaying){var game=FindObjectOfType<TripleMazeGame>();if(game)game.StartLevel(level.Copy());return;}
            TripleMazeGame.PreviewLevel=level.Copy();SessionState.SetString("TripleMaze.Preview",JsonUtility.ToJson(level));EditorApplication.isPlaying=true;
        }
    }
}
