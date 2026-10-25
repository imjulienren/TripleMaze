using UnityEngine;
using UnityEngine.UI;
namespace TripleMaze
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BoardDots : MaskableGraphic
    {
        public ArrowBoardGraphic Board;
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(!Board || Board.Model==null)return;var d=Board.Model.Level;
            for(int y=0;y<d.gridHeight;y++)for(int x=0;x<d.gridWidth;x++)
            {
                Vector2 p=Board.GridPosition(new Vector2Int(x,y));float dist=Vector2.Distance(new Vector2(x,y),new Vector2(d.gridWidth-1,d.gridHeight-1)*.5f)/Mathf.Max(d.gridWidth,d.gridHeight);
                float pulse=Board.WinWave<0?0:Mathf.Max(0,1-Mathf.Abs(Board.WinWave-dist*.8f)/.22f);
                float r=1.2f+pulse*3.5f;Color c=Color.Lerp(new Color(.65f,.77f,.88f,.18f),new Color(.55f,1,.84f,.9f),pulse);
                for(int n=0;n<6;n++){int k=v.currentVertCount;float a=n*Mathf.PI/3,b=(n+1)*Mathf.PI/3;v.AddVert(p,c,Vector2.zero);v.AddVert(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c,Vector2.zero);v.AddVert(p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,c,Vector2.zero);v.AddTriangle(k,k+1,k+2);}
            }
        }
        void LateUpdate(){SetVerticesDirty();}
    }
    public static class StreakWords
    {
        public static string At(int groups){if(groups>=9 && groups%3==0)return "Excellent";switch(groups){case 2:return "Good";case 4:return "Great";case 6:return "Perfect";default:return null;}}
    }
}
