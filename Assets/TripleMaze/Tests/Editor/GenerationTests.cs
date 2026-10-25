using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TripleMaze.Editor.Generation;

namespace TripleMaze.Tests
{
    public class GenerationTests
    {
        LevelData Free(params int[] colors)
        {var d=new LevelData{gridWidth=30,gridHeight=10};for(int i=0;i<colors.Length;i++)d.initialSegments.Add(new ArrowData{colorDisplayIndex=colors[i],points=new List<Vector2Int>{new Vector2Int(i*2,2),new Vector2Int(i*2,4)}});return d;}
        [Test] public void TransparentImagePreservesAspectColorAndTopOrigin()
        {
            var image=new Texture2D(12,8,TextureFormat.RGBA32,false);
            try
            {
                for(int y=0;y<8;y++)for(int x=0;x<12;x++)image.SetPixel(x,y,x>=2 && x<10 && y>=1 && y<7?(y>=4?Color.red:Color.blue):Color.clear);image.Apply();
                var t=TemplateImageProcessor.FromImage(image,12,ImageBackground.AutoBorder,.1f,6,false);
                Assert.AreEqual(12,t.width);Assert.AreEqual(8,t.height);Assert.IsFalse(t.Contains(0,0));Assert.IsTrue(t.Contains(3,2));Assert.Greater(t.samples[2*12+3].r,.8f);Assert.Greater(t.samples[5*12+3].b,.8f);Assert.GreaterOrEqual(t.palette.Count,2);
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        [Test] public void WhiteAndDarkBackgroundModesKeepForeground()
        {
            var image=new Texture2D(10,10,TextureFormat.RGBA32,false);
            try
            {
                foreach(var background in new[]{Color.white,Color.black})
                {
                    for(int y=0;y<10;y++)for(int x=0;x<10;x++)image.SetPixel(x,y,x>=3 && x<7 && y>=3 && y<7?Color.green:background);image.Apply();
                    var t=TemplateImageProcessor.FromImage(image,10,background==Color.white?ImageBackground.White:ImageBackground.AutoBorder,.1f,4,false);
                    Assert.AreEqual(16,t.Count);Assert.IsTrue(t.Contains(4,4));Assert.IsFalse(t.Contains(0,0));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        [Test] public void PolygonReplacesMaskAndKeepsOutsideEmpty()
        {
            var t=TemplateImageProcessor.Shape(12,12,0);TemplateImageProcessor.FillPolygon(t,new List<Vector2Int>{new Vector2Int(2,2),new Vector2Int(9,2),new Vector2Int(9,9),new Vector2Int(2,9)},Color.red,true);
            Assert.IsTrue(t.Contains(5,5));Assert.IsFalse(t.Contains(0,0));Assert.Greater(t.ColorAt(new Vector2Int(5,5)).r,.9f);
        }
        [Test] public void GeneratedArrowsStayInsideStencilAndReplayWins()
        {
            var t=TemplateImageProcessor.Shape(18,22,3);for(int y=9;y<12;y++)for(int x=8;x<10;x++)t.mask[y*t.width+x]=false;
            var settings=new GenerationSettings{arrowCount=18,colorCount=4,probeRuns=8};string fail;var candidate=QuickLevelGenerator.Attempt(t,settings,2026,out fail);
            Assert.IsNotNull(candidate,fail);CollectionAssert.IsEmpty(PuzzleModel.Validate(candidate.level));
            foreach(var a in candidate.level.initialSegments)foreach(var p in PuzzleModel.Cells(a))Assert.IsTrue(t.Contains(p.x,p.y),p.ToString());
            var model=new PuzzleModel(candidate.level);foreach(var i in candidate.difficulty.solution){Assert.IsTrue(model.CanExit(i));model.Collect(i);}Assert.AreEqual(RoundState.Won,model.State);Assert.Less(candidate.difficulty.knownPeak,candidate.level.slotCapacity);
        }
        [Test] public void RecipeAndSeedReproduceIdenticalCandidate()
        {
            var t=TemplateImageProcessor.Shape(14,16,1);var cfg=new GenerationSettings{arrowCount=12,colorCount=4,probeRuns=8};string fail;
            var first=QuickLevelGenerator.Attempt(t,cfg,418,out fail);Assert.IsNotNull(first,fail);
            var recipe=new GenerationRecipe{template=t,settings=cfg,result=first};var restored=JsonUtility.FromJson<GenerationRecipe>(JsonUtility.ToJson(recipe));
            var second=QuickLevelGenerator.Attempt(restored.template,restored.settings,418,out fail);Assert.AreEqual(JsonUtility.ToJson(first.level),JsonUtility.ToJson(second.level));
        }
        [Test] public void ThreeColorsSevenSlotsHaveNoCapacityPressure()
        {
            var d=Free(1,1,1,2,2,2,3,3,3);var report=DifficultyAnalyzer.Analyze(d,Enumerable.Range(0,9).ToList(),64,true);
            Assert.IsFalse(report.slotPressurePossible);Assert.AreEqual(0,report.matchScore);Assert.AreEqual(0,report.randomFailureRate);Assert.IsTrue(report.solutionVerified);Assert.AreEqual(2,report.minimumPeak);Assert.IsTrue(report.minimumPeakExact);
        }
        [Test] public void FourColorsExposeRandomOrderingRisk()
        {
            var d=Free(1,1,1,2,2,2,3,3,3,4,4,4);var r=DifficultyAnalyzer.Analyze(d,Enumerable.Range(0,12).ToList(),64);
            Assert.IsTrue(r.slotPressurePossible);Assert.Greater(r.randomFailureRate,0);Assert.AreEqual(0,r.greedyFailureRate);Assert.Greater(r.matchScore,0);
        }
        [Test] public void BlockingChainIsHarderThanIndependentArrows()
        {
            var independent=Free(1,1,1);var chain=new LevelData{gridWidth=8,gridHeight=9};for(int i=0;i<3;i++)chain.initialSegments.Add(new ArrowData{colorDisplayIndex=1,points=new List<Vector2Int>{new Vector2Int(1,1+i*2),new Vector2Int(1,2+i*2)}});
            var a=DifficultyAnalyzer.Analyze(independent);var b=DifficultyAnalyzer.Analyze(chain);Assert.AreEqual(3,b.maxDepth);Assert.AreEqual(1,b.initialExits);Assert.Greater(b.arrowScore,a.arrowScore);
        }
        [Test] public void CycleIsRejectedRatherThanCalledHard()
        {
            var d=new LevelData{gridWidth=8,gridHeight=8};
            int[][] pairs={new[]{1,0,0,0},new[]{3,1,3,0},new[]{2,3,3,3},new[]{0,2,0,3},new[]{5,1,5,2},new[]{7,1,7,2}};
            foreach(var p in pairs)d.initialSegments.Add(new ArrowData{colorDisplayIndex=1,points=new List<Vector2Int>{new Vector2Int(p[0],p[1]),new Vector2Int(p[2],p[3])}});
            CollectionAssert.IsEmpty(PuzzleModel.Validate(d));var result=DifficultyAnalyzer.Analyze(d);Assert.IsFalse(result.structurallySolvable);Assert.IsFalse(result.solutionVerified);
        }
        [Test] public void StrictTwoColorImageCannotClaimHardMatchTarget()
        {
            var t=TemplateImageProcessor.Shape(14,16,0);t.palette=new List<Color>{Color.red,Color.blue};var cfg=new GenerationSettings{arrowCount=12,colorCount=4,allowExtraColors=false,matchTarget=85,probeRuns=8};string fail;var c=QuickLevelGenerator.Attempt(t,cfg,77,out fail);
            Assert.IsNotNull(c,fail);Assert.IsFalse(c.targetReached);Assert.LessOrEqual(c.level.colorPalette.Count,2);Assert.AreEqual(0,c.difficulty.matchScore);Assert.IsNotEmpty(c.warning);
        }
        [Test] public void BudgetExhaustionIsExplicitAndDifferentFromNoSolution()
        {
            var d=Free(1,1,1);bool exhausted;int nodes;var path=DifficultyAnalyzer.SolveWithPeak(d,new ArrowDependencyGraph(d),6,0,out exhausted,out nodes);Assert.IsNull(path);Assert.IsTrue(exhausted);
        }
        [Test] public void ImportedPaletteSurvivesRuntimeJsonAndLegacyFallback()
        {
            var d=Free(1,1,1);d.colorPalette.Add(new Color(.2f,.4f,.7f,1));var copy=d.Copy();Assert.AreEqual(d.colorPalette[0],PuzzleModel.DisplayColor(copy,1));Assert.AreEqual(ArrowBoardGraphic.Palette[1],PuzzleModel.DisplayColor(copy,2));
        }
    }
}
