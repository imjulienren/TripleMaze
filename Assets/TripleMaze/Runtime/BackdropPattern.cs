using UnityEngine;
using UnityEngine.UI;
namespace TripleMaze
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BackdropPattern : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            for(int y=-4;y<=4;y++)for(int x=-3;x<=3;x++)
            {
                Vector2 p=new Vector2(x*164+(y%2)*72,y*174);float sign=(x+y)%2==0?1:-1;
                var c=new Color(.55f,.8f,.82f,.055f);if((x+y)%3==0)c=new Color(.72f,.62f,.88f,.045f);
                Vector2 a=p+new Vector2(-34,-30)*sign,b=p+new Vector2(25,-30)*sign,d=p+new Vector2(25,35)*sign;
                Line(v,a,b,c);Line(v,b,d,c);Line(v,d,d+new Vector2(-13,-14)*sign,c);Line(v,d,d+new Vector2(13,-14)*sign,c);
            }
        }
        static void Line(VertexHelper v,Vector2 a,Vector2 b,Color c)
        {var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*1.8f;int k=v.currentVertCount;v.AddVert(a-n,c,Vector2.zero);v.AddVert(a+n,c,Vector2.zero);v.AddVert(b+n,c,Vector2.zero);v.AddVert(b-n,c,Vector2.zero);v.AddTriangle(k,k+1,k+2);v.AddTriangle(k,k+2,k+3);}
    }
}
