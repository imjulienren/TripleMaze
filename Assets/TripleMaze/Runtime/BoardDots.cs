using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
namespace TripleMaze
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardDots : MaskableGraphic
    {
        public ArrowBoardGraphic Board;
        LevelData cachedLevel,lastDrawnLevel;
        Vector2 lastOrigin,lastStep;
        float lastReveal=float.NaN;
        readonly List<Vector2Int> cells=new List<Vector2Int>();
        readonly List<Color> colors=new List<Color>();
        Texture2D sourceTexture;
        RawImage sourceImage;
        void ReleaseImage()
        {
            if(sourceImage)sourceImage.texture=null;
            if(sourceTexture)Destroy(sourceTexture);
            sourceTexture=null;
        }
        void CachePattern(LevelData level)
        {
            if(ReferenceEquals(cachedLevel,level))return;
            cachedLevel=level;cells.Clear();colors.Clear();ReleaseImage();
            if(!string.IsNullOrEmpty(level.sourceImagePng))
            {
                try
                {
                    sourceTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    if(!sourceTexture.LoadImage(System.Convert.FromBase64String(level.sourceImagePng)))ReleaseImage();
                }
                catch(System.Exception ex){ReleaseImage();Debug.LogWarning("关卡底图无法读取，使用格点配色："+ex.Message);}
            }
            if(sourceTexture)
            {
                sourceTexture.wrapMode=TextureWrapMode.Clamp;sourceTexture.filterMode=FilterMode.Bilinear;
                if(!sourceImage)
                {
                    var go=new GameObject("Completed source image",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));
                    go.transform.SetParent(transform,false);sourceImage=go.GetComponent<RawImage>();sourceImage.raycastTarget=false;
                }
                sourceImage.texture=sourceTexture;sourceImage.color=new Color(1,1,1,0);
            }
            else if(sourceImage)sourceImage.color=Color.clear;
            // Preserve the occupied stencil even after every arrow has left the board.
            var seen=new HashSet<Vector2Int>();
            foreach(var arrow in level.initialSegments)foreach(var p in PuzzleModel.Cells(arrow))
                if(seen.Add(p))
                {
                    cells.Add(p);
                    colors.Add(sourceTexture?sourceTexture.GetPixelBilinear((p.x+.5f)/level.gridWidth,1-(p.y+.5f)/level.gridHeight):PuzzleModel.DisplayColor(level,arrow.colorDisplayIndex));
                }
        }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(!Board || Board.Model==null || !ReferenceEquals(cachedLevel,Board.Model.Level))return;var d=Board.Model.Level;
            float progress=Mathf.Clamp01(Board.CompletionReveal);
            float spacing=Mathf.Abs((Board.GridPosition(Vector2Int.right)-Board.GridPosition(Vector2Int.zero)).x);
            float mosaicFade=sourceTexture?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.90f,1,progress)):1;
            for(int i=0;i<cells.Count;i++)
            {
                var cell=cells[i];Vector2 p=Board.GridPosition(cell);
                float distance=Vector2.Distance(cell,new Vector2(d.gridWidth-1,d.gridHeight-1)*.5f)/Mathf.Max(1,new Vector2(d.gridWidth-1,d.gridHeight-1).magnitude*.5f);
                float fill=Mathf.SmoothStep(0,1,Mathf.InverseLerp(distance*.28f,distance*.28f+.48f,progress));
                var c=Color.Lerp(new Color(.65f,.77f,.88f,.18f),colors[i],fill);c.a*=mosaicFade;
                if(fill<=0)
                {
                    // The in-game dots retain their existing appearance.
                    int center=v.currentVertCount;v.AddVert(p,c,Vector2.zero);
                    for(int n=0;n<6;n++){float a=n*Mathf.PI/3;v.AddVert(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*1.2f,c,Vector2.zero);}
                    for(int n=0;n<6;n++)v.AddTriangle(center,center+1+n,center+1+(n+1)%6);
                }
                else
                {
                    float half=Mathf.Lerp(1.2f,spacing*.5f,fill);int k=v.currentVertCount;
                    v.AddVert(p+new Vector2(-half,-half),c,Vector2.zero);v.AddVert(p+new Vector2(-half,half),c,Vector2.zero);
                    v.AddVert(p+new Vector2(half,half),c,Vector2.zero);v.AddVert(p+new Vector2(half,-half),c,Vector2.zero);
                    v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);
                }
            }
        }
        void LateUpdate()
        {
            if(!Board || Board.Model==null)return;
            CachePattern(Board.Model.Level);
            var origin=Board.GridPosition(Vector2Int.zero);var step=Board.GridPosition(Vector2Int.one)-origin;
            if(sourceImage && sourceTexture)
            {
                var d=Board.Model.Level;var rect=sourceImage.rectTransform;
                rect.anchoredPosition=(origin+Board.GridPosition(new Vector2Int(d.gridWidth-1,d.gridHeight-1)))*.5f;
                rect.sizeDelta=new Vector2(Mathf.Abs(step.x)*d.gridWidth,Mathf.Abs(step.y)*d.gridHeight);
                // Blend in full image detail after the cells fill, then retire the mosaic underneath.
                sourceImage.color=new Color(1,1,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,.90f,Board.CompletionReveal)));
            }
            if(ReferenceEquals(lastDrawnLevel,Board.Model.Level) && origin==lastOrigin && step==lastStep && Board.CompletionReveal==lastReveal)return;
            lastDrawnLevel=Board.Model.Level;lastOrigin=origin;lastStep=step;lastReveal=Board.CompletionReveal;SetVerticesDirty();
        }
        protected override void OnDestroy(){ReleaseImage();base.OnDestroy();}
    }
    public static class StreakWords
    {
        public static string At(int groups){if(groups>=9 && groups%3==0)return "Excellent";switch(groups){case 2:return "Good";case 4:return "Great";case 6:return "Perfect";default:return null;}}
    }
}
