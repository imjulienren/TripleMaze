using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TripleMaze
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class ArrowBoardGraphic : MaskableGraphic, IPointerClickHandler, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerDownHandler, IPointerUpHandler
    {
        public PuzzleModel Model;
        public Action<int> Clicked;
        public sealed class Motion
        {
            public float Travel, FlashAmount;
            public int ImpactTarget=-1;
            public Vector2 TargetRecoil;
            public Vector2? BlockPoint;
        }
        public readonly Dictionary<int,Motion> Motions=new Dictionary<int,Motion>();
        public int Moving { get { foreach(var pair in Motions)return pair.Key;return -1; } }
        public float Travel => Moving>=0?Motions[Moving].Travel:0;
        public int ImpactTarget { get { foreach(var motion in Motions.Values)if(motion.ImpactTarget>=0)return motion.ImpactTarget;return -1; } }
        public int Hint=-1, Pressed=-1;
        public float WinWave=-1, Reveal=1;
        public float ContactTravel => Mathf.Max(0,.88f-Mathf.Clamp(Scale*.155f,4.2f,7.6f)*.5f/Scale);
        public System.Action<int> PressedArrow;
        readonly Dictionary<int,List<Vector2>> cached=new Dictionary<int,List<Vector2>>();
        const int ArrowsPerLayer=32;
        readonly List<ArrowBoardLayer> renderLayers=new List<ArrowBoardLayer>();
        bool Layered=>Model!=null && Model.Level.initialSegments.Count>64;
        float lastScale;Vector2 lastPan;
        public float Zoom=1;
        Vector2 pan;
        float suppressClickUntil;
        public static readonly Color[] Palette={new Color32(255,148,137,255),new Color32(115,198,255,255),new Color32(130,224,186,255),new Color32(193,166,255,255),new Color32(255,208,116,255),new Color32(248,154,206,255)};
        float Scale => Model==null?1:Mathf.Min((rectTransform.rect.width-36)/Model.Level.gridWidth,(rectTransform.rect.height-36)/Model.Level.gridHeight)*Zoom;
        Vector2 Point(Vector2 p) => new Vector2((p.x-(Model.Level.gridWidth-1)*.5f)*Scale,((Model.Level.gridHeight-1)*.5f-p.y)*Scale)+pan;
        public void Redraw()
        {EnsureRenderLayers();SetVerticesDirty();foreach(var layer in renderLayers)if(layer)layer.SetVerticesDirty();}
        void EnsureRenderLayers()
        {
            int desired=Layered?(Model.Level.initialSegments.Count+ArrowsPerLayer-1)/ArrowsPerLayer-1:0;
            while(renderLayers.Count<desired)
            {
                var go=new GameObject("Arrow mesh "+(renderLayers.Count+2),typeof(RectTransform),typeof(CanvasRenderer),typeof(ArrowBoardLayer));
                var rect=go.GetComponent<RectTransform>();rect.SetParent(transform,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var layer=go.GetComponent<ArrowBoardLayer>();layer.Board=this;layer.StartIndex=(renderLayers.Count+1)*ArrowsPerLayer;layer.raycastTarget=false;renderLayers.Add(layer);
            }
            while(renderLayers.Count>desired){int last=renderLayers.Count-1;var layer=renderLayers[last];renderLayers.RemoveAt(last);if(layer)Destroy(layer.gameObject);}
        }
        public void ResetPaths(){cached.Clear();Redraw();}
        public Vector2 GridPosition(Vector2Int p)=>Point(p);
        public Vector2 Head(int index)=>Point(Model.Level.initialSegments[index].points[0]);
        public float ExitDistance(int index)
        {
            // Begin the tray flight when the visible tip reaches the clipping edge, including zoom and pan.
            var head=Head(index);var dir=PuzzleModel.Direction(Model.Level.initialSegments[index]);
            var bounds=rectTransform.rect;
            float pixels=dir.x>0?bounds.xMax-head.x:dir.x<0?head.x-bounds.xMin:dir.y>0?head.y-bounds.yMin:bounds.yMax-head.y;
            float tip=Mathf.Clamp(Scale*.34f,8,15)*.12f+Mathf.Clamp(Scale*.155f,4.2f,7.6f)*.46f;
            return Mathf.Max(0,(pixels-tip)/Scale);
        }
        public void SetZoom(float value){Zoom=Mathf.Clamp(value,1,3);if(Zoom==1)pan=Vector2.zero;Redraw();}
        public void OnBeginDrag(PointerEventData e){suppressClickUntil=Time.unscaledTime+.25f;}
        public void OnDrag(PointerEventData e)
        {if(Zoom<=1)return;RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var current);RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position-e.delta,e.pressEventCamera,out var previous);pan+=current-previous;pan=Vector2.Max(-rectTransform.rect.size*(Zoom-1)*.5f,Vector2.Min(rectTransform.rect.size*(Zoom-1)*.5f,pan));suppressClickUntil=Time.unscaledTime+.25f;Redraw();}
        public void OnScroll(PointerEventData e){SetZoom(Zoom+e.scrollDelta.y*.15f);}
        void Update()
        {
            EnsureRenderLayers();
            if(Hint>=0)Redraw();
            if(!raycastTarget || Input.touchCount!=2)return;
            var a=Input.GetTouch(0);var b=Input.GetTouch(1);
            if(!RectTransformUtility.RectangleContainsScreenPoint(rectTransform,a.position))return;
            float before=(a.position-a.deltaPosition-b.position+b.deltaPosition).magnitude;
            if(before>1)SetZoom(Zoom*(a.position-b.position).magnitude/before);
            suppressClickUntil=Time.unscaledTime+.3f;
        }
        public List<Vector2> Path(int index)
        {
            var a=Model.Level.initialSegments[index];var result=new List<Vector2>();
            float travel=Motions.TryGetValue(index,out var motion)?motion.Travel:0;
            if(travel<=0) {foreach(var p in a.points) result.Add(Point(p));return result;}
            var path=new List<Vector2>();for(int i=a.points.Count-1;i>=0;i--) path.Add(a.points[i]);
            float length=0;for(int i=1;i<path.Count;i++) length+=Vector2.Distance(path[i-1],path[i]);
            Vector2 dir=PuzzleModel.Direction(a);path.Add(path[path.Count-1]+dir*(travel+Model.Level.gridWidth+Model.Level.gridHeight));
            float lo=travel,hi=length+travel,at=0;
            for(int i=1;i<path.Count;i++)
            {
                float len=Vector2.Distance(path[i-1],path[i]),end=at+len;
                if(end>=lo && at<=hi)
                {
                    float from=Mathf.Clamp01((lo-at)/len),to=Mathf.Clamp01((hi-at)/len);
                    if(result.Count==0) result.Add(Point(Vector2.Lerp(path[i-1],path[i],from)));
                    result.Add(Point(Vector2.Lerp(path[i-1],path[i],to)));
                }
                at=end;if(at>=hi)break;
            }
            result.Reverse();return result;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {PopulateRange(vh,0,Layered?ArrowsPerLayer:int.MaxValue);}
        public void PopulateRange(VertexHelper vh,int first,int count)
        {
            vh.Clear();if(Model==null)return;
            if(Mathf.Abs(lastScale-Scale)>.01f || lastPan!=pan){cached.Clear();lastScale=Scale;lastPan=pan;}
            for(int i=first;i<Mathf.Min(Model.Level.initialSegments.Count,first+count);i++)
            {
                bool moving=Motions.TryGetValue(i,out var motion);
                if(Model.Removed.Contains(i) && !moving)continue;
                List<Vector2> pts;
                if(moving || !cached.TryGetValue(i,out pts))
                {
                    var raw=Path(i);var source=new List<Vector3>();foreach(var p in raw)source.Add(p);
                    var smooth=new List<Vector3>();PathSmoother.SmoothPath(ref smooth,source,0,Scale*.23f,4);
                    pts=new List<Vector2>();foreach(var p in smooth)pts.Add(p);
                    if(!moving)cached[i]=pts;
                }
                if(pts.Count<2)continue;
                var c=PuzzleModel.DisplayColor(Model.Level,Model.Level.initialSegments[i].colorDisplayIndex);
                float width=Mathf.Clamp(Scale*.155f,4.2f,7.6f);
                if(i==Hint){width*=1.12f;c=Color.Lerp(c,Color.white,.18f+.12f*Mathf.Sin(Time.unscaledTime*5));}
                if(i==Pressed){width*=1.14f;c=Color.Lerp(c,Color.white,.22f);}
                Vector2 shift=Vector2.zero;
                if(moving)c=Color.Lerp(c,new Color(1,.43f,.4f),motion.FlashAmount);
                if(!moving)foreach(var hit in Motions.Values)if(hit.ImpactTarget==i)shift+=hit.TargetRecoil;
                c.a*=Reveal;
                Stroke(vh,pts,width+2,new Color(0,0,0,.22f*Reveal),shift+new Vector2(0,-2));
                Stroke(vh,pts,width,c,shift);
                var head=pts[0]+shift;var dir=(pts[0]-pts[1]).normalized;var side=new Vector2(-dir.y,dir.x);float size=Mathf.Clamp(Scale*.34f,8,15);
                var headPath=new List<Vector2>{head-dir*size+side*size*.62f,head+dir*size*.12f,head-dir*size-side*size*.62f};
                Stroke(vh,headPath,width*.92f,c,Vector2.zero);
            }
            if(first==0)foreach(var hit in Motions.Values)
                if(hit.BlockPoint.HasValue)Disc(vh,hit.BlockPoint.Value,7,new Color(1,.57f,.5f,hit.FlashAmount));
        }
        static void Stroke(VertexHelper v,List<Vector2> ps,float width,Color c,Vector2 offset)
        {
            int start=v.currentVertCount;
            for(int i=0;i<ps.Count;i++)
            {
                Vector2 direction=i==0?(ps[1]-ps[0]).normalized:i==ps.Count-1?(ps[i]-ps[i-1]).normalized:((ps[i]-ps[i-1]).normalized+(ps[i+1]-ps[i]).normalized).normalized;
                Vector2 normal=new Vector2(-direction.y,direction.x),p=ps[i]+offset;var clear=c;clear.a=0;
                v.AddVert(p+normal*(width*.5f+1),clear,Vector2.zero);v.AddVert(p+normal*width*.5f,c,Vector2.zero);v.AddVert(p-normal*width*.5f,c,Vector2.zero);v.AddVert(p-normal*(width*.5f+1),clear,Vector2.zero);
                if(i==0)continue;int a=start+(i-1)*4,b=start+i*4;
                for(int n=0;n<3;n++){v.AddTriangle(a+n,b+n,b+n+1);v.AddTriangle(a+n,b+n+1,a+n+1);}
            }
            Disc(v,ps[0]+offset,width*.5f,c);Disc(v,ps[ps.Count-1]+offset,width*.5f,c);
        }
        static void Quad(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {int n=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddVert(d,color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);}
        static void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int n=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddTriangle(n,n+1,n+2);}
        static void Disc(VertexHelper v,Vector2 p,float r,Color c)
        {
            int start=v.currentVertCount;v.AddVert(p,c,Vector2.zero);
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8;v.AddVert(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c,Vector2.zero);}
            for(int i=0;i<16;i++)v.AddTriangle(start,start+1+i,start+1+(i+1)%16);
        }
        int Pick(PointerEventData e)
        {
            if(Model==null)return -1;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var p);
            float best=Mathf.Max(15,Scale*.38f);int picked=-1;
            for(int i=0;i<Model.Level.initialSegments.Count;i++)
            {
                if(Model.Removed.Contains(i) || Motions.ContainsKey(i))continue;var a=Path(i);
                for(int j=1;j<a.Count;j++){var ab=a[j]-a[j-1];float t=Mathf.Clamp01(Vector2.Dot(p-a[j-1],ab)/ab.sqrMagnitude);float dist=Vector2.Distance(p,a[j-1]+ab*t);if(dist<best){best=dist;picked=i;}}
            }
            return picked;
        }
        public void OnPointerDown(PointerEventData e)
        {Pressed=Pick(e);if(Pressed>=0)PressedArrow?.Invoke(Pressed);Redraw();}
        public void OnPointerUp(PointerEventData e){Pressed=-1;Redraw();}
        public void OnPointerClick(PointerEventData e)
        {
            if(Model==null || Time.unscaledTime<suppressClickUntil)return;
            int picked=Pick(e);if(picked>=0)Clicked?.Invoke(picked);
        }
    }
}
