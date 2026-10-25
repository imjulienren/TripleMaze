using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public static class TemplateImageProcessor
    {
        // 保留 ArrowSmash 的 GPU 采样、顶部为零的网格坐标约定，并保留 alpha 供透明图使用。
        static Color[] Sample(Texture2D source,int w,int h)
        {
            var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;Texture2D temp=null;
            try
            {
                Graphics.Blit(source,rt);RenderTexture.active=rt;temp=new Texture2D(w,h,TextureFormat.RGBA32,false);temp.ReadPixels(new Rect(0,0,w,h),0,0);temp.Apply();
                var result=new Color[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)result[y*w+x]=temp.GetPixel(x,h-1-y);return result;
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(temp)UnityEngine.Object.DestroyImmediate(temp);}
        }
        static string EncodeSourceImage(Texture2D source)
        {
            float scale=Mathf.Min(1,1024f/Mathf.Max(source.width,source.height));
            int w=Mathf.Max(1,Mathf.RoundToInt(source.width*scale)),h=Mathf.Max(1,Mathf.RoundToInt(source.height*scale));
            var pixels=Sample(source,w,h);var image=new Texture2D(w,h,TextureFormat.RGBA32,false);
            try
            {
                var bottomUp=new Color[pixels.Length];
                for(int y=0;y<h;y++)System.Array.Copy(pixels,y*w,bottomUp,(h-1-y)*w,w);
                image.SetPixels(bottomUp);image.Apply();return Convert.ToBase64String(image.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        public static Color32[,] SamplePixelsColor(Texture2D source,int w,int h)
        {var pixels=Sample(source,w,h);var result=new Color32[w,h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){var c=pixels[y*w+x];result[x,y]=Color.Lerp(Color.white,c,c.a);}return result;}
        public static GenerationTemplate FromImage(Texture2D source,int longestEdge,ImageBackground background,float threshold,int colorLimit,bool removeOutline,bool keepAspect=true)
        {
            if(!source)throw new ArgumentException("请先选择图片。");
            int w=longestEdge,h=longestEdge;
            if(keepAspect){if(source.width>=source.height)h=Mathf.Max(2,Mathf.RoundToInt(longestEdge*source.height/(float)source.width));else w=Mathf.Max(2,Mathf.RoundToInt(longestEdge*source.width/(float)source.height));}
            var result=new GenerationTemplate();result.Reset(w,h);result.sourceImagePng=EncodeSourceImage(source);result.source=source.name+" ("+source.width+"×"+source.height+")";var pixels=Sample(source,w,h);
            var corners=new[]{pixels[0],pixels[w-1],pixels[(h-1)*w],pixels[w*h-1]};var bg=corners.OrderBy(c=>corners.Sum(other=>Distance(c,other))).First();
            bool transparent=pixels.Any(c=>c.a<.1f);
            for(int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];bool valid=c.a>.15f;
                if(background==ImageBackground.White)valid&=c.r*.299f+c.g*.587f+c.b*.114f<1-threshold;
                else if(background==ImageBackground.AutoBorder && !transparent)valid&=Distance(c,bg)>threshold;
                if(removeOutline)valid&=c.grayscale>.09f;
                result.mask[i]=valid;result.samples[i]=new Color(c.r,c.g,c.b,1);
            }
            // 复用原项目的 Lab 聚类。遮罩内的颜色重新聚类，避免自动背景占据有效色阶。
            var filtered=new Texture2D(w,h,TextureFormat.RGBA32,false);
            try
            {
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)filtered.SetPixel(x,h-1-y,result.mask[y*w+x]?result.samples[y*w+x]:Color.white);
                filtered.Apply();List<string> warnings;
                var grades=ColorGradeDetector.Detect(filtered,w,h,Mathf.Clamp(colorLimit,1,6),.995f,removeOutline?.09f:-1,.12f,out warnings);
                result.palette=grades.Where(g=>g.Role==ColorGradeRole.Fill).Select(g=>(Color)g.Representative).ToList();
                if(result.palette.Count==0 && result.Count>0)result.palette.Add(result.samples.Where((c,i)=>result.mask[i]).First());
            }
            finally{UnityEngine.Object.DestroyImmediate(filtered);}
            return result;
        }
        public static float Distance(Color a,Color b)
        {float r=a.r-b.r,g=a.g-b.g,bl=a.b-b.b;return Mathf.Sqrt(.299f*r*r+.587f*g*g+.114f*bl*bl);}
        public static GenerationTemplate Shape(int w,int h,int shape)
        {
            var t=new GenerationTemplate();t.Reset(w,h);t.palette=new List<Color>{ArrowBoardGraphic.Palette[0],ArrowBoardGraphic.Palette[1],ArrowBoardGraphic.Palette[2],ArrowBoardGraphic.Palette[3]};
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float nx=(x+.5f)/w*2-1,ny=1-(y+.5f)/h*2;bool fill=shape==0 || shape==1 && nx*nx+ny*ny<=.93f || shape==2 && Mathf.Abs(nx)+Mathf.Abs(ny)<1 || shape==3 && Mathf.Pow(nx*nx+ny*ny-0.6f,3)-nx*nx*ny*ny*ny<0;
                t.mask[y*w+x]=fill;t.samples[y*w+x]=t.palette[Mathf.Clamp(x*4/w,0,3)];
            }
            t.source=new[]{"矩形","椭圆","菱形","爱心"}[shape];return t;
        }
        public static void FillPolygon(GenerationTemplate t,List<Vector2Int> polygon,Color color,bool replace)
        {
            if(polygon.Count<3)return;
            for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)
            {
                bool inside=false;for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
                {var a=polygon[i];var b=polygon[j];if((a.y>y+.01f)!=(b.y>y+.01f) && x+.01f<(b.x-a.x)*(y+.01f-a.y)/(b.y-a.y)+a.x)inside=!inside;}
                int index=y*t.width+x;if(replace)t.mask[index]=inside;else if(inside)t.mask[index]=true;if(inside)t.samples[index]=color;
            }
        }
    }
}
