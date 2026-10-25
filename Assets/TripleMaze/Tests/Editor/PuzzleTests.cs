using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TripleMaze.Tests
{
    public class PuzzleTests
    {
        ArrowData Arrow(int x,int y,int color=1)=>new ArrowData{colorDisplayIndex=color,points=new List<Vector2Int>{new Vector2Int(x,y),new Vector2Int(x,y+1)}};
        LevelData Independent(params int[] colors)
        {var d=new LevelData{gridWidth=20,gridHeight=8};for(int i=0;i<colors.Length;i++)d.initialSegments.Add(Arrow(i*2,1,colors[i]));return d;}
        [TestCase(2,"Good")][TestCase(4,"Great")][TestCase(6,"Perfect")][TestCase(9,"Excellent")][TestCase(12,"Excellent")][TestCase(15,"Excellent")][TestCase(18,"Excellent")][TestCase(10,null)][TestCase(11,null)][TestCase(3,null)]
        public void StreakFeedbackMatchesRequestedMilestones(int groups,string expected){Assert.AreEqual(expected,StreakWords.At(groups));}
        [Test] public void SparseCornersExpandEveryCell()
        {var a=new ArrowData{points=new List<Vector2Int>{new Vector2Int(0,0),new Vector2Int(0,3),new Vector2Int(2,3)}};Assert.AreEqual(6,PuzzleModel.Cells(a).Count());}
        [Test] public void HeadUsesFirstPoint(){Assert.AreEqual(new Vector2Int(0,-1),PuzzleModel.Direction(Arrow(1,1)));}
        [Test] public void BlockedTapDoesNotConsumeArrowOrTray()
        {var d=Independent(1,1,1);d.initialSegments[1].points=new List<Vector2Int>{new Vector2Int(1,0),new Vector2Int(0,0)};var m=new PuzzleModel(d);Assert.AreEqual(MoveResult.Blocked,m.Collect(0));Assert.AreEqual(0,m.Moves);Assert.IsEmpty(m.Tray);}
        [Test] public void RemovalUnblocksHeadRay()
        {var d=Independent(1,1,1);d.initialSegments[1].points=new List<Vector2Int>{new Vector2Int(1,0),new Vector2Int(0,0)};var m=new PuzzleModel(d);Assert.IsFalse(m.CanExit(0));m.Collect(1);Assert.IsTrue(m.CanExit(0));}
        [Test] public void ThreeSameColorsClearEvenWhenNonConsecutive()
        {var m=new PuzzleModel(Independent(1,2,1,2,1,2));m.Collect(0);m.Collect(1);m.Collect(2);Assert.AreEqual(MoveResult.Matched,m.Collect(4));CollectionAssert.AreEqual(new[]{2},m.Tray);}
        [Test] public void SeventhArrowMatchesBeforeCapacityCheck()
        {var m=new PuzzleModel(Independent(1,1,2,2,3,3,1,2,3));for(int i=0;i<6;i++)m.Collect(i);Assert.AreEqual(MoveResult.Matched,m.Collect(6));Assert.AreEqual(4,m.Tray.Count);Assert.AreEqual(RoundState.Playing,m.State);}
        [Test] public void FullTrayLosesAndRejectsFurtherInput()
        {var m=new PuzzleModel(Independent(1,1,2,2,3,3,4,4,4));for(int i=0;i<7;i++)m.Collect(i);Assert.AreEqual(RoundState.Lost,m.State);Assert.AreEqual(MoveResult.Rejected,m.Collect(7));}
        [Test] public void WinningRequiresEmptyBoardAndTray()
        {var m=new PuzzleModel(Independent(1,1,1));m.Collect(0);m.Collect(1);Assert.AreEqual(MoveResult.Won,m.Collect(2));Assert.IsEmpty(m.Tray);}
        [Test] public void ValidationRejectsOverlapDiagonalAndUnevenColors()
        {var d=Independent(1,1,2);d.initialSegments[1].points=d.initialSegments[0].points;d.initialSegments[2].points=new List<Vector2Int>{Vector2Int.zero,Vector2Int.one};Assert.GreaterOrEqual(PuzzleModel.Validate(d).Count,3);}
        [Test] public void JsonRoundTripPreservesGridPoints()
        {var d=Independent(1,1,1);var copy=d.Copy();Assert.AreEqual(d.initialSegments[2].points[0],copy.initialSegments[2].points[0]);}
        [Test] public void SolverReturnsReplayableSolution()
        {var d=Independent(1,2,1,2,1,2);bool exhausted;var solution=PuzzleModel.Solve(d,1000,out exhausted);Assert.IsNotNull(solution);var m=new PuzzleModel(d);foreach(var i in solution)m.Collect(i);Assert.AreEqual(RoundState.Won,m.State);}
        [System.Serializable] class Source{public int level;public int[] knownSolution;}
        [System.Serializable] class Sources{public Source[] items;}
        [Test] public void AllThirtyImportedLevelsValidateAndKnownSolutionWins()
        {
            var json=System.IO.File.ReadAllText(Application.dataPath+"/../Docs/source-levels.json");var sources=JsonUtility.FromJson<Sources>("{\"items\":"+json+"}");Assert.AreEqual(30,sources.items.Length);
            foreach(var item in sources.items)
            {
                var asset=Resources.Load<TextAsset>($"Levels/level_{item.level:00}");Assert.IsNotNull(asset);
                var d=JsonUtility.FromJson<LevelData>(asset.text);CollectionAssert.IsEmpty(PuzzleModel.Validate(d),"Level "+item.level);
                var model=new PuzzleModel(d);foreach(var i in item.knownSolution){Assert.IsTrue(model.CanExit(i),$"Level {item.level}, arrow {i}");model.Collect(i);}
                Assert.AreEqual(RoundState.Won,model.State,"Level "+item.level);
            }
        }
    }
}
