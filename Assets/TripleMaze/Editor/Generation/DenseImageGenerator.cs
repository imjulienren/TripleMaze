using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    // Geometry follows the image. Difficulty changes orientation, not an arrow-count budget.
    public static class DenseImageGenerator
    {
        static readonly Vector2Int[] Directions={Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};
        static List<Vector2Int> Compress(List<Vector2Int> cells)
        {
            var result=new List<Vector2Int>{cells[0]};
            for(int i=1;i<cells.Count-1;i++)if(cells[i]-cells[i-1]!=cells[i+1]-cells[i])result.Add(cells[i]);
            result.Add(cells[cells.Count-1]);return result;
        }
        static bool ClearRay(Vector2Int head,Vector2Int direction,HashSet<Vector2Int> remaining,int w,int h)
        {for(var p=head+direction;p.x>=0 && p.y>=0 && p.x<w && p.y<h;p+=direction)if(remaining.Contains(p))return false;return true;}
        static int Degree(Vector2Int p,HashSet<Vector2Int> remaining)
        {int n=0;foreach(var d in Directions)if(remaining.Contains(p+d))n++;return n;}
        static List<Color> ImagePalette(GenerationTemplate t,int limit)
        {
            // Cluster foreground only; transparent padding must not erase white image regions.
            var bins=new Dictionary<int,List<Color>>();
            for(int i=0;i<t.mask.Count;i++)if(t.mask[i])
            {
                Color c=t.samples[i];int key=(Mathf.RoundToInt(c.r*31)<<10)|(Mathf.RoundToInt(c.g*31)<<5)|Mathf.RoundToInt(c.b*31);
                if(!bins.ContainsKey(key))bins[key]=new List<Color>();bins[key].Add(c);
            }
            var colors=bins.Values.Select(v=>new {color=v.Aggregate(Color.clear,(sum,c)=>sum+c)/v.Count,weight=v.Count}).OrderByDescending(v=>v.weight).ToList();
            var centers=new List<Color>{colors[0].color};
            while(centers.Count<Mathf.Clamp(limit,1,6))
            {
                var next=colors.OrderByDescending(v=>v.weight*Mathf.Pow(centers.Min(c=>TemplateImageProcessor.Distance(c,v.color)),2)).First();
                if(centers.Min(c=>TemplateImageProcessor.Distance(c,next.color))<.10f)break;
                centers.Add(next.color);
            }
            for(int pass=0;pass<10;pass++)
            {
                var sums=new Color[centers.Count];var weights=new int[centers.Count];
                foreach(var item in colors){int c=Enumerable.Range(0,centers.Count).OrderBy(k=>TemplateImageProcessor.Distance(item.color,centers[k])).First();sums[c]+=item.color*item.weight;weights[c]+=item.weight;}
                for(int c=0;c<centers.Count;c++)if(weights[c]>0){centers[c]=sums[c]/weights[c];var value=centers[c];value.a=1;centers[c]=value;}
            }
            return centers;
        }
        static LevelData Fill(GenerationTemplate t,GenerationSettings cfg,System.Random random)
        {
            var level=new LevelData{gridWidth=t.width,gridHeight=t.height,slotCapacity=cfg.slots,imageFilled=true};
            var remaining=new HashSet<Vector2Int>();for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)if(t.Contains(x,y))remaining.Add(new Vector2Int(x,y));
            int length=Mathf.Clamp(cfg.imageArrowLength,3,12);
            while(remaining.Count>=2)
            {
                List<Vector2Int> best=null;float bestScore=float.MinValue;
                foreach(var head in remaining.OrderBy(p=>p.y*t.width+p.x))foreach(var direction in Directions)
                {
                    var behind=head-direction;
                    if(!remaining.Contains(behind) || !ClearRay(head,direction,remaining,t.width,t.height))continue;
                    var path=new List<Vector2Int>{head,behind};var used=new HashSet<Vector2Int>(path);var reference=t.ColorAt(head);
                    int goal=length+random.Next(0,3);
                    while(path.Count<goal)
                    {
                        var tail=path[path.Count-1];var last=tail-path[path.Count-2];Vector2Int next=default;float score=float.MinValue;bool found=false;
                        foreach(var d in Directions)
                        {
                            var p=tail+d;if(!remaining.Contains(p) || used.Contains(p))continue;
                            if((p.x==head.x && direction.y!=0 && Math.Sign(p.y-head.y)==direction.y) || (p.y==head.y && direction.x!=0 && Math.Sign(p.x-head.x)==direction.x))continue;
                            float difference=TemplateImageProcessor.Distance(reference,t.ColorAt(p));
                            if(difference>.16f && path.Count>=2 && Degree(p,remaining)>1)continue;
                            float rank=(d==last?1.7f:0)-difference*12+(Degree(p,remaining)==1?4:0)+(float)random.NextDouble()*.4f;
                            if(rank>score){score=rank;next=p;found=true;}
                        }
                        if(!found)break;path.Add(next);used.Add(next);
                    }
                    // Finish narrow tips before removing their only adjacent cell.
                    int leaves=path.Count(p=>Degree(p,remaining)==1);
                    float mismatch=path.Average(p=>TemplateImageProcessor.Distance(reference,t.ColorAt(p)));
                    float value=leaves*4+path.Count*.6f-mismatch*16+(float)random.NextDouble()*1.4f;
                    if(value>bestScore){best=path;bestScore=value;}
                }
                if(best==null)break;
                remaining.ExceptWith(best);level.initialSegments.Add(new ArrowData{points=Compress(best)});
            }
            // Absorb leftover tips by extending adjacent endpoints. Never discard a filled arrow.
            bool changed=true;
            while(changed && remaining.Count>0)
            {
                changed=false;
                foreach(var p in remaining.OrderBy(p=>p.y*t.width+p.x).ToArray())
                {
                    foreach(var arrow in level.initialSegments.OrderBy(a=>TemplateImageProcessor.Distance(t.ColorAt(p),t.ColorAt(a.points[a.points.Count-1]))))
                    {
                        var old=arrow.points;var raw=PuzzleModel.Cells(arrow).ToList();List<Vector2Int> extended=null;
                        if((p-raw[raw.Count-1]).sqrMagnitude==1){extended=new List<Vector2Int>(raw);extended.Add(p);}
                        else if(p==raw[0]+PuzzleModel.Direction(arrow)){extended=new List<Vector2Int>{p};extended.AddRange(raw);}
                        if(extended==null)continue;arrow.points=Compress(extended);
                        if(new ArrowDependencyGraph(level).Solvable){remaining.Remove(p);changed=true;break;}
                        arrow.points=old;
                    }
                }
            }
            // Split straight pieces to satisfy triples without punching holes in the stencil.
            for(int extra=(3-level.initialSegments.Count%3)%3;extra>0;extra--)
            {
                bool split=false;
                for(int i=0;i<level.initialSegments.Count;i++)
                {
                    var a=level.initialSegments[i];var raw=PuzzleModel.Cells(a).ToList();if(raw.Count<4)continue;
                    var old=a.points;
                    for(int cut=2;cut<=raw.Count-2;cut++)
                    {
                        a.points=Compress(raw.Take(cut).ToList());var b=new ArrowData{points=Compress(raw.Skip(cut).ToList())};level.initialSegments.Add(b);
                        if(new ArrowDependencyGraph(level).Solvable){split=true;break;}
                        level.initialSegments.RemoveAt(level.initialSegments.Count-1);a.points=old;
                    }
                    if(split)break;
                }
                if(!split)throw new InvalidOperationException("当前轮廓无法在保留填充的前提下配平三消，请换一个种子。");
            }
            return level;
        }
        static float ArrowScore(ArrowDependencyGraph graph,LevelData level)
        {
            float exits=graph.blockers.Count(x=>x.Count==0)/(float)graph.Count;
            return 100*(.30f*(1-exits)+.35f*Mathf.Clamp01(((float)graph.depth.Average()-1)/8)+.20f*Mathf.Clamp01((graph.depth.Max()-1)/18f)+.15f*Mathf.Clamp01((float)graph.blockers.Average(x=>x.Count)/4));
        }
        static ArrowDependencyGraph Orient(LevelData level,GenerationSettings settings,System.Random random)
        {
            var graph=new ArrowDependencyGraph(level);float distance=Mathf.Abs(ArrowScore(graph,level)-settings.arrowTarget);
            // Same filled paths and same number of arrows: change heads to tune dependency layers.
            for(int pass=0;pass<3;pass++)foreach(int i in Enumerable.Range(0,graph.Count).OrderBy(_=>random.Next()))
            {
                level.initialSegments[i].points.Reverse();var candidate=new ArrowDependencyGraph(level);
                float next=candidate.Solvable?Mathf.Abs(ArrowScore(candidate,level)-settings.arrowTarget):float.MaxValue;
                if(next<distance){distance=next;graph=candidate;}else level.initialSegments[i].points.Reverse();
            }
            return graph;
        }
        static List<int> ColorForSolution(LevelData level,GenerationTemplate t,ArrowDependencyGraph graph,out float error)
        {
            int n=graph.Count,k=level.colorPalette.Count,states=1;for(int c=0;c<k;c++)states*=3;
            var powers=new int[k];int power=1;for(int c=0;c<k;c++){powers[c]=power;power*=3;}
            var loads=new int[states];var completion=new int[states];for(int s=0;s<states;s++){int v=s;for(int c=0;c<k;c++){int count=v%3;loads[s]+=count;completion[s]+=(3-count)%3;v/=3;}}
            var previous=new int[n+1,states];var choices=new byte[n+1,states];
            var costs=Enumerable.Repeat(float.PositiveInfinity,states).ToArray();costs[0]=0;
            for(int step=0;step<n;step++)
            {
                int index=graph.removalOrder[step];var cells=PuzzleModel.Cells(level.initialSegments[index]).ToList();
                var errors=new float[k];for(int c=0;c<k;c++)errors[c]=cells.Average(p=>Mathf.Pow(TemplateImageProcessor.Distance(t.ColorAt(p),level.colorPalette[c]),2));
                var next=Enumerable.Repeat(float.PositiveInfinity,states).ToArray();
                for(int s=0;s<states;s++)if(!float.IsPositiveInfinity(costs[s]))for(int c=0;c<k;c++)
                {
                    int count=s/powers[c]%3,newState=s+(count==2?-2:1)*powers[c];
                    if(loads[newState]>=level.slotCapacity || completion[newState]>n-step-1)continue;
                    float score=costs[s]+errors[c]*cells.Count;
                    if(score<next[newState]){next[newState]=score;previous[step+1,newState]=s;choices[step+1,newState]=(byte)c;}
                }
                costs=next;
            }
            if(float.IsPositiveInfinity(costs[0]))throw new InvalidOperationException("无法在当前槽位容量内配平图片颜色。");
            int state=0;for(int step=n;step>0;step--){int c=choices[step,state];level.initialSegments[graph.removalOrder[step-1]].colorDisplayIndex=c+1;state=previous[step,state];}
            error=Mathf.Sqrt(costs[0]/Math.Max(1,level.initialSegments.Sum(a=>PuzzleModel.Cells(a).Count())));
            return new List<int>(graph.removalOrder);
        }
        public static GeneratedCandidate Attempt(GenerationTemplate template,GenerationSettings settings,int seed,out string failure)
        {
            failure="";
            try
            {
                if(template.Count<6)throw new InvalidOperationException("至少需要六个有效格点。");
                var random=new System.Random(seed);var level=Fill(template,settings,random);
                if(level.initialSegments.Count<3)throw new InvalidOperationException("轮廓太小，无法组成三枚箭头。");
                var graph=Orient(level,settings,random);level.colorPalette=ImagePalette(template,settings.colorCount);
                float error;var solution=ColorForSolution(level,template,graph,out error);
                var errors=PuzzleModel.Validate(level);if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
                var report=DifficultyAnalyzer.Analyze(level,solution,8);report.arrowScore=ArrowScore(graph,level);report.overallScore=report.arrowScore;
                if(!report.solutionVerified)throw new InvalidOperationException("填充关卡未通过实际规则重放。");
                float coverage=level.initialSegments.Sum(a=>PuzzleModel.Cells(a).Count())/(float)template.Count;
                var candidate=new GeneratedCandidate{level=level,difficulty=report,coverage=coverage,colorError=error,seed=seed,targetDistance=Mathf.Abs(report.arrowScore-settings.arrowTarget)+error*45+(1-coverage)*200,targetReached=Mathf.Abs(report.arrowScore-settings.arrowTarget)<=settings.tolerance};
                if(coverage<.98f)candidate.warning=$"剩余 {template.Count-level.initialSegments.Sum(a=>PuzzleModel.Cells(a).Count())} 个孤立或无法连接的格点；没有为配平三消删除已填区域。";
                if(!candidate.targetReached)candidate.warning+="\n遮挡难度未进入目标容差，图片密度保持不变。";
                return candidate;
            }
            catch(Exception ex){failure=ex.Message;return null;}
        }
    }
}
