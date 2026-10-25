using System;
using System.Collections.Generic;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public enum ImageBackground { AutoBorder, White, Alpha, None }
    [Serializable] public class GenerationTemplate
    {
        public int width=20,height=24;
        public string source="手绘轮廓";
        public string sourceImagePng="";
        public List<bool> mask=new List<bool>();
        public List<Color> samples=new List<Color>();
        public List<Color> palette=new List<Color>();
        public int Count {get {int n=0;foreach(bool p in mask)if(p)n++;return n;}}
        public bool Contains(int x,int y)=>x>=0 && y>=0 && x<width && y<height && mask.Count==width*height && mask[y*width+x];
        public Color ColorAt(Vector2Int p)=>samples.Count==width*height?samples[p.y*width+p.x]:ArrowBoardGraphic.Palette[2];
        public void Reset(int w,int h)
        {width=w;height=h;mask=new List<bool>(new bool[w*h]);samples=new List<Color>();for(int i=0;i<w*h;i++)samples.Add(ArrowBoardGraphic.Palette[2]);}
        public GenerationTemplate Copy()=>JsonUtility.FromJson<GenerationTemplate>(JsonUtility.ToJson(this));
    }
    [Serializable] public class GenerationSettings
    {
        public bool fillImage=true;
        public int imageArrowLength=6;
        public int seed=12345,arrowCount=30,colorCount=5,slots=7,attempts=24,candidateCount=4,probeRuns=32;
        public float arrowTarget=50,matchTarget=45,tolerance=15,colorFidelity=.75f,coverage=.8f;
        public bool allowExtraColors=true,brightenDarkColors=true;
        public string preset="标准";
        public void Preset(int index)
        {
            string[] names={"入门","轻松","标准","困难","挑战"};preset=names[index];
            arrowCount=new[]{12,18,30,42,54}[index];colorCount=new[]{3,4,4,5,6}[index];
            arrowTarget=new[]{18,32,50,68,82}[index];matchTarget=new[]{0,20,45,65,80}[index];
            if(fillImage){colorCount=5;arrowTarget=new[]{40,50,60,72,84}[index];}
        }
        public GenerationSettings Copy()=>JsonUtility.FromJson<GenerationSettings>(JsonUtility.ToJson(this));
    }
    [Serializable] public class DifficultyReport
    {
        public int arrows,colors,initialExits,maxDepth,minimumPeak=-1,knownPeak,randomTrials,searchNodes;
        public float meanDepth,meanBlockers,meanChoices,meanTurns,arrowScore,matchScore,overallScore,randomFailureRate,greedyFailureRate;
        public bool structurallySolvable,solutionVerified,minimumPeakExact,searchExhausted,slotPressurePossible;
        public string note="";
        public List<int> solution=new List<int>();
    }
    [Serializable] public class GeneratedCandidate
    {
        public LevelData level;
        public DifficultyReport difficulty;
        public int seed;
        public float coverage,colorError,targetDistance;
        public bool targetReached;
        public string warning="";
    }
    [Serializable] public class GenerationRecipe
    {
        public int version=1;
        public GenerationTemplate template;
        public GenerationSettings settings;
        public GeneratedCandidate result;
    }
}
