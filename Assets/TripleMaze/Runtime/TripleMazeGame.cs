using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TripleMaze
{
    public class TripleMazeGame : MonoBehaviour
    {
        public static LevelData PreviewLevel;
        [Min(1f)] public float ArrowSpeed=50f; // Grid cells per second, constant throughout flight.
        public PuzzleModel Model {get;private set;}
        public int LevelNumber {get;private set;}=1;
        public bool Busy {get;private set;}
        public string Page {get;private set;}="Home";
        RectTransform settingsOverlay;float settingsTimeScale=1;
        Canvas canvas;RectTransform root,screen,pageRoot,boardViewport,tray,effectsLayer;
        Font font,displayFont;FeedbackAudio audioFeedback;
        ArrowBoardGraphic board;Text status,progress,trayCount;SoftPanel progressFill;
        readonly List<TextAsset> levels=new List<TextAsset>();
        readonly List<RectTransform> tokens=new List<RectTransform>();
        readonly List<Animator> stars=new List<Animator>();
        readonly List<int> moveHistory=new List<int>();
        readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
        readonly Color white=new Color32(242,246,251,255),muted=new Color32(153,171,194,255),mint=new Color32(139,233,197,255),navy=new Color32(24,35,55,255),panel=new Color32(35,49,71,255),gold=new Color32(255,210,127,255);
        sealed class CollectionAnimation
        {
            public int Index,Color,Combo;
            public RectTransform View;
            public bool Arrived;
            public CollectionAnimation[] Match;
        }
        readonly List<CollectionAnimation> logicalTray=new List<CollectionAnimation>();
        readonly Queue<CollectionAnimation> collections=new Queue<CollectionAnimation>();
        readonly HashSet<RectTransform> mergingTokens=new HashSet<RectTransform>();
        int combo,lastMatchMove,activeAnimations;bool settling,finishing;float statusUntil;
        void Awake()
        {
            #if UNITY_EDITOR
            // Only an explicit editor jump may override this particular launch.
            LevelNumber=UnityEditor.SessionState.GetInt("TripleMaze.NextDebugLevel",1);
            UnityEditor.SessionState.EraseInt("TripleMaze.NextDebugLevel");
#endif
            Screen.orientation=ScreenOrientation.Portrait;
            int refreshRate=Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate=Mathf.Max(60,refreshRate);
            QualitySettings.vSyncCount=Application.isMobilePlatform?0:1;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval=1;
            Application.runInBackground=true;
            font=Resources.Load<Font>("Fonts/NotoSansSC");if(!font)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            displayFont=Resources.Load<Font>("Polish/Inter-Bold");if(!displayFont)displayFont=font;
            audioFeedback=gameObject.AddComponent<FeedbackAudio>();
            for(int n=1;;n++){var a=Resources.Load<TextAsset>($"Levels/level_{n:00}");if(!a)break;levels.Add(a);}
            var go=new GameObject("TripleMaze UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            go.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
            var backdrop=Box("Window Background",go.transform,Vector2.zero,Vector2.zero,new Color32(14,23,38,255),0);Stretch(backdrop.rectTransform);
            root=Rect("Phone Viewport",go.transform,Vector2.zero,PortraitViewport.DesignSize);root.gameObject.AddComponent<RectMask2D>();root.gameObject.AddComponent<PortraitViewport>().Refresh();
            if(!FindObjectOfType<EventSystem>())new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            if(PreviewLevel!=null){var d=PreviewLevel;PreviewLevel=null;StartLevel(d);}else Home();
        }
        void OnDestroy(){CloseSettings();if(canvas)Destroy(canvas.gameObject);foreach(var s in sprites.Values)if(s)Destroy(s);}
        static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.sizeDelta=Vector2.zero;r.anchoredPosition=Vector2.zero;}
        RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=Vector2.one*.5f;r.sizeDelta=size;r.anchoredPosition=pos;return r;}
        SoftPanel Box(string name,Transform parent,Vector2 pos,Vector2 size,Color color,float radius=24)
        {var p=Rect(name,parent,pos,size).gameObject.AddComponent<SoftPanel>();p.color=color;p.Radius=radius;return p;}
        Text Label(string message,Transform parent,Vector2 pos,Vector2 size,int fs,Color color,bool english=false)
        {var t=Rect(message,parent,pos,size).gameObject.AddComponent<Text>();t.font=english?displayFont:font;t.text=message;t.fontSize=fs;t.color=color;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;return t;}
        Sprite Sprite(string key)
        {
            if(sprites.TryGetValue(key,out var s))return s;
            if(key.StartsWith("confetti-"))
            {
                var sheet=Resources.Load<Texture2D>("Polish/firework-chips");int tile=int.Parse(key.Substring(9));
                s=UnityEngine.Sprite.Create(sheet,new Rect((tile%2)*sheet.width/2,(tile/2)*sheet.height/2,sheet.width/2,sheet.height/2),Vector2.one*.5f,100);sprites[key]=s;return s;
            }
            var t=Resources.Load<Texture2D>("Polish/"+key);if(!t)return null;
            s=UnityEngine.Sprite.Create(t,new Rect(0,0,t.width,t.height),Vector2.one*.5f,100);sprites[key]=s;return s;
        }
        Image Icon(string key,Transform parent,Vector2 pos,Vector2 size,Color tint)
        {var image=Rect(key,parent,pos,size).gameObject.AddComponent<Image>();image.sprite=Sprite(key);image.preserveAspect=true;image.color=tint;image.raycastTarget=false;return image;}
        void Glow(Transform parent,Vector2 pos,Vector2 size,Color color)
        {var g=Rect("Ambient glow",parent,pos,size).gameObject.AddComponent<SoftGlow>();g.color=color;g.raycastTarget=false;}
        Button Button(string title,Transform parent,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction action,bool primary=false,string icon=null)
        {
            Box("Button shadow",parent,pos+new Vector2(0,-5),size,primary?new Color32(57,127,112,255):new Color32(15,24,40,255),size.y*.3f).raycastTarget=false;
            var face=Box(title,parent,pos,size,primary?mint:panel,size.y*.3f);face.Gradient=true;face.Bottom=primary?new Color32(112,210,179,255):new Color32(31,45,66,255);
            var button=face.gameObject.AddComponent<Button>();button.targetGraphic=face;button.transition=Selectable.Transition.None;face.gameObject.AddComponent<PressFeedback>();
            var text=Label(title,face.transform,icon==null?Vector2.zero:new Vector2(18,0),new Vector2(size.x-(icon==null?20:68),size.y),primary?28:22,primary?navy:white);
            if(icon!=null)Icon(icon,face.transform,new Vector2(-size.x*.5f+40,0),new Vector2(34,36),icon=="hint"?Color.white:primary?navy:muted);
            button.onClick.AddListener(()=>{audioFeedback.Play("tap",.3f);action();});return button;
        }
        Button IconButton(string name,string icon,Transform parent,Vector2 pos,UnityEngine.Events.UnityAction action)
        {var f=Box(name,parent,pos,new Vector2(76,76),panel,26);var b=f.gameObject.AddComponent<Button>();b.targetGraphic=f;b.transition=Selectable.Transition.None;f.gameObject.AddComponent<PressFeedback>();if(icon=="retry"){var symbol=Rect("Retry",f.transform,Vector2.zero,new Vector2(34,34)).gameObject.AddComponent<RetryIcon>();symbol.color=white;symbol.raycastTarget=false;}else Icon(icon,f.transform,Vector2.zero,new Vector2(30,32),white);b.onClick.AddListener(()=>{audioFeedback.Play("tap",.3f);action();});return b;}
        void Clear(string page)
        {
#if UNITY_EDITOR
            DebugAutoPlaying=false;
#endif
            CloseSettings();StopAllCoroutines();Busy=false;activeAnimations=0;settling=false;finishing=false;logicalTray.Clear();collections.Clear();mergingTokens.Clear();Page=page;tokens.Clear();moveHistory.Clear();combo=0;lastMatchMove=-99;
            if(pageRoot){pageRoot.gameObject.SetActive(false);Destroy(pageRoot.gameObject);}
            var bg=Box(page,root,Vector2.zero,PortraitViewport.DesignSize,navy,0);bg.Gradient=true;bg.Bottom=new Color32(16,25,43,255);pageRoot=bg.rectTransform;Stretch(pageRoot);
            Glow(pageRoot,new Vector2(270,450),new Vector2(920,900),new Color(.22f,.43f,.56f,.14f));Glow(pageRoot,new Vector2(-290,-460),new Vector2(900,900),new Color(.28f,.23f,.5f,.12f));
            screen=Rect("SafeArea",pageRoot,Vector2.zero,PortraitViewport.DesignSize);
            effectsLayer=Rect("Feedback",screen,Vector2.zero,PortraitViewport.DesignSize);
        }
        void ApplySafeAreaLayout()
        {
            // 保留 720 x 1280 中的边距：顶部和底部控件随安全区延展，棋盘保持居中。
            for(int i=0;i<screen.childCount;i++)
            {
                var item=screen.GetChild(i) as RectTransform;
                if(!item || item==effectsLayer)continue;
                var position=item.anchoredPosition;
                if(position.y>=350)
                {
                    item.anchorMin=item.anchorMax=new Vector2(.5f,1);
                    item.anchoredPosition=new Vector2(position.x,position.y-PortraitViewport.DesignSize.y*.5f);
                }
                else if(position.y<=-300)
                {
                    item.anchorMin=item.anchorMax=new Vector2(.5f,0);
                    item.anchoredPosition=new Vector2(position.x,position.y+PortraitViewport.DesignSize.y*.5f);
                }
            }
            root.GetComponent<PortraitViewport>().Refresh();
        }
        void AddSettingsButton()
        {
            Button("设置",screen,new Vector2(292,550),new Vector2(84,76),OpenSettings);
        }
        void CloseSettings()
        {
            if(!settingsOverlay)return;
            settingsOverlay.gameObject.SetActive(false);Destroy(settingsOverlay.gameObject);settingsOverlay=null;
            Time.timeScale=settingsTimeScale;audioFeedback.SaveVolumes();
        }
        void OpenSettings()
        {
            if(settingsOverlay)return;
            settingsTimeScale=Time.timeScale;Time.timeScale=0;
            var shade=Box("Settings overlay",root,Vector2.zero,Vector2.zero,new Color(0,0,0,.72f),0);
            settingsOverlay=shade.rectTransform;Stretch(settingsOverlay);settingsOverlay.SetAsLastSibling();
            var panelRoot=Box("Settings panel",settingsOverlay,screen.anchoredPosition,new Vector2(600,720),panel,32).rectTransform;
            Label("设置",panelRoot,new Vector2(0,285),new Vector2(400,64),36,white);
            VolumeSlider(panelRoot,"音乐",145,audioFeedback.MusicVolume,audioFeedback.SetMusicVolume);
            VolumeSlider(panelRoot,"音效",15,audioFeedback.EffectsVolume,audioFeedback.SetEffectsVolume);
            Label("拖到最左侧即可静音",panelRoot,new Vector2(0,-75),new Vector2(460,40),20,muted);
            if(Page=="Game")
            {
                var currentLevel=Model.Level;
                Button("重新开始",panelRoot,new Vector2(0,-150),new Vector2(440,70),()=>{CloseSettings();StartLevel(currentLevel);});
                Button("返回菜单",panelRoot,new Vector2(0,-235),new Vector2(440,70),()=>{CloseSettings();Home();});
            }
            Button("关闭",panelRoot,new Vector2(0,-315),new Vector2(220,60),CloseSettings,true);
        }
        void VolumeSlider(Transform parent,string title,float y,float value,UnityEngine.Events.UnityAction<float> change)
        {
            Label(title,parent,new Vector2(-170,y+35),new Vector2(100,40),26,white);
            var number=Label(value<=0?"静音":Mathf.RoundToInt(value*100)+"%",parent,new Vector2(165,y+35),new Vector2(130,40),23,mint);
            var sliderRoot=Rect(title+" volume",parent,new Vector2(0,y-15),new Vector2(440,52));
            var track=Box("Track",sliderRoot,Vector2.zero,new Vector2(440,10),new Color32(15,24,40,255),5);
            var fillArea=Rect("Fill area",sliderRoot,Vector2.zero,new Vector2(410,10));
            var fill=Box("Fill",fillArea,Vector2.zero,Vector2.zero,mint,5);Stretch(fill.rectTransform);
            var handleArea=Rect("Handle area",sliderRoot,Vector2.zero,new Vector2(410,0));
            var handle=Box("Handle",handleArea,Vector2.zero,new Vector2(34,34),white,17);
            var slider=sliderRoot.gameObject.AddComponent<Slider>();slider.direction=Slider.Direction.LeftToRight;
            slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.minValue=0;slider.maxValue=1;slider.value=value;
            slider.onValueChanged.AddListener(v=>{change(v);number.text=v<=0?"静音":Mathf.RoundToInt(v*100)+"%";});
        }
        public void Home()
        {
            Clear("Home");AddSettingsButton();
            // Layout approved in the 720 x 1280 UI preview (top-origin Y = 260).
            Icon("title-art",screen,new Vector2(0,380),new Vector2(650,420),Color.white);
            AddMenuWatermark();
            int saved=Mathf.Clamp(LevelNumber,1,Mathf.Max(1,levels.Count));
            Button($"开始第 {saved} 关",screen,new Vector2(0,-350),new Vector2(252,96),()=>{LevelNumber=saved;StartLevel();},true);
            ApplySafeAreaLayout();StartCoroutine(Entrance(pageRoot));
        }
        static ArrowData A(int color,params int[] xy){var a=new ArrowData{colorDisplayIndex=color};for(int i=0;i<xy.Length;i+=2)a.points.Add(new Vector2Int(xy[i],xy[i+1]));return a;}
        public void StartLevel(){if(levels.Count>0)StartLevel(JsonUtility.FromJson<LevelData>(levels[(LevelNumber-1)%levels.Count].text));}
        public void StartLevel(LevelData data)
        {
            var errors=PuzzleModel.Validate(data);if(errors.Count>0){Debug.LogError(string.Join("; ",errors));return;}
            Clear("Game");Model=new PuzzleModel(data);LevelNumber=Mathf.Clamp(data.levelId,1,Mathf.Max(1,levels.Count));
            AddSettingsButton();
            Label($"第 {data.levelId:00} 关",screen,new Vector2(0,560),new Vector2(320,60),33,white);

            var track=Box("Progress track",screen,new Vector2(0,469),new Vector2(592,5),new Color32(47,64,87,255),2);track.raycastTarget=false;
            progressFill=Box("Progress fill",track.transform,new Vector2(-296,0),new Vector2(0,5),mint,2);progressFill.rectTransform.pivot=new Vector2(0,.5f);progressFill.raycastTarget=false;
            track.gameObject.SetActive(false);
            progress=null;trayCount=null;status=null;
            boardViewport=Rect("Board Viewport",screen,new Vector2(0,-55),new Vector2(650,660));boardViewport.gameObject.AddComponent<RectMask2D>();
            var br=Rect("Arrow Board",boardViewport,Vector2.zero,new Vector2(650,660));board=br.gameObject.AddComponent<ArrowBoardGraphic>();board.Model=Model;board.Clicked=ClickArrow;
            var dots=Rect("Grid dots",boardViewport,Vector2.zero,new Vector2(650,660)).gameObject.AddComponent<BoardDots>();dots.Board=board;dots.raycastTarget=false;dots.transform.SetAsFirstSibling();
            board.PressedArrow=i=>{if(Model.State==RoundState.Playing)audioFeedback.Play("pick",.4f);};
            var trayFace=Box("Tray frame",screen,new Vector2(0,365),new Vector2(672,112),new Color32(42,58,80,255),28);trayFace.Gradient=true;trayFace.Bottom=new Color32(29,43,64,255);trayFace.raycastTarget=false;
            tray=Rect("Collection Tray",screen,new Vector2(0,367),new Vector2(650,100));
            stars.Clear();
            var healthBar=Rect("Remaining stars",screen,new Vector2(-239,550),new Vector2(176,52));
            for(int h=0;h<3;h++)
            {
                var heart=Rect("Life star "+(h+1),healthBar,new Vector2((h-1)*56,0),new Vector2(46,43));
                Icon("star-empty",heart,Vector2.zero,new Vector2(46,43),Color.white);
                var filled=Icon("star",heart,Vector2.zero,new Vector2(44,40),Color.white);filled.name="on";
                var animator=heart.gameObject.AddComponent<Animator>();animator.runtimeAnimatorController=Resources.Load<RuntimeAnimatorController>("Polish/HeartCtrl");animator.enabled=false;stars.Add(animator);
            }
            float step=TrayStep;
            for(int i=0;i<data.slotCapacity;i++){var slot=Box("Slot "+i,tray,new Vector2(TrayX(i),0),new Vector2(step-8,83),new Color32(19,31,49,255),18);slot.raycastTarget=false;Label("",slot.transform,Vector2.zero,new Vector2(70,70),16,muted);}
            var hintRoot=Rect("Hint button layout",screen,new Vector2(-102,-505),new Vector2(170,84));hintRoot.localScale=Vector3.one*.8f;
            Button("提示",hintRoot,Vector2.zero,new Vector2(170,84),ShowHint,false,"hint");
            var undoRoot=Rect("Undo button layout",screen,new Vector2(102,-505),new Vector2(170,84));undoRoot.localScale=Vector3.one*.8f;
            Button("撤回",undoRoot,Vector2.zero,new Vector2(170,84),UndoMove);
            effectsLayer.SetAsLastSibling();Refresh();ApplySafeAreaLayout();StartCoroutine(Entrance(pageRoot));
        }
        float TrayStep=>Mathf.Min(86,624f/Model.Level.slotCapacity);
        float TrayX(int index)=>(index-(Model.Level.slotCapacity-1)*.5f)*TrayStep;
        RectTransform Token(int color,Transform parent,Vector2 position)
        {
            Color c=PuzzleModel.DisplayColor(Page=="Home"?null:Model?.Level,color);float width=Model!=null?Mathf.Min(74,TrayStep-12):70;
            var r=Rect("Color token "+color,parent,position,new Vector2(width,78));
            Box("Token depth",r,new Vector2(0,-4),new Vector2(width,74),Color.Lerp(c,navy,.4f),18).raycastTarget=false;
            var face=Box("Token face",r,new Vector2(0,2),new Vector2(width,72),c,18);face.Gradient=true;face.Bottom=Color.Lerp(c,Color.white,.12f);face.raycastTarget=false;
            Box("Soft highlight",r,new Vector2(0,29),new Vector2(width-24,3),new Color(1,1,1,.24f),2).raycastTarget=false;
            Icon("arrow",r,new Vector2(0,3),new Vector2(28,31),new Color(navy.r,navy.g,navy.b,.83f));return r;
        }
        public void ClickArrow(int index)
        {
            if(Page!="Game" || Model.State!=RoundState.Playing || index<0 || index>=Model.Level.initialSegments.Count || Model.Removed.Contains(index) || board.Motions.ContainsKey(index))return;
            board.Pressed=-1;board.Hint=-1;
            // All gameplay decisions use the board at the instant of the click.
            if(!Model.CanExit(index))
            {
                var arrow=Model.Level.initialSegments[index];var dir=PuzzleModel.Direction(arrow);
                var cell=arrow.points[0]+dir;int target=-1;
                for(;cell.x>=0 && cell.y>=0 && cell.x<Model.Level.gridWidth && cell.y<Model.Level.gridHeight;cell+=dir)
                {
                    for(int j=0;j<Model.Level.initialSegments.Count;j++)
                        if(j!=index && !Model.Removed.Contains(j) && PuzzleModel.Cells(Model.Level.initialSegments[j]).Contains(cell)){target=j;break;}
                    if(target>=0)break;
                }
                if(target<0 || !Model.HitObstacle())return;
                combo=0;lastMatchMove=-99;
                activeAnimations++;Busy=true;
                StartCoroutine(Blocked(index,target,cell,Model.Health));return;
            }
            int color=Model.Level.initialSegments[index].colorDisplayIndex;
            var last=logicalTray.LastOrDefault(x=>x.Color==color);
            var result=Model.Collect(index);
            if(result==MoveResult.Blocked || result==MoveResult.Rejected)return;
            moveHistory.Add(index);
            var job=new CollectionAnimation{Index=index,Color=color};
            int logicalIndex=last==null?logicalTray.Count:logicalTray.IndexOf(last)+1;
            logicalTray.Insert(logicalIndex,job);
            job.View=Token(color,tray,Vector2.zero);job.View.gameObject.SetActive(false);
            int visualIndex=last==null?tokens.Count:tokens.IndexOf(last.View)+1;
            tokens.Insert(visualIndex,job.View);
            if(logicalTray.Count(x=>x.Color==color)>=3)
            {
                job.Match=logicalTray.Where(x=>x.Color==color).Take(3).ToArray();
                foreach(var match in job.Match)logicalTray.Remove(match);
                job.Combo=++combo;lastMatchMove=Model.Moves;
            }
            collections.Enqueue(job);activeAnimations++;Busy=true;
            StartCoroutine(Exit(job));
            if(!settling){settling=true;StartCoroutine(SettleCollections());}
            Refresh();
        }
        IEnumerator Blocked(int i,int target,Vector2Int cell,int healthAfterClick)
        {
            var dir=PuzzleModel.Direction(Model.Level.initialSegments[i]);
            var head=Model.Level.initialSegments[i].points[0];
            float travel=Vector2Int.Distance(head,cell)-1+board.ContactTravel;
            float duration=travel/Mathf.Max(1f,ArrowSpeed);
            var motion=new ArrowBoardGraphic.Motion();board.Motions.Add(i,motion);
            for(float t=0;t<duration;t+=Time.deltaTime){motion.Travel=travel*(t/duration);board.Redraw();yield return null;}
            motion.Travel=travel;motion.ImpactTarget=target;motion.FlashAmount=1;
            motion.BlockPoint=board.GridPosition(cell);
            var lostStar=stars[healthAfterClick];lostStar.enabled=true;lostStar.Play("LoseHeart",0,0);
            audioFeedback.Play("blocked",.32f);audioFeedback.Haptic();
            SetStatus("前方有阻挡，先帮它让出一条路",new Color32(255,176,155,255));
            Burst(board.transform,motion.BlockPoint.Value,new Color(1,.77f,.58f),7);
            board.Redraw();yield return new WaitForSeconds(.055f);
            for(float t=0;t<.34f;t+=Time.deltaTime)
            {
                float k=t/.34f;motion.Travel=travel*(1-Ease(k));motion.FlashAmount=1-k;
                motion.TargetRecoil=new Vector2(dir.x,-dir.y)*Mathf.Sin(k*Mathf.PI)*7*(1-k);
                board.Redraw();yield return null;
            }
            board.Motions.Remove(i);board.Redraw();activeAnimations--;FinishIfIdle();
        }
        float VisualTrayX(int index)=>tokens.Count<=Model.Level.slotCapacity?TrayX(index):(index-(tokens.Count-1)*.5f)*Mathf.Min(TrayStep,580f/Mathf.Max(1,tokens.Count-1));
        void LateUpdate()
        {
            if(Page!="Game")return;
            // Keep landed tokens aligned as concurrent flights reserve or release slots.
            for(int n=0;n<tokens.Count;n++)
                if(tokens[n] && tokens[n].parent==tray && !mergingTokens.Contains(tokens[n]))
                    tokens[n].anchoredPosition=Vector2.Lerp(tokens[n].anchoredPosition,new Vector2(VisualTrayX(n),0),1-Mathf.Exp(-Time.deltaTime*22));
        }
        IEnumerator Exit(CollectionAnimation job)
        {
            int i=job.Index;var motion=new ArrowBoardGraphic.Motion();board.Motions.Add(i,motion);
            audioFeedback.Play("pick",.4f);audioFeedback.Haptic();audioFeedback.Play("slide",.5f,Random.Range(.96f,1.07f));
            float contactDistance=board.ExitDistance(i),length=0;
            var points=Model.Level.initialSegments[i].points;
            for(int n=1;n<points.Count;n++)length+=Vector2Int.Distance(points[n-1],points[n]);
            // Keep moving outward until the tail clears the mask; never turn into a flying token.
            float distance=contactDistance+length+1f;
            float speed=Mathf.Max(1f,ArrowSpeed),flightDuration=distance/speed;
            if(FeedbackAnimationRate>1){flightDuration=Mathf.Min(flightDuration,.12f);speed=distance/Mathf.Max(.001f,flightDuration);}
            bool collected=false;StartCoroutine(ArrowTrail(i));
            for(float t=0;t<flightDuration;t+=Time.deltaTime)
            {
                motion.Travel=speed*t;
                if(!collected && motion.Travel>=contactDistance)
                {collected=true;StartCoroutine(PopCollectedToken(job));}
                board.Redraw();yield return null;
            }
            if(!collected)StartCoroutine(PopCollectedToken(job));
            board.Motions.Remove(i);board.Redraw();activeAnimations--;FinishIfIdle();
        }
        IEnumerator PopCollectedToken(CollectionAnimation job)
        {
            var view=job.View;
            view.anchoredPosition=new Vector2(VisualTrayX(tokens.IndexOf(view)),0);
            view.localScale=Vector3.one*.68f;
            var fade=view.gameObject.AddComponent<CanvasGroup>();fade.alpha=0;
            view.gameObject.SetActive(true);audioFeedback.Play("land",.5f);
            // One restrained pop at the slot, with a quick reveal and a soft settle.
            float duration=.18f/FeedbackAnimationRate;
            for(float t=0;t<duration;t+=Time.deltaTime)
            {
                float p=t/duration;
                float scale=p<.7f?Mathf.Lerp(.68f,1.055f,Ease(p/.7f)):Mathf.Lerp(1.055f,1,Mathf.SmoothStep(0,1,(p-.7f)/.3f));
                view.localScale=Vector3.one*scale;fade.alpha=Mathf.Clamp01(t/.055f);yield return null;
            }
            view.localScale=Vector3.one;fade.alpha=1;Destroy(fade);job.Arrived=true;
        }
        IEnumerator SettleCollections()
        {
            // Only tray feedback is ordered; input and all arrow/entry flights remain concurrent.
            while(collections.Count>0)
            {
                var job=collections.Peek();while(!job.Arrived)yield return null;
                if(job.Match!=null)
                {
                    while(job.Match.Any(x=>!x.Arrived))yield return null;
                    var word=StreakWords.At(job.Combo);if(word!=null)StartCoroutine(WordFeedback(word));
                    SetStatus(job.Combo>1?$"漂亮！连续完成 {job.Combo} 组三消":"刚好三枚，漂亮！",mint);
                    audioFeedback.Play(job.Combo>1?"chain":"match",.85f,Mathf.Min(1.18f,1+(job.Combo-1)*.035f));audioFeedback.Haptic(true);
                    var matching=job.Match.Select(x=>x.View).ToArray();
                    foreach(var view in matching)mergingTokens.Add(view);
                    float center=matching.Average(r=>r.anchoredPosition.x);
                    var positions=matching.Select(r=>r.anchoredPosition).ToArray();
                    float mergeDuration=.20f/FeedbackAnimationRate;
                    for(float t=0;t<mergeDuration;t+=Time.deltaTime)
                    {
                        float p=t/mergeDuration;
                        for(int n=0;n<matching.Length;n++){matching[n].anchoredPosition=Vector2.Lerp(positions[n],new Vector2(center,6),Ease(p));matching[n].localScale=Vector3.one*(1+.18f*Mathf.Sin(p*Mathf.PI)-.85f*p*p);}
                        yield return null;
                    }
                    Burst(effectsLayer,effectsLayer.InverseTransformPoint(tray.TransformPoint(new Vector2(center,6))),PuzzleModel.DisplayColor(Model.Level,job.Color),14);
                    foreach(var view in matching){mergingTokens.Remove(view);tokens.Remove(view);Destroy(view.gameObject);}
                    if(FeedbackAnimationRate<=1)yield return new WaitForSeconds(.12f);
                }
                collections.Dequeue();
            }
            settling=false;FinishIfIdle();
        }
        void FinishIfIdle()
        {
            Busy=activeAnimations>0 || settling || finishing;
            if(Busy || Page!="Game" || Model.State==RoundState.Playing)return;
            finishing=true;Busy=true;StartCoroutine(FinishRound());
        }
        float FeedbackAnimationRate
        {
            get
            {
#if UNITY_EDITOR
                if(DebugAutoPlaying)return 10;
#endif
                return 1;
            }
        }
#if UNITY_EDITOR
        public bool DebugAutoPlaying {get;private set;}
        public void DebugWinCurrentLevel()
        {
            if(Page!="Game" || Model==null || Model.State!=RoundState.Playing || DebugAutoPlaying)return;
            DebugAutoPlaying=true;StartCoroutine(DebugPlaySolution());
        }
        IEnumerator DebugPlaySolution()
        {
            var current=Model;var currentBoard=board;
            bool wasInteractive=currentBoard.raycastTarget;
            currentBoard.raycastTarget=false;
            try
            {
                // 等玩家已经触发的动画结束，再从当前棋盘与槽位求解。
                while(Busy && Page=="Game" && Model==current)yield return null;
                if(Page!="Game" || Model!=current || current.State!=RoundState.Playing)yield break;
                bool exhausted;
                var solution=PuzzleModel.Solve(current,100000,out exhausted);
                if(solution==null)
                {
                    Debug.LogWarning(exhausted?"自动通关搜索达到预算，请重试或重新开始当前关卡。":"当前局面未找到通关解，请重新开始当前关卡。");
                    yield break;
                }
                int expectedMoves=current.Moves,next=0;
                float started=Time.time;
                const float launchWindow=2.8f;
                while(next<solution.Count)
                {
                    if(Page!="Game" || Model!=current)yield break;
                    // 按整关时间排程，帧率较低时同帧补发，允许飞出和三消并行。
                    int due=Mathf.Min(solution.Count,1+Mathf.FloorToInt((Time.time-started)/launchWindow*solution.Count));
                    while(next<due)
                    {
                        int index=solution[next++];
                        if(current.Moves!=expectedMoves || !current.CanExit(index))yield break;
                        ClickArrow(index);expectedMoves++;
                    }
                    yield return null;
                }
                // 末批箭头与三消继续保持快速反馈，结束后由正常流程播放胜利动效。
                while(Busy && Page=="Game" && Model==current)yield return null;
            }
            finally
            {
                DebugAutoPlaying=false;
                if(currentBoard && board==currentBoard && Page=="Game" && current.State==RoundState.Playing)currentBoard.raycastTarget=wasInteractive;
            }
        }
#endif
        IEnumerator FinishRound()
        {
            if(Model.State==RoundState.Won)
            {
                board.raycastTarget=false;
                yield return board.RestoreOverview(.45f);
                const float revealDuration=2.6f;
                for(float t=0;t<revealDuration;t+=Time.deltaTime){board.CompletionReveal=t/revealDuration;yield return null;}
                board.CompletionReveal=1;
                yield return new WaitForSeconds(1.6f);
            }
            else yield return new WaitForSeconds(1.1f);
            Result();
        }
        // Reuse the project's sparkle sprite inside the board mask, with a short fading wake.
        IEnumerator ArrowTrail(int index)
        {
            var owner=board;var tint=PuzzleModel.DisplayColor(Model.Level,Model.Level.initialSegments[index].colorDisplayIndex);
            const float life=.16f;float next=0;
            var images=new List<Image>();var born=new List<float>();
            while(owner && (owner.Motions.ContainsKey(index) || images.Count>0))
            {
                float now=Time.time;
                if(owner.Motions.ContainsKey(index) && owner.Motions[index].Travel>0 && now>=next)
                {
                    var path=owner.Path(index);
                    if(path.Count>1)
                    {
                        var tail=path[path.Count-1];var image=Icon("sparkle",owner.transform,tail,new Vector2(15,15),tint);
                        image.raycastTarget=false;image.name="Arrow Trail";image.transform.SetAsFirstSibling();images.Add(image);born.Add(now);
                    }
                    next=now+.018f;
                }
                for(int n=images.Count-1;n>=0;n--)
                {
                    float age=(now-born[n])/life;
                    if(age>=1){if(images[n])Destroy(images[n].gameObject);images.RemoveAt(n);born.RemoveAt(n);continue;}
                    if(!images[n])continue;var c=tint;c.a=(1-age)*.55f;images[n].color=c;
                    images[n].rectTransform.localScale=Vector3.one*(1-age*.8f);
                }
                yield return null;
            }
        }
        void Refresh()
        {
            if(progress)progress.text=$"剩余 {Model.Remaining} 枚箭头     已完成 {Model.Matches} 组";
            progressFill.rectTransform.sizeDelta=new Vector2(592*(1-Model.Remaining/(float)Model.Level.initialSegments.Count),5);
            if(trayCount)trayCount.text=$"{Model.Tray.Count} / {Model.Level.slotCapacity}";if(trayCount)trayCount.color=Model.Tray.Count>=5?gold:muted;
        }
        void UndoMove()
        {
            if(Busy || Page!="Game")return;
            if(moveHistory.Count==0){SetStatus("还没有需要撤回的移动",muted);return;}
            var data=Model.Level;int health=Model.Health;moveHistory.RemoveAt(moveHistory.Count-1);var previous=moveHistory.ToArray();Model=new PuzzleModel(data,health);foreach(int i in previous)Model.Collect(i);board.Model=Model;board.ResetPaths();
            foreach(var r in tokens)Destroy(r.gameObject);tokens.Clear();logicalTray.Clear();
            for(int n=0;n<Model.Tray.Count;n++)
            {
                var view=Token(Model.Tray[n],tray,new Vector2(TrayX(n),0));tokens.Add(view);
                logicalTray.Add(new CollectionAnimation{Color=Model.Tray[n],View=view,Arrived=true});
            }
            combo=0;lastMatchMove=-99;Refresh();SetStatus("退一步，换一种颜色试试",muted);audioFeedback.Play("land",.25f);
        }
        void ShowHint()
        {
            if(Busy || Model.State!=RoundState.Playing)return;var available=Model.Available();
            if(available.Count==0){SetStatus("没有出口了，可以撤回重新规划",gold);return;}
            int pick=available.OrderByDescending(i=>Model.Tray.Count(c=>c==Model.Level.initialSegments[i].colorDisplayIndex)).First();
            board.Hint=pick;board.Redraw();audioFeedback.Play("hint-sound",.23f);SetStatus("跟随发亮的箭头，先补齐已有的颜色",gold);
            Burst(effectsLayer,board.Head(pick)+boardViewport.anchoredPosition,gold,7);
        }
        void SetStatus(string text,Color color){if(!status)return;status.text=text;status.color=color;statusUntil=Time.unscaledTime+3;}
        void AddBackdrop()
        {
            var pattern=Rect("Quiet full-screen arrow pattern",screen,Vector2.zero,PortraitViewport.DesignSize).gameObject.AddComponent<BackdropPattern>();pattern.raycastTarget=false;pattern.transform.SetAsFirstSibling();
        }
        void AddMenuWatermark()
        {
            // Match the preview's 12% watermark after linear-space UI blending.
            float alpha=QualitySettings.activeColorSpace==ColorSpace.Linear?.03f:.12f;
            var image=Icon("menu-art",screen,new Vector2(0,-80),new Vector2(1150,1150),new Color(1,1,1,alpha));
            image.name="Menu artwork watermark";
            image.rectTransform.localEulerAngles=new Vector3(0,0,12);
            image.transform.SetAsFirstSibling();
        }
        IEnumerator WordFeedback(string word)
        {
            const float origin=465f;
            const float feedbackWidth=239f;
            const float feedbackScale=feedbackWidth/480f;
            var group=Rect("Achievement "+word,effectsLayer,new Vector2(0,origin),new Vector2(500,190)*feedbackScale);
            var art=Icon("feedback-"+word.ToLowerInvariant(),group,Vector2.zero,new Vector2(feedbackWidth,185*feedbackScale),Color.white);
            var fade=group.gameObject.AddComponent<CanvasGroup>();
            audioFeedback.Play("voice-"+word.ToLowerInvariant(),.75f);
            StartCoroutine(ComboParticles(new Vector2(0,origin)));
            for(float t=0;t<1.4f;t+=Time.deltaTime)
            {
                float enter=Mathf.Clamp01(t/.22f);group.localScale=Vector3.one*Mathf.LerpUnclamped(.15f,1,Back(enter));
                group.anchoredPosition=new Vector2(0,origin);
                group.localEulerAngles=Vector3.zero;
                fade.alpha=t<1?1:1-(t-1)/.4f;yield return null;
            }
            Destroy(group.gameObject);
        }
        IEnumerator ComboParticles(Vector2 center)
        {
            var pieces=new List<Image>();var starts=new List<Vector2>();var speeds=new List<Vector2>();
            for(int n=0;n<28;n++)
            {
                float angle=n*137.5f*Mathf.Deg2Rad;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)*.65f);
                var start=center+new Vector2(direction.x*78,direction.y*26);float size=n%4==0?48:Random.Range(20,32);
                var image=Icon(n%4==0?"combo-star":"combo-shard",effectsLayer,start,new Vector2(size,size),n%3==0?mint:n%3==1?gold:new Color(.8f,.7f,1));
                image.name="Combo particle";pieces.Add(image);starts.Add(start);speeds.Add(direction*Random.Range(100,220));
            }
            for(float t=0;t<.85f;t+=Time.deltaTime)
            {
                float p=t/.85f;
                for(int n=0;n<pieces.Count;n++)
                {
                    var image=pieces[n];if(!image)continue;
                    image.rectTransform.anchoredPosition=starts[n]+speeds[n]*(1-Mathf.Pow(1-p,2))*.65f+Vector2.down*24*p*p;
                    image.rectTransform.localEulerAngles=new Vector3(0,0,n*37+p*(n%2==0?160:-160));
                    image.rectTransform.localScale=Vector3.one*Mathf.Lerp(1,.5f,p);
                    var c=image.color;c.a=Mathf.Clamp01(p/.08f)*(1-p*p);image.color=c;
                }
                yield return null;
            }
            foreach(var image in pieces)if(image)Destroy(image.gameObject);
        }
        void Result()
        {
            bool win=Model.State==RoundState.Won;var data=Model.Level;
            Clear(win?"Won":"Lost");audioFeedback.Play(win?"win":"lose",win?.42f:.23f);
            Label($"LEVEL {data.levelId:00}",screen,new Vector2(0,520),new Vector2(400,40),21,gold,true).rectTransform.localScale=Vector3.one*2;
            Label(win?"关卡完成":"暂未通关",screen,new Vector2(0,436),new Vector2(600,80),60,white);
            // Original cartoon artwork, aligned to the same visual center without a halo.
            Icon(win?"crown":"hint",screen,new Vector2(0,234),win?new Vector2(264,210):new Vector2(150,195),Color.white);
            for(int n=0;n<3;n++)
            {
                var star=Icon(win && n<Model.Health?"star":"star-empty",screen,new Vector2((n-1)*104,n==1?-64:-84),new Vector2(n==1?105:80,n==1?105:80),Color.white);
                star.name="Result star "+(n+1);star.rectTransform.localEulerAngles=new Vector3(0,0,(n-1)*-12);StartCoroutine(StarEnter(star.rectTransform,.1f+n*.13f));
            }
            Button(win?"下一关":"再试一次",screen,new Vector2(0,-307),new Vector2(230,96),()=>{if(win){LevelNumber=LevelNumber%levels.Count+1;StartLevel();}else StartLevel(data);},true);
            Button("回到首页",screen,new Vector2(0,-425),new Vector2(230,76),Home);
            effectsLayer.SetAsLastSibling();ApplySafeAreaLayout();StartCoroutine(Entrance(pageRoot));if(win)StartCoroutine(ResultFireworks());
        }
        IEnumerator ResultFireworks()
        {
            yield return new WaitForSeconds(.12f);
            audioFeedback.Play("fireworks",.55f);
            var layer=Rect("Side fireworks",effectsLayer,Vector2.zero,PortraitViewport.DesignSize);
            var pieces=new List<Image>();var velocities=new List<Vector2>();var origins=new List<Vector2>();var delays=new List<float>();
            for(int side=-1;side<=1;side+=2)for(int n=0;n<54;n++)
            {
                float size=Random.Range(13,26);var start=new Vector2(side*390,-170+Random.Range(-30,30));
                var image=Icon(n%6==0?"combo-star":"confetti-"+(n%4),layer,start,new Vector2(size,size),Color.white);
                image.name="Firework particle";image.color=new Color(1,1,1,0);pieces.Add(image);origins.Add(start);
                velocities.Add(new Vector2(-side*Random.Range(180,420),Random.Range(480,800)));delays.Add(n%2*.24f+Random.Range(0,.1f));
            }
            for(float t=0;t<3.2f;t+=Time.deltaTime)
            {
                for(int n=0;n<pieces.Count;n++)
                {
                    float age=t-delays[n];if(age<0)continue;var image=pieces[n];
                    image.rectTransform.anchoredPosition=origins[n]+velocities[n]*age+Vector2.down*(240*age*age);
                    image.rectTransform.localEulerAngles=new Vector3(0,0,n*53+age*(n%2==0?160:-160));
                    image.rectTransform.localScale=new Vector3(.55f+.45f*Mathf.Abs(Mathf.Cos(age*7+n)),1,1);
                    image.color=new Color(1,1,1,Mathf.Clamp01(age/.06f)*Mathf.Clamp01((2.8f-age)/.7f));
                }
                yield return null;
            }
            Destroy(layer.gameObject);
        }
        void Burst(Transform parent,Vector2 center,Color color,int count){StartCoroutine(Particles(parent,center,color,count));}
        IEnumerator Particles(Transform parent,Vector2 center,Color color,int count)
        {
            var pieces=new List<RectTransform>();var velocities=new List<Vector2>();var rotations=new List<float>();
            for(int i=0;i<count;i++){float angle=(i*137.5f+Random.Range(-15,15))*Mathf.Deg2Rad;float speed=Random.Range(70,190);var im=Icon(i%3==0?"star":"sparkle",parent,center,new Vector2(i%3==0?18:11,i%3==0?18:11),i%3==0?gold:color);pieces.Add(im.rectTransform);velocities.Add(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed);rotations.Add(Random.Range(-160,160));}
            for(float t=0;t<.65f;t+=Time.deltaTime){float p=t/.65f;for(int n=0;n<pieces.Count;n++){if(!pieces[n])continue;pieces[n].anchoredPosition=center+velocities[n]*t+new Vector2(0,-100*t*t);pieces[n].localEulerAngles=new Vector3(0,0,rotations[n]*t);pieces[n].localScale=Vector3.one*(1-.8f*p);var img=pieces[n].GetComponent<Image>();var c=img.color;c.a=1-p;img.color=c;}yield return null;}
            foreach(var r in pieces)if(r)Destroy(r.gameObject);
        }
        IEnumerator StarEnter(RectTransform r,float delay){r.localScale=Vector3.zero;yield return new WaitForSeconds(delay);for(float t=0;t<.28f;t+=Time.deltaTime){float p=t/.28f;r.localScale=Vector3.one*Back(p);yield return null;}r.localScale=Vector3.one;}
        IEnumerator DialogEnter(RectTransform r){r.localScale=Vector3.one*.85f;for(float t=0;t<.25f;t+=Time.deltaTime){r.localScale=Vector3.one*Mathf.LerpUnclamped(.85f,1,Back(t/.25f));yield return null;}r.localScale=Vector3.one;}
        IEnumerator Entrance(RectTransform page){var group=page.gameObject.AddComponent<CanvasGroup>();group.alpha=0;for(float t=0;t<.18f;t+=Time.deltaTime){group.alpha=Ease(t/.18f);yield return null;}group.alpha=1;}
        IEnumerator Move(RectTransform r,Vector2 from,Vector2 to,float duration){for(float t=0;t<duration;t+=Time.deltaTime){if(!r)yield break;r.anchoredPosition=Vector2.Lerp(from,to,Ease(t/duration));yield return null;}if(r)r.anchoredPosition=to;}
        IEnumerator Punch(RectTransform r,float duration,float scale){for(float t=0;t<duration;t+=Time.deltaTime){if(!r)yield break;r.localScale=Vector3.one*(1+(scale-1)*Mathf.Sin(t/duration*Mathf.PI));yield return null;}if(r)r.localScale=Vector3.one;}
        static float Ease(float p)=>1-Mathf.Pow(1-Mathf.Clamp01(p),3);
        static float Back(float p)=>1+2.7f*Mathf.Pow(p-1,3)+1.7f*Mathf.Pow(p-1,2);
    }
}
