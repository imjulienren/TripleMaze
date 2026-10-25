using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TripleMaze.Editor.Generation
{
    public sealed class ArrowDependencyGraph
    {
        public readonly List<int>[] blockers;
        public readonly List<int> removalOrder=new List<int>();
        public readonly int[] depth;
        public int Count=>blockers.Length;
        public bool Solvable=>removalOrder.Count==Count;
        public ArrowDependencyGraph(LevelData level)
        {
            int n=level.initialSegments.Count;blockers=new List<int>[n];depth=new int[n];var owners=new Dictionary<Vector2Int,int>();
            for(int i=0;i<n;i++)foreach(var p in PuzzleModel.Cells(level.initialSegments[i]))owners[p]=i;
            for(int i=0;i<n;i++)
            {
                var set=new HashSet<int>();var a=level.initialSegments[i];var d=PuzzleModel.Direction(a);
                for(var p=a.points[0]+d;p.x>=0 && p.y>=0 && p.x<level.gridWidth && p.y<level.gridHeight;p+=d)if(owners.TryGetValue(p,out int owner) && owner!=i)set.Add(owner);
                blockers[i]=set.OrderBy(x=>x).ToList();
            }
            var done=new HashSet<int>();
            while(done.Count<n)
            {
                bool changed=false;
                for(int i=0;i<n;i++)if(!done.Contains(i) && blockers[i].All(done.Contains)){depth[i]=1+(blockers[i].Count==0?0:blockers[i].Max(j=>depth[j]));done.Add(i);removalOrder.Add(i);changed=true;}
                if(!changed)break;
            }
        }
        public List<int> Available(bool[] removed)
        {var result=new List<int>();for(int i=0;i<Count;i++)if(!removed[i] && blockers[i].All(j=>removed[j]))result.Add(i);return result;}
    }
    public static class DifficultyAnalyzer
    {
        public static List<int> SolveWithPeak(LevelData level,ArrowDependencyGraph graph,int limit,int budget,out bool exhausted,out int nodes)
        {
            exhausted=false;nodes=0;if(!graph.Solvable)return null;
            if(graph.Count>63)return SolveLarge(level,graph,limit,budget,out exhausted,out nodes);
            int n=graph.Count;var masks=new ulong[n];for(int i=0;i<n;i++)foreach(int b in graph.blockers[i])masks[i]|=1UL<<b;
            ulong all=(1UL<<n)-1;var counts=new int[6];var path=new List<int>();var seen=new HashSet<(ulong,int)>();bool hitLimit=false;int visited=0;
            bool Search(ulong removed,int occupied)
            {
                if(removed==all)return occupied==0;
                if(++visited>budget){hitLimit=true;return false;}
                int code=0;for(int c=0;c<6;c++)code=code*3+counts[c];if(!seen.Add((removed,code)))return false;
                var candidates=new List<int>();for(int i=0;i<n;i++)if((removed&(1UL<<i))==0 && (masks[i]&~removed)==0)candidates.Add(i);
                candidates.Sort((a,b)=>counts[level.initialSegments[b].colorDisplayIndex-1].CompareTo(counts[level.initialSegments[a].colorDisplayIndex-1]));
                foreach(int i in candidates)
                {
                    int c=level.initialSegments[i].colorDisplayIndex-1,old=counts[c],next=(old+1)%3,load=occupied+next-old;if(load>limit)continue;
                    counts[c]=next;path.Add(i);if(Search(removed|(1UL<<i),load))return true;path.RemoveAt(path.Count-1);counts[c]=old;if(hitLimit)return false;
                }
                return false;
            }
            bool success=Search(0,0);nodes=visited;exhausted=hitLimit;return success?new List<int>(path):null;
        }
        static List<int> SolveLarge(LevelData level,ArrowDependencyGraph graph,int limit,int budget,out bool exhausted,out int nodes)
        {
            var removed=new bool[graph.Count];var counts=new int[6];var path=new List<int>();var seen=new HashSet<string>();int visited=0;bool hit=false;
            bool Search(int load)
            {
                if(path.Count==graph.Count)return load==0;
                if(++visited>budget){hit=true;return false;}
                var words=new char[(graph.Count+15)/16];for(int i=0;i<removed.Length;i++)if(removed[i])words[i/16]=(char)(words[i/16]|(1<<(i%16)));
                int code=0;foreach(int c in counts)code=code*3+c;if(!seen.Add(new string(words)+":"+code))return false;
                foreach(int i in graph.Available(removed).OrderByDescending(i=>counts[level.initialSegments[i].colorDisplayIndex-1]))
                {
                    int c=level.initialSegments[i].colorDisplayIndex-1,old=counts[c],next=(old+1)%3,newLoad=load+next-old;if(newLoad>limit)continue;
                    counts[c]=next;removed[i]=true;path.Add(i);if(Search(newLoad))return true;
                    path.RemoveAt(path.Count-1);removed[i]=false;counts[c]=old;if(hit)return false;
                }
                return false;
            }
            bool solved=Search(0);exhausted=hit;nodes=visited;return solved?new List<int>(path):null;
        }
        static float Probe(LevelData level,ArrowDependencyGraph graph,int runs,bool greedy)
        {
            var random=new System.Random(1739);int failed=0;
            for(int run=0;run<runs;run++)
            {
                var removed=new bool[graph.Count];var counts=new int[6];int occupied=0,completed=0;
                while(completed<graph.Count)
                {
                    var possible=graph.Available(removed);if(possible.Count==0){failed++;break;}
                    int selected=possible[random.Next(possible.Count)];
                    if(greedy){int best=possible.Max(i=>counts[level.initialSegments[i].colorDisplayIndex-1]);var preferred=possible.Where(i=>counts[level.initialSegments[i].colorDisplayIndex-1]==best).ToList();selected=preferred[random.Next(preferred.Count)];}
                    int c=level.initialSegments[selected].colorDisplayIndex-1,old=counts[c];counts[c]=(old+1)%3;occupied+=counts[c]-old;removed[selected]=true;completed++;
                    if(occupied>=level.slotCapacity){failed++;break;}
                }
            }
            return failed/(float)Math.Max(1,runs);
        }
        public static DifficultyReport Analyze(LevelData level,List<int> witness=null,int trials=32,bool probeMinimum=false)
        {
            var result=new DifficultyReport();var errors=PuzzleModel.Validate(level);if(errors.Count>0){result.note=string.Join("\n",errors);return result;}
            var graph=new ArrowDependencyGraph(level);result.arrows=graph.Count;result.colors=level.initialSegments.Select(a=>a.colorDisplayIndex).Distinct().Count();result.structurallySolvable=graph.Solvable;
            result.initialExits=graph.blockers.Count(b=>b.Count==0);result.meanBlockers=(float)graph.blockers.Average(b=>b.Count);result.meanTurns=(float)level.initialSegments.Average(a=>Math.Max(0,a.points.Count-2));
            if(!graph.Solvable){result.note="存在无法解除的遮挡环，不能作为可玩难度。";return result;}
            result.meanDepth=(float)graph.depth.Average();result.maxDepth=graph.depth.Max();
            if(witness==null){bool exhausted;int nodes;witness=SolveWithPeak(level,graph,level.slotCapacity-1,18000,out exhausted,out nodes);result.searchExhausted=exhausted;result.searchNodes=nodes;}
            if(witness!=null)
            {
                var model=new PuzzleModel(level);float choices=0;bool valid=true;
                foreach(int i in witness){choices+=graph.Available(Enumerable.Range(0,graph.Count).Select(j=>model.Removed.Contains(j)).ToArray()).Count;if(!model.CanExit(i)){valid=false;break;}model.Collect(i);result.knownPeak=Math.Max(result.knownPeak,model.Tray.Count);if(model.State==RoundState.Lost){valid=false;break;}}
                result.solutionVerified=valid && model.State==RoundState.Won;if(result.solutionVerified){result.solution=new List<int>(witness);result.meanChoices=choices/graph.Count;}
            }
            result.slotPressurePossible=2*result.colors>=level.slotCapacity;result.randomTrials=trials;
            result.randomFailureRate=Probe(level,graph,trials,false);result.greedyFailureRate=Probe(level,graph,trials,true);
            result.arrowScore=100*(.25f*(1-result.initialExits/(float)graph.Count)+.30f*Mathf.Clamp01((result.meanDepth-1)/5)+.20f*Mathf.Clamp01((result.maxDepth-1)/9f)+.15f*Mathf.Clamp01(result.meanBlockers/3)+.10f*Mathf.Clamp01(result.meanTurns/3));
            if(level.imageFilled)result.arrowScore=100*(.30f*(1-result.initialExits/(float)graph.Count)+.35f*Mathf.Clamp01((result.meanDepth-1)/8)+.20f*Mathf.Clamp01((result.maxDepth-1)/18f)+.15f*Mathf.Clamp01(result.meanBlockers/4));
            result.matchScore=result.slotPressurePossible?100*(.15f*Mathf.Clamp01((2f*result.colors-level.slotCapacity+1)/5)+.45f*result.randomFailureRate+.30f*result.greedyFailureRate+.10f*Mathf.Clamp01(result.knownPeak/(float)Math.Max(1,level.slotCapacity-1))):0;
            result.overallScore=level.imageFilled?result.arrowScore:.45f*result.arrowScore+.55f*result.matchScore;
            if(probeMinimum)
            {
                bool exact=true;
                for(int peak=2;peak<level.slotCapacity;peak++)
                {bool limit;int nodes;var path=SolveWithPeak(level,graph,peak,12000,out limit,out nodes);result.searchNodes+=nodes;if(path!=null){result.minimumPeak=peak;result.minimumPeakExact=exact;break;}if(limit)exact=false;}
            }
            result.note=!result.slotPressurePossible?$"只有 {result.colors} 色、{level.slotCapacity} 槽，自动三消后不可能槽满；三消压力记为 0。":result.solutionVerified?"已重放验证一条完整通关序列。评分是启发式设计指标，策略失败率来自采样，不等同于真人胜率。":result.searchExhausted?"搜索预算耗尽，尚不能确认有解。":"当前颜色与遮挡组合未找到可行解。";
            return result;
        }
    }
}
