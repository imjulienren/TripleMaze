using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TripleMaze
{
    [Serializable] public class ArrowData
    {
        public int colorDisplayIndex = 1;
        public List<Vector2Int> points = new List<Vector2Int>();
    }
    [Serializable] public class LevelData
    {
        public int levelId = 1;
        public string description = "";
        public int gridWidth = 10, gridHeight = 12, slotCapacity = 7;
        public bool imageFilled;
        // PNG travels with the level and its recipe, including exported JSON levels.
        public string sourceImagePng = "";
        public List<ArrowData> initialSegments = new List<ArrowData>();
        public List<Color> colorPalette = new List<Color>();
        public LevelData Copy() => JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(this));
    }
    public enum RoundState { Playing, Won, Lost }
    public enum MoveResult { Blocked, Collected, Matched, Won, Lost, Rejected }
    public sealed class PuzzleModel
    {
        public readonly LevelData Level;
        public readonly HashSet<int> Removed = new HashSet<int>();
        public readonly List<int> Tray = new List<int>();
        public RoundState State { get; private set; }
        public int Moves { get; private set; }
        public int Matches { get; private set; }
        public int Health { get; private set; }
        public int Remaining => Level.initialSegments.Count - Removed.Count;
        public PuzzleModel(LevelData level,int health=3) { Level = level.Copy(); Health=Math.Max(0,Math.Min(3,health)); }
        public bool HitObstacle()
        {
            if(State!=RoundState.Playing || Health<=0)return false;
            Health--;if(Health==0)State=RoundState.Lost;return true;
        }
        public static Color DisplayColor(LevelData level,int index)
        {return level!=null && level.colorPalette!=null && index>0 && index<=level.colorPalette.Count?level.colorPalette[index-1]:ArrowBoardGraphic.Palette[Mathf.Clamp(index-1,0,5)];}
        public static Vector2Int Direction(ArrowData a)
        {
            var v = a.points[0] - a.points[1];
            return new Vector2Int(Math.Sign(v.x), Math.Sign(v.y));
        }
        public static IEnumerable<Vector2Int> Cells(ArrowData a)
        {
            for (int i=0;i<a.points.Count-1;i++)
            {
                var p=a.points[i]; var delta=a.points[i+1]-p;
                int n=Math.Max(Math.Abs(delta.x),Math.Abs(delta.y));
                var step=new Vector2Int(Math.Sign(delta.x),Math.Sign(delta.y));
                for(int s=0;s<n;s++) yield return p+step*s;
            }
            yield return a.points[a.points.Count-1];
        }
        public bool CanExit(int index)
        {
            if(index<0 || index>=Level.initialSegments.Count || Removed.Contains(index)) return false;
            var a=Level.initialSegments[index]; var dir=Direction(a);
            var occupied=new HashSet<Vector2Int>();
            for(int i=0;i<Level.initialSegments.Count;i++)
                if(i!=index && !Removed.Contains(i)) occupied.UnionWith(Cells(Level.initialSegments[i]));
            for(var p=a.points[0]+dir;p.x>=0 && p.y>=0 && p.x<Level.gridWidth && p.y<Level.gridHeight;p+=dir)
                if(occupied.Contains(p)) return false;
            return true;
        }
        public MoveResult Collect(int index)
        {
            if(State!=RoundState.Playing) return MoveResult.Rejected;
            if(!CanExit(index)) return MoveResult.Blocked;
            int color=Level.initialSegments[index].colorDisplayIndex;
            Removed.Add(index); Moves++;
            int insert=Tray.LastIndexOf(color); Tray.Insert(insert<0?Tray.Count:insert+1,color);
            bool match=Tray.Count(c=>c==color)>=3;
            if(match) { for(int n=0;n<3;n++) Tray.Remove(color); Matches++; }
            // 先结算三消，再判断槽满；最后一组消除后才能胜利。
            if(Tray.Count>=Level.slotCapacity) { State=RoundState.Lost; return MoveResult.Lost; }
            if(Remaining==0) { State=Tray.Count==0?RoundState.Won:RoundState.Lost; return State==RoundState.Won?MoveResult.Won:MoveResult.Lost; }
            return match?MoveResult.Matched:MoveResult.Collected;
        }
        public List<int> Available() => Enumerable.Range(0,Level.initialSegments.Count).Where(CanExit).ToList();
        public static List<string> Validate(LevelData d)
        {
            var errors=new List<string>(); var occupied=new HashSet<Vector2Int>();
            if(d==null || d.initialSegments==null) { errors.Add("缺少关卡数据"); return errors; }
            if(d.gridWidth<2 || d.gridHeight<2 || d.gridWidth>80 || d.gridHeight>80) errors.Add("地图尺寸应为 2 至 80");
            if(d.slotCapacity<3 || d.slotCapacity>12) errors.Add("槽位容量应为 3 至 12");
            if(d.initialSegments.Count==0) errors.Add("没有箭头");
            var colors=new Dictionary<int,int>();
            for(int i=0;i<d.initialSegments.Count;i++)
            {
                var a=d.initialSegments[i];
                if(a==null || a.points==null || a.points.Count<2) {errors.Add($"箭头 {i+1} 至少需要两个点");continue;}
                if(a.colorDisplayIndex<1 || a.colorDisplayIndex>6) errors.Add($"箭头 {i+1} 颜色应为 1 至 6");
                if(!colors.ContainsKey(a.colorDisplayIndex)) colors[a.colorDisplayIndex]=0;
                colors[a.colorDisplayIndex]++;
                bool valid=true;
                for(int j=0;j<a.points.Count-1;j++)
                {
                    var v=a.points[j+1]-a.points[j];
                    if(v==Vector2Int.zero || (v.x!=0 && v.y!=0)) {errors.Add($"箭头 {i+1} 必须是非零正交折线");valid=false;break;}
                }
                if(!valid) continue;
                foreach(var p in Cells(a))
                {
                    if(p.x<0 || p.y<0 || p.x>=d.gridWidth || p.y>=d.gridHeight) errors.Add($"箭头 {i+1} 越界");
                    if(!occupied.Add(p)) errors.Add($"箭头 {i+1} 重叠或自交");
                }
            }
            foreach(var c in colors) if(c.Value%3!=0) errors.Add($"颜色 {c.Key} 数量 {c.Value} 不是 3 的倍数");
            return errors.Distinct().ToList();
        }
        public static List<int> Solve(LevelData level, int budget, out bool exhausted)
        {
            exhausted=false;
            if(Validate(level).Count>0) return null;
            return Solve(new PuzzleModel(level),budget,out exhausted);
        }
        public static List<int> Solve(PuzzleModel current,int budget,out bool exhausted)
        {
            exhausted=false;
            if(current==null || current.State==RoundState.Lost || Validate(current.Level).Count>0)return null;
            var level=current.Level;
            var model=new PuzzleModel(level,current.Health);
            model.Removed.UnionWith(current.Removed);model.Tray.AddRange(current.Tray);
            var path=new List<int>(); var seen=new HashSet<string>();
            int nodes=0; bool limit=false;
            bool Search()
            {
                if(model.Remaining==0) return model.Tray.Count==0;
                if(++nodes>budget) { limit=true; return false; }
                var key=string.Join(",",model.Removed.OrderBy(x=>x))+"/"+string.Join(",",model.Tray.OrderBy(x=>x));
                if(!seen.Add(key)) return false;
                foreach(int i in model.Available().OrderByDescending(i=>model.Tray.Count(c=>c==level.initialSegments[i].colorDisplayIndex)))
                {
                    var before=model.Tray.ToArray(); var result=model.Collect(i);path.Add(i);
                    if(result==MoveResult.Won || (model.State==RoundState.Playing && Search())) return true;
                    path.RemoveAt(path.Count-1);model.Removed.Remove(i);model.Tray.Clear();model.Tray.AddRange(before);model.State=RoundState.Playing;
                    if(limit) return false;
                }
                return false;
            }
            bool solved=Search();exhausted=limit;return solved?path:null;
        }
    }
}
