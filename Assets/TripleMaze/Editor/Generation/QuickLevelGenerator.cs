using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public sealed class GenerationJob
    {
        readonly GenerationTemplate template;readonly GenerationSettings settings;
        public readonly List<GeneratedCandidate> Candidates=new List<GeneratedCandidate>();
        public int Completed {get;private set;}
        public bool Finished=>Completed>=settings.attempts;
        public string LastMessage {get;private set;}="等待生成";
        public GenerationJob(GenerationTemplate template,GenerationSettings settings){this.template=template.Copy();this.settings=settings.Copy();}
        public void Step()
        {
            if(Finished)return;int seed=unchecked(settings.seed+Completed*7919);Completed++;
            var c=QuickLevelGenerator.Attempt(template,settings,seed,out string failure);
            if(c==null){LastMessage=failure;return;}
            Candidates.Add(c);Candidates.Sort((a,b)=>a.targetDistance.CompareTo(b.targetDistance));if(Candidates.Count>settings.candidateCount)Candidates.RemoveAt(Candidates.Count-1);
            LastMessage=$"已找到 {Candidates.Count} 个可解候选，最佳综合差值 {Candidates[0].targetDistance:F1}";
        }
    }
    public static class QuickLevelGenerator
    {
        static readonly Vector2Int[] Directions={Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};
        static bool FreeRay(Vector2Int p,Vector2Int d,HashSet<Vector2Int> occupied,int w,int h)
        {for(p+=d;p.x>=0 && p.y>=0 && p.x<w && p.y<h;p+=d)if(occupied.Contains(p))return false;return true;}
        static List<Vector2Int> Corners(List<Vector2Int> cells)
        {var result=new List<Vector2Int>{cells[0]};for(int i=1;i<cells.Count-1;i++)if(cells[i]-cells[i-1]!=cells[i+1]-cells[i])result.Add(cells[i]);result.Add(cells[cells.Count-1]);return result;}
        static Color Average(ArrowData a,GenerationTemplate template)
        {Color sum=Color.black;int n=0;foreach(var p in PuzzleModel.Cells(a)){sum+=template.ColorAt(p);n++;}var c=sum/Math.Max(1,n);c.a=1;return c;}
        static List<Color> Palette(GenerationTemplate template,GenerationSettings cfg)
        {
            var result=template.palette.Take(cfg.colorCount).ToList();
            if(result.Count==0)result.Add(ArrowBoardGraphic.Palette[2]);
            if(cfg.allowExtraColors)foreach(var color in ArrowBoardGraphic.Palette)if(result.Count<cfg.colorCount && result.All(c=>TemplateImageProcessor.Distance(c,color)>.12f))result.Add(color);
            if(cfg.brightenDarkColors)for(int i=0;i<result.Count;i++){Color.RGBToHSV(result[i],out float h,out float s,out float v);result[i]=Color.HSVToRGB(h,Mathf.Clamp(s,.18f,.8f),Mathf.Max(.55f,v));}
            return result;
        }
        static LevelData Geometry(GenerationTemplate t,GenerationSettings cfg,System.Random random)
        {
            var level=new LevelData{gridWidth=t.width,gridHeight=t.height,slotCapacity=cfg.slots,sourceImagePng=t.sourceImagePng};var points=new List<Vector2Int>();for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)if(t.Contains(x,y))points.Add(new Vector2Int(x,y));
            var occupied=new HashSet<Vector2Int>();int requested=Math.Min(60,Math.Max(3,cfg.arrowCount/3*3));
            int average=Mathf.Clamp(Mathf.RoundToInt(points.Count*cfg.coverage/requested),2,38);
            for(int arrow=0;arrow<requested;arrow++)
            {
                var free=points.Where(p=>!occupied.Contains(p)).ToList();if(free.Count<2)break;
                var heat=new Dictionary<Vector2Int,int>();
                foreach(var old in level.initialSegments){var d=PuzzleModel.Direction(old);for(var p=old.points[0]+d;p.x>=0 && p.y>=0 && p.x<t.width && p.y<t.height;p+=d){if(!heat.ContainsKey(p))heat[p]=0;heat[p]++;}}
                List<Vector2Int> best=null;float bestScore=float.MinValue;
                for(int trial=0;trial<Math.Min(100,free.Count*3);trial++)
                {
                    var head=free[random.Next(free.Count)];var direction=Directions[random.Next(4)];var behind=head-direction;
                    if(!t.Contains(behind.x,behind.y) || occupied.Contains(behind) || !FreeRay(head,direction,occupied,t.width,t.height))continue;
                    var path=new List<Vector2Int>{head,behind};var used=new HashSet<Vector2Int>(path);var forbidden=new HashSet<Vector2Int>();for(var p=head+direction;p.x>=0 && p.y>=0 && p.x<t.width && p.y<t.height;p+=direction)forbidden.Add(p);
                    int target=Math.Max(2,average+random.Next(-Math.Max(1,average/3),Math.Max(2,average/3)));
                    while(path.Count<target)
                    {
                        var tail=path[path.Count-1];var last=tail-path[path.Count-2];var possible=Directions.Select(d=>tail+d).Where(p=>t.Contains(p.x,p.y) && !occupied.Contains(p) && !used.Contains(p) && !forbidden.Contains(p)).ToList();if(possible.Count==0)break;
                        var baseColor=t.ColorAt(head);
                        var next=possible.OrderByDescending(p=>random.NextDouble()+((p-tail)==last?.7f:.05f)*(1-cfg.arrowTarget/130)+((heat.TryGetValue(p,out int value)?value:0)*cfg.arrowTarget/100f)*.55f-TemplateImageProcessor.Distance(t.ColorAt(p),baseColor)*cfg.colorFidelity*2).First();
                        used.Add(next);path.Add(next);
                    }
                    float score=path.Count*.12f+path.Sum(p=>heat.TryGetValue(p,out int n)?n:0)*cfg.arrowTarget/100f+random.NextDoubleAsFloat();
                    if(score>bestScore){best=path;bestScore=score;}
                    if(trial>=12 && best!=null && best.Count>=average)break;
                }
                if(best==null)break;occupied.UnionWith(best);level.initialSegments.Add(new ArrowData{points=Corners(best),colorDisplayIndex=1});
            }
            // 后加入的箭头先移出；裁去不足一组三枚的前缀，剩余依赖仍然无环。
            while(level.initialSegments.Count%3!=0)level.initialSegments.RemoveAt(level.initialSegments.Count-1);
            return level;
        }
        static float NextDoubleAsFloat(this System.Random random)=>(float)random.NextDouble();
        static int[] AssignImageColors(LevelData level,List<Color> means,System.Random random)
        {
            int n=level.initialSegments.Count,k=Math.Min(level.colorPalette.Count,n/3);var desired=new float[k];
            foreach(var color in means){int nearest=Enumerable.Range(0,k).OrderBy(c=>TemplateImageProcessor.Distance(color,level.colorPalette[c])).First();desired[nearest]++;}
            var quotas=Enumerable.Repeat(3,k).ToArray();for(int left=n-k*3;left>=3;left-=3){int next=Enumerable.Range(0,k).OrderByDescending(c=>desired[c]-quotas[c]).First();quotas[next]+=3;}
            var assignment=new int[n];foreach(int i in Enumerable.Range(0,n).OrderBy(_=>random.Next())){int color=Enumerable.Range(0,k).Where(c=>quotas[c]>0).OrderBy(c=>TemplateImageProcessor.Distance(means[i],level.colorPalette[c])).First();assignment[i]=color+1;quotas[color]--;}
            return assignment;
        }
        static void Apply(LevelData level,int[] colors){for(int i=0;i<colors.Length;i++)level.initialSegments[i].colorDisplayIndex=colors[i];}
        static float ColorError(LevelData level,List<Color> means)=>Enumerable.Range(0,means.Count).Average(i=>TemplateImageProcessor.Distance(means[i],level.colorPalette[level.initialSegments[i].colorDisplayIndex-1]));
        public static GeneratedCandidate Attempt(GenerationTemplate template,GenerationSettings cfg,int seed,out string failure)
        {
            if(cfg.fillImage)return DenseImageGenerator.Attempt(template,cfg,seed,out failure);
            failure="";
            if(template.Count<6){failure="轮廓有效格点不足六个，无法放下可三消的箭头。";return null;}
            var random=new System.Random(seed);var level=Geometry(template,cfg,random);
            if(level.initialSegments.Count<3){failure="轮廓太细碎，无法生成至少三枚不重叠箭头；请增大网格或补齐轮廓。";return null;}
            level.colorPalette=Palette(template,cfg);while(level.colorPalette.Count>level.initialSegments.Count/3)level.colorPalette.RemoveAt(level.colorPalette.Count-1);
            var means=level.initialSegments.Select(a=>Average(a,template)).ToList();var graph=new ArrowDependencyGraph(level);if(!graph.Solvable){failure="生成遮挡图异常，已丢弃。";return null;}
            int[] original=AssignImageColors(level,means,random);GeneratedCandidate best=null;
            for(int trial=0;trial<9;trial++)
            {
                var assignment=(int[])original.Clone();
                if(trial>0)for(int swaps=0;swaps<trial*2;swaps++){int a=random.Next(assignment.Length),b=random.Next(assignment.Length);int color=assignment[a];assignment[a]=assignment[b];assignment[b]=color;}
                Apply(level,assignment);bool exhausted;int nodes;var witness=DifficultyAnalyzer.SolveWithPeak(level,graph,level.slotCapacity-1,4500,out exhausted,out nodes);
                if(witness==null)continue;
                var analysis=DifficultyAnalyzer.Analyze(level,witness,Math.Max(8,cfg.probeRuns/2));
                float fit=ColorError(level,means);float target=(Mathf.Abs(analysis.arrowScore-cfg.arrowTarget)+Mathf.Abs(analysis.matchScore-cfg.matchTarget))*.5f;
                int occupied=level.initialSegments.Sum(a=>PuzzleModel.Cells(a).Count());float coverage=occupied/(float)template.Count;
                float rank=target+fit*cfg.colorFidelity*24+Mathf.Max(0,cfg.coverage-coverage)*20+Mathf.Abs(level.initialSegments.Count-cfg.arrowCount)*.25f;
                if(best==null || rank<best.targetDistance)
                {
                    best=new GeneratedCandidate{level=level.Copy(),difficulty=analysis,seed=seed,coverage=coverage,colorError=fit,targetDistance=rank,targetReached=Mathf.Abs(analysis.arrowScore-cfg.arrowTarget)<=cfg.tolerance && Mathf.Abs(analysis.matchScore-cfg.matchTarget)<=cfg.tolerance};
                }
            }
            if(best==null)
            {
                // 图片配色冲突时保留一条按三枚成组的可解保底，不伪称达到了目标难度。
                var assignment=new int[level.initialSegments.Count];int groups=assignment.Length/3;
                for(int g=0;g<groups;g++){int c=g%level.colorPalette.Count+1;for(int j=0;j<3;j++)assignment[graph.removalOrder[g*3+j]]=c;}
                Apply(level,assignment);var analysis=DifficultyAnalyzer.Analyze(level,graph.removalOrder,Math.Max(8,cfg.probeRuns/2));
                best=new GeneratedCandidate{level=level.Copy(),difficulty=analysis,seed=seed,coverage=level.initialSegments.Sum(a=>PuzzleModel.Cells(a).Count())/(float)template.Count,colorError=ColorError(level,means),warning="原图配色未在预算内找到解，已提供重新配色的可解候选。"};
                best.targetDistance=(Mathf.Abs(analysis.arrowScore-cfg.arrowTarget)+Mathf.Abs(analysis.matchScore-cfg.matchTarget))*.5f+best.colorError*cfg.colorFidelity*24;
                best.targetReached=Mathf.Abs(analysis.arrowScore-cfg.arrowTarget)<=cfg.tolerance && Mathf.Abs(analysis.matchScore-cfg.matchTarget)<=cfg.tolerance;
            }
            if(!best.difficulty.solutionVerified){failure="通关序列未能重放，已丢弃。";return null;}
            if(!best.targetReached)best.warning+="\n此候选未命中目标难度，请比较实测分数后决定是否采用。";
            if(best.level.initialSegments.Count!=cfg.arrowCount)best.warning+=$"\n轮廓只能容纳 {best.level.initialSegments.Count} 枚有效箭头（目标 {cfg.arrowCount}）。";
            if(best.coverage<.5f)best.warning+="\n轮廓覆盖率低于 50%，建议增大网格或减少箭头数以延长箭身。";
            if(!best.difficulty.slotPressurePossible && cfg.matchTarget>10)best.warning+="\n当前颜色数不足以产生槽满压力；可以允许补色或调整收集槽。";
            return best;
        }
    }
}
