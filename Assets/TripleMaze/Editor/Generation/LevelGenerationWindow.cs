using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public sealed class LevelGenerationWindow : EditorWindow
    {
        [SerializeField] GenerationTemplate template;
        [SerializeField] GenerationSettings settings=new GenerationSettings();
        [SerializeField] string imagePath="",message="选择图片，或在画布上绘制轮廓，再选择难度生成候选。";
        [SerializeField] int edge=28,backgroundMode,brushMode,brushSize=1,brushColor,shape=1,levelId=101;
        [SerializeField] float backgroundThreshold=.15f;
        [SerializeField] bool stripOutline,keepAspect=true,replacePolygon=true,showMask=true,sourceExpanded=true,advanced;
        [SerializeField] Texture2D image;
        TripleMazeLevelEditor owner;
        LevelData baseLevel;
        GenerationJob job;
        [SerializeField] List<GeneratedCandidate> candidates=new List<GeneratedCandidate>();
        [SerializeField] int selectedSeed=int.MinValue;
        GeneratedCandidate selected;
        DifficultyReport currentReport;
        List<Vector2Int> polygon=new List<Vector2Int>();
        Stack<string> undo=new Stack<string>(),redo=new Stack<string>();
        Vector2 sideScroll,pan;float zoom=1,cell;Vector2 origin;bool stroke,draggedPolygon;bool ownsImage;
        [SerializeField] string generationTemplateJson,generationSettingsJson;
        [MenuItem("TripleMaze/图片与难度生成器")]
        public static void OpenMenu()=>Open(null,null);
        public static void Open(TripleMazeLevelEditor owner,LevelData level)
        {var w=GetWindow<LevelGenerationWindow>();w.titleContent=new GUIContent("TripleMaze 快速生成");w.minSize=new Vector2(1120,780);w.owner=owner;w.baseLevel=level;if(level!=null)w.levelId=level.levelId;w.Show();}
        public static void AnalyzeCurrent(TripleMazeLevelEditor owner,LevelData level)
        {Open(owner,level);var w=GetWindow<LevelGenerationWindow>();w.Analyze(level);}
        void OnEnable()
        {
            if(template==null || template.mask.Count!=template.width*template.height)template=TemplateImageProcessor.Shape(24,28,1);
            if(!image && !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)){var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(texture.LoadImage(File.ReadAllBytes(imagePath))){texture.name=Path.GetFileNameWithoutExtension(imagePath);image=texture;ownsImage=true;}else DestroyImmediate(texture);}
            else if(image && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(image)))ownsImage=true;
            if(candidates.Count>0){selected=candidates.FirstOrDefault(c=>c.seed==selectedSeed)??candidates[0];currentReport=selected.difficulty;}
            EditorApplication.update+=Tick;
        }
        void OnDisable(){if(selected!=null)selectedSeed=selected.seed;EditorApplication.update-=Tick;job=null;}
        void OnDestroy(){if(ownsImage && image)DestroyImmediate(image);}
        void Tick()
        {
            if(job==null)return;
            try{job.Step();message=job.LastMessage;candidates=new List<GeneratedCandidate>(job.Candidates);if(job.Finished){job=null;if(candidates.Count>0){selected=candidates[0];currentReport=selected.difficulty;message=settings.fillImage?"生成完成。图片优先填满，绿色标签表示遮挡难度命中。":"生成完成。绿色标签表示难度命中；未命中的候选保留真实分数。";}else message="未生成可用候选。"+message;}Repaint();}
            catch(Exception ex){job=null;message="生成失败："+ex.Message;Debug.LogException(ex);}
        }
        void Record(){undo.Push(JsonUtility.ToJson(template));redo.Clear();}
        void Restore(Stack<string> from,Stack<string> to){if(from.Count==0)return;to.Push(JsonUtility.ToJson(template));template=JsonUtility.FromJson<GenerationTemplate>(from.Pop());Invalidate();}
        void Invalidate(){selected=null;candidates.Clear();currentReport=null;message="轮廓或颜色已修改，请重新生成。";Repaint();}
        void OnGUI()
        {
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("图片 / 轮廓 → 难度筛选 → 可解关卡",EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using(new EditorGUI.DisabledScope(job!=null)){if(GUILayout.Button("撤销轮廓",EditorStyles.toolbarButton))Restore(undo,redo);if(GUILayout.Button("重做",EditorStyles.toolbarButton))Restore(redo,undo);if(GUILayout.Button("打开生成配方",EditorStyles.toolbarButton))LoadRecipe();}
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
                {
                    sideScroll=EditorGUILayout.BeginScrollView(sideScroll);
                    using(new EditorGUI.DisabledScope(job!=null))
                    {
                        DrawSource();EditorGUI.BeginChangeCheck();DrawSettings();if(EditorGUI.EndChangeCheck() && candidates.Count>0)Invalidate();
                        levelId=EditorGUILayout.IntField("采用 / 导出起始编号",levelId);
                        if(GUILayout.Button("按设置搜索可解候选",GUILayout.Height(34)))Begin();
                    }
                    if(job!=null){var r=GUILayoutUtility.GetRect(100,20);EditorGUI.ProgressBar(r,job.Completed/(float)Math.Max(1,settings.attempts),$"候选搜索 {job.Completed}/{settings.attempts}");if(GUILayout.Button("停止搜索，保留已有候选")){job=null;if(candidates.Count>0){selected=candidates[0];currentReport=selected.difficulty;}}}
                    EditorGUILayout.HelpBox(message,MessageType.Info);
                    DrawDifficulty();
                    EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {showMask=GUILayout.Toggle(showMask,"显示轮廓与图片配色",EditorStyles.toolbarButton);if(GUILayout.Button("居中",EditorStyles.toolbarButton)){pan=Vector2.zero;zoom=1;}GUILayout.Label($"{template.width} × {template.height}，有效格点 {template.Count}");}
                    var canvas=GUILayoutUtility.GetRect(400,420,GUILayout.ExpandHeight(true),GUILayout.ExpandWidth(true));DrawCanvas(canvas);
                    DrawCandidates();
                }
            }
            if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Return){FinishPolygon();Event.current.Use();}
            if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Escape){polygon.Clear();Event.current.Use();Repaint();}
        }
        void DrawSource()
        {
            sourceExpanded=EditorGUILayout.Foldout(sourceExpanded,"1  图片与轮廓",true);
            if(!sourceExpanded){GUILayout.Label(template.source,EditorStyles.wordWrappedLabel);GUILayout.Label($"{template.width}×{template.height}，{template.palette.Count} 个主色");return;}
            var chosen=(Texture2D)EditorGUILayout.ObjectField("图片资源",image,typeof(Texture2D),false);if(chosen!=image){if(ownsImage && image)DestroyImmediate(image);image=chosen;ownsImage=false;if(image)ReadImage();}
            using(new EditorGUILayout.HorizontalScope())
            {if(GUILayout.Button("选择本地 PNG / JPG")){string p=EditorUtility.OpenFilePanelWithFilters("选择轮廓图片","",new[]{"图片","png,jpg,jpeg"});if(!string.IsNullOrEmpty(p))LoadImage(p);}if(GUILayout.Button("重新识别"))ReadImage();}
            edge=EditorGUILayout.IntSlider("网格长边",edge,8,48);keepAspect=EditorGUILayout.Toggle("保持图片宽高比",keepAspect);
            backgroundMode=EditorGUILayout.Popup("背景识别",backgroundMode,new[]{"自动边角背景 / 透明","白色背景","只使用透明通道","不剔除背景"});
            backgroundThreshold=EditorGUILayout.Slider("背景容差",backgroundThreshold,.02f,.45f);stripOutline=EditorGUILayout.Toggle("剔除近黑描边",stripOutline);
            EditorGUILayout.HelpBox("这里识别图片的轮廓与色块，再生成新的箭头布局；不是恢复图片中已有箭头或语义抠图。复杂照片可先用画笔修正遮罩。",MessageType.None);
            using(new EditorGUILayout.HorizontalScope())
            {shape=EditorGUILayout.Popup(shape,new[]{"矩形","椭圆","菱形","爱心"});if(GUILayout.Button("建立轮廓")){Record();template=TemplateImageProcessor.Shape(edge,Mathf.Min(48,Mathf.RoundToInt(edge*1.2f)),shape);Invalidate();pan=Vector2.zero;zoom=1;}}
            brushMode=GUILayout.SelectionGrid(brushMode,new[]{"画笔填色","擦除格点","圈出轮廓"},3);brushSize=EditorGUILayout.IntSlider("画笔半径",brushSize,0,3);
            replacePolygon=EditorGUILayout.Toggle("圈选替换已有遮罩",replacePolygon);
            if(polygon.Count>0 && GUILayout.Button("闭合轮廓 (Enter)"))FinishPolygon();
            GUILayout.Label("图片主色 / 手绘颜色");
            if(template.palette.Count==0)template.palette.Add(ArrowBoardGraphic.Palette[2]);
            for(int i=0;i<template.palette.Count;i++)
            {
                using(new EditorGUILayout.HorizontalScope())
                {if(GUILayout.Toggle(brushColor==i,(i+1).ToString(),"Button",GUILayout.Width(32)))brushColor=i;EditorGUI.BeginChangeCheck();var c=EditorGUILayout.ColorField(template.palette[i]);c.a=1;if(EditorGUI.EndChangeCheck()){Record();var previous=template.palette.ToArray();for(int p=0;p<template.samples.Count;p++)if(template.mask[p] && Enumerable.Range(0,previous.Length).OrderBy(k=>TemplateImageProcessor.Distance(template.samples[p],previous[k])).First()==i)template.samples[p]=c;template.palette[i]=c;Invalidate();}}
            }
            if(template.palette.Count<6 && GUILayout.Button("添加颜色")){Record();template.palette.Add(ArrowBoardGraphic.Palette[template.palette.Count]);Invalidate();}
        }
        void DrawSettings()
        {
            GUILayout.Space(10);GUILayout.Label("2  目标难度与生成参数",EditorStyles.boldLabel);
            int preset=Array.IndexOf(new[]{"入门","轻松","标准","困难","挑战"},settings.preset);int next=EditorGUILayout.Popup("难度预设",Math.Max(0,preset),new[]{"入门","轻松","标准","困难","挑战"});if(next!=preset)settings.Preset(next);
            settings.fillImage=EditorGUILayout.Toggle("优先填满图片",settings.fillImage);
            settings.arrowTarget=EditorGUILayout.Slider("遮挡难度目标",settings.arrowTarget,0,100);
            if(!settings.fillImage)settings.matchTarget=EditorGUILayout.Slider("三消压力目标",settings.matchTarget,0,100);
            settings.tolerance=EditorGUILayout.Slider("目标容差",settings.tolerance,5,30);
            if(settings.fillImage)
            {
                settings.imageArrowLength=EditorGUILayout.IntSlider("箭身参考长度",settings.imageArrowLength,3,12);
                EditorGUILayout.HelpBox("按轮廓自动决定箭头数量，没有数量上限。保留原图色块，用朝向、出口和遮挡层数调节难度；三消压力仅作参考。",MessageType.None);
            }
            else{settings.arrowCount=EditorGUILayout.IntSlider("目标箭头数",settings.arrowCount,3,60);settings.arrowCount=Math.Max(3,settings.arrowCount/3*3);}
            settings.colorCount=EditorGUILayout.IntSlider(settings.fillImage?"颜色类别上限":"目标颜色数",settings.colorCount,1,settings.fillImage?5:6);settings.slots=EditorGUILayout.IntSlider("收集槽容量",settings.slots,3,9);
            if(settings.fillImage)EditorGUILayout.HelpBox("相近的深浅色自动合并为同一消除编号，避免看着相似却不能消除。",MessageType.None);
            advanced=EditorGUILayout.Foldout(advanced,"高级参数：颜色、覆盖率、种子与搜索",true);
            if(advanced)
            {
            if(!settings.fillImage){settings.colorFidelity=EditorGUILayout.Slider("原图配色优先",settings.colorFidelity,0,1);settings.allowExtraColors=EditorGUILayout.Toggle("允许为难度补充颜色",settings.allowExtraColors);
            settings.brightenDarkColors=EditorGUILayout.Toggle("提高暗色可读性",settings.brightenDarkColors);settings.coverage=EditorGUILayout.Slider("目标轮廓覆盖率",settings.coverage,.4f,.95f);}
            settings.seed=EditorGUILayout.IntField("随机种子",settings.seed);if(GUILayout.Button("换一个种子"))settings.seed=unchecked((int)DateTime.UtcNow.Ticks);
            settings.attempts=EditorGUILayout.IntSlider("搜索种子数",settings.attempts,4,64);settings.candidateCount=EditorGUILayout.IntSlider("保留候选数",settings.candidateCount,1,8);
            }
            if(!settings.fillImage && 2*settings.colorCount<settings.slots && settings.matchTarget>10)EditorGUILayout.HelpBox("当前颜色数无法产生槽满压力。提高到至少四色，或减少槽位；否则不会命中高三消压力。",MessageType.Warning);
        }
        void LoadImage(string path)
        {
            try{if(ownsImage && image)DestroyImmediate(image);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!texture.LoadImage(File.ReadAllBytes(path))){DestroyImmediate(texture);throw new Exception("图片格式不能读取。");}texture.name=Path.GetFileNameWithoutExtension(path);image=texture;ownsImage=true;imagePath=path;ReadImage();}
            catch(Exception ex){message="读取图片失败："+ex.Message;}
        }
        void ReadImage()
        {
            try{var value=TemplateImageProcessor.FromImage(image,edge,(ImageBackground)backgroundMode,backgroundThreshold,6,stripOutline,keepAspect);Record();template=value;brushColor=0;polygon.Clear();Invalidate();message=$"已识别：{template.source} → {template.width}×{template.height}，{template.palette.Count} 个主色，{template.Count} 个有效格点。";pan=Vector2.zero;zoom=1;}
            catch(Exception ex){message="识别失败："+ex.Message;}
        }
        void Begin()
        {
            if(template.Count<6){message="有效轮廓太小，至少需要六个格点。";return;}
            candidates.Clear();selected=null;currentReport=null;sourceExpanded=false;generationTemplateJson=JsonUtility.ToJson(template);generationSettingsJson=JsonUtility.ToJson(settings);job=new GenerationJob(template,settings);message="正在逐个种子搜索，已验证的候选会出现在右下方。";
        }
        void Analyze(LevelData level)
        {currentReport=DifficultyAnalyzer.Analyze(level,null,64,true);message="正在分析已打开关卡；生成设置不会改变当前关卡。";selected=null;Repaint();}
        void DrawDifficulty()
        {
            GUILayout.Space(8);GUILayout.Label("3  实测难度与可解性",EditorStyles.boldLabel);
            if(currentReport==null){GUILayout.Label("选择候选后显示实测分数。",EditorStyles.wordWrappedLabel);return;}
            var d=currentReport;Score("箭头难度",d.arrowScore);Score("三消压力",d.matchScore);Score("综合参考",d.overallScore);
            EditorGUILayout.LabelField("初始出口",$"{d.initialExits} / {d.arrows}");EditorGUILayout.LabelField("平均 / 最大遮挡深度",$"{d.meanDepth:F1} / {d.maxDepth}");
            EditorGUILayout.LabelField("平均遮挡者 / 转折",$"{d.meanBlockers:F1} / {d.meanTurns:F1}");EditorGUILayout.LabelField("已知解平均可选数",d.meanChoices.ToString("F1"));
            EditorGUILayout.LabelField("已验证通关解峰值",$"{d.knownPeak} 格");EditorGUILayout.LabelField("随机策略失败",$"{d.randomFailureRate:P0} ({d.randomTrials} 次)");EditorGUILayout.LabelField("优先凑色策略失败",$"{d.greedyFailureRate:P0} ({d.randomTrials} 次)");
            if(d.minimumPeak>=0)EditorGUILayout.LabelField(d.minimumPeakExact?"已证实最小峰值":"找到的峰值上界",d.minimumPeak+" 格");
            EditorGUILayout.HelpBox(d.note,d.solutionVerified?MessageType.Info:MessageType.Warning);
            if(selected!=null)
            {
                EditorGUILayout.LabelField("轮廓覆盖率",selected.coverage.ToString("P0"));EditorGUILayout.LabelField("配色误差（越小越近）",selected.colorError.ToString("F3"));
                if(!string.IsNullOrWhiteSpace(selected.warning))EditorGUILayout.HelpBox(selected.warning.Trim(),MessageType.Warning);
                if(GUILayout.Button("详细分析最小槽位峰值"))currentReport=DifficultyAnalyzer.Analyze(selected.level,selected.difficulty.solution,64,true);
            }
            if(baseLevel!=null && GUILayout.Button("重新分析编辑器当前关卡"))Analyze(baseLevel);
        }
        static void Score(string name,float value){var r=GUILayoutUtility.GetRect(100,20);EditorGUI.ProgressBar(r,Mathf.Clamp01(value/100),$"{name}  {value:F0} / 100");}
        void DrawCandidates()
        {
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.Height(160)))
            {
                GUILayout.Label("生成候选：点击预览，不会覆盖已打开的关卡",EditorStyles.boldLabel);
                using(new EditorGUILayout.HorizontalScope())
                {
                    foreach(var c in candidates)
                    {var old=GUI.backgroundColor;GUI.backgroundColor=c.targetReached?new Color(.55f,.85f,.65f):new Color(.9f,.8f,.5f);if(GUILayout.Button($"{c.level.initialSegments.Count} 枚  种子 {c.seed}\n箭头 {c.difficulty.arrowScore:F0}  三消 {c.difficulty.matchScore:F0}\n覆盖 {c.coverage:P0}  {(c.targetReached?"命中难度":"难度未命中")}",GUILayout.Height(62))){selected=c;currentReport=c.difficulty;Repaint();}GUI.backgroundColor=old;}
                }
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(selected==null || job!=null))
                    {if(GUILayout.Button("采用到关卡编辑器",GUILayout.Height(28)))Adopt();if(GUILayout.Button("导出此关卡 + 生成配方",GUILayout.Height(28)))Export(false);}
                    using(new EditorGUI.DisabledScope(candidates.Count==0 || job!=null))if(GUILayout.Button("批量导出全部候选",GUILayout.Height(28)))Export(true);
                }
            }
        }
        void Adopt()
        {
            if(selected==null)return;if(!owner)owner=GetWindow<TripleMazeLevelEditor>();var value=selected.level.Copy();value.levelId=levelId;value.description="生成关卡 "+template.source;owner.AdoptGenerated(value,$"来自快速生成器：箭头 {selected.difficulty.arrowScore:F0}，三消 {selected.difficulty.matchScore:F0}。采用后尚未写盘，请保存或试玩。");owner.Show();owner.Focus();
        }
        void Export(bool all)
        {
            string folder=EditorUtility.OpenFolderPanel("导出候选与生成配方",Application.dataPath+"/TripleMaze/Resources/Levels","");if(string.IsNullOrEmpty(folder))return;
            var values=all?candidates:new List<GeneratedCandidate>{selected};int index=0;
            foreach(var candidate in values)
            {
                var level=candidate.level.Copy();level.levelId=levelId+index++;string name=$"level_{level.levelId:00}_seed_{candidate.seed}";int suffix=1;string basename=name;while(File.Exists(Path.Combine(folder,name+".json")))name=basename+"_"+suffix++;
                File.WriteAllText(Path.Combine(folder,name+".json"),JsonUtility.ToJson(level,true));
                var exported=JsonUtility.FromJson<GeneratedCandidate>(JsonUtility.ToJson(candidate));exported.level=level;
                var recipe=new GenerationRecipe{template=JsonUtility.FromJson<GenerationTemplate>(generationTemplateJson??JsonUtility.ToJson(template)),settings=JsonUtility.FromJson<GenerationSettings>(generationSettingsJson??JsonUtility.ToJson(settings)),result=exported};File.WriteAllText(Path.Combine(folder,name+".recipe.json"),JsonUtility.ToJson(recipe,true));
            }
            AssetDatabase.Refresh();message=$"已导出 {values.Count} 个候选及对应配方。现有关卡文件未覆盖。";
        }
        void LoadRecipe()
        {
            string path=EditorUtility.OpenFilePanel("打开生成配方","","json");if(string.IsNullOrEmpty(path))return;
            try{var r=JsonUtility.FromJson<GenerationRecipe>(File.ReadAllText(path));if(r.version!=1 || r.template==null || r.settings==null || r.template.mask.Count!=r.template.width*r.template.height)throw new Exception("不是有效的生成配方。");Record();template=r.template;settings=r.settings;candidates.Clear();selected=r.result;if(selected!=null){candidates.Add(selected);currentReport=selected.difficulty;}generationTemplateJson=JsonUtility.ToJson(template);generationSettingsJson=JsonUtility.ToJson(settings);message="已恢复轮廓、颜色、种子、参数与候选。";}
            catch(Exception ex){message="读取配方失败："+ex.Message;}
        }
        Vector2 Screen(Vector2Int p)=>origin+new Vector2(p.x,p.y)*cell;
        Vector2Int Grid(Vector2 p)=>new Vector2Int(Mathf.RoundToInt((p.x-origin.x)/cell),Mathf.RoundToInt((p.y-origin.y)/cell));
        void DrawCanvas(Rect r)
        {
            EditorGUI.DrawRect(r,new Color(.10f,.15f,.23f));cell=Mathf.Min((r.width-55)/template.width,(r.height-55)/template.height)*zoom;origin=r.center-new Vector2(template.width-1,template.height-1)*cell*.5f+pan;
            GUI.BeginClip(r);var offset=r.position;
            for(int y=0;y<template.height;y++)for(int x=0;x<template.width;x++)
            {
                var p=new Vector2Int(x,y);var point=Screen(p)-offset;bool active=template.Contains(x,y);
                if(showMask && active){var c=template.ColorAt(p);c.a=selected==null?.60f:.16f;EditorGUI.DrawRect(new Rect(point-Vector2.one*cell*.47f,Vector2.one*cell*.94f),c);}
                else EditorGUI.DrawRect(new Rect(point-Vector2.one,Vector2.one*2),new Color(1,1,1,.10f));
            }
            Handles.BeginGUI();
            if(selected!=null)foreach(var a in selected.level.initialSegments)
            {
                Handles.color=PuzzleModel.DisplayColor(selected.level,a.colorDisplayIndex);var raw=a.points.Select(p=>(Vector3)(Screen(p)-offset)).ToList();var smooth=new List<Vector3>();PathSmoother.SmoothPath(ref smooth,raw,0,cell*.2f,4);Handles.DrawAAPolyLine(3,smooth.ToArray());var dir=(raw[0]-raw[1]).normalized;var side=new Vector3(-dir.y,dir.x);Handles.DrawAAConvexPolygon(raw[0]+dir*cell*.08f,raw[0]-dir*cell*.35f+side*cell*.23f,raw[0]-dir*cell*.35f-side*cell*.23f);
            }
            if(polygon.Count>1){Handles.color=Color.yellow;Handles.DrawAAPolyLine(3,polygon.Select(p=>(Vector3)(Screen(p)-offset)).ToArray());}
            Handles.EndGUI();GUI.EndClip();
            var e=Event.current;if(!r.Contains(e.mousePosition))return;
            if(e.type==EventType.ScrollWheel){zoom=Mathf.Clamp(zoom-e.delta.y*.04f,.4f,3);e.Use();Repaint();}
            if(e.type==EventType.MouseDrag && e.button==2){pan+=e.delta;e.Use();Repaint();}
            if(job!=null)return;var grid=Grid(e.mousePosition);
            if(e.type==EventType.MouseDown && e.button==0)
            {
                if(brushMode==2){if(template.Contains(grid.x,grid.y) || grid.x>=0 && grid.y>=0 && grid.x<template.width && grid.y<template.height){polygon.Add(grid);draggedPolygon=false;}}
                else{Record();Invalidate();Paint(grid);stroke=true;}e.Use();Repaint();
            }
            if(e.type==EventType.MouseDrag && e.button==0)
            {
                if(brushMode==2){if(grid.x>=0 && grid.y>=0 && grid.x<template.width && grid.y<template.height && (polygon.Count==0 || polygon[polygon.Count-1]!=grid)){polygon.Add(grid);draggedPolygon=true;}}
                else if(stroke)Paint(grid);e.Use();Repaint();
            }
            if(e.type==EventType.MouseUp && e.button==0){stroke=false;if(brushMode==2 && draggedPolygon && polygon.Count>=3)FinishPolygon();}
        }
        void Paint(Vector2Int p)
        {
            var color=template.palette[Mathf.Clamp(brushColor,0,template.palette.Count-1)];
            for(int y=p.y-brushSize;y<=p.y+brushSize;y++)for(int x=p.x-brushSize;x<=p.x+brushSize;x++)if(x>=0 && y>=0 && x<template.width && y<template.height){int i=y*template.width+x;template.mask[i]=brushMode!=1;if(brushMode!=1)template.samples[i]=color;}
        }
        void FinishPolygon(){if(polygon.Count<3)return;Record();TemplateImageProcessor.FillPolygon(template,polygon,template.palette[Mathf.Clamp(brushColor,0,template.palette.Count-1)],replacePolygon);polygon.Clear();Invalidate();}
    }
}
