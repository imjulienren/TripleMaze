using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace TripleMaze.Tests
{
    public class DemoFlowTests
    {
        TripleMazeGame game;
        GameObject testListener;
        [System.Serializable] class CampaignSource { public int level; public int[] knownSolution; }
        [System.Serializable] class CampaignSources { public CampaignSource[] items; }
        static string Caption(Button b){var text=b.GetComponentInChildren<Text>();return text?text.text:b.name;}
        [UnitySetUp] public IEnumerator SetUp()
        {if(!Object.FindObjectOfType<AudioListener>()){testListener=new GameObject("Flow Test Audio Listener");testListener.AddComponent<AudioListener>();}game=Object.FindObjectOfType<TripleMazeGame>();if(!game)game=new GameObject("Flow Test").AddComponent<TripleMazeGame>();game.Home();yield return null;}
        [UnityTearDown] public IEnumerator TearDown()
        {if(testListener)Object.Destroy(testListener);testListener=null;yield return null;}
        [UnityTest] public IEnumerator HomeStartAndRetryWorkThroughButtons()
        {
            var start=Object.FindObjectsOfType<Button>().First(b=>Caption(b).StartsWith("开始第"));start.onClick.Invoke();yield return null;
            Assert.AreEqual("Game",game.Page);Assert.Greater(game.Model.Remaining,0);
            var board=Object.FindObjectOfType<ArrowBoardGraphic>();Assert.IsNotNull(board.GetComponent<CanvasRenderer>());Assert.Greater(board.Path(0).Count,1);
            var retry=Object.FindObjectsOfType<Button>().First(b=>Caption(b)=="重试");retry.onClick.Invoke();yield return null;Assert.AreEqual(0,game.Model.Moves);
            game.Home();yield return null;Assert.AreEqual(1,Object.FindObjectsOfType<Canvas>().Length);Assert.AreEqual("Home",game.Page);
        }
        [UnityTest] public IEnumerator ArrowAnimationsLeadToWinningResultAndNextLevel()
        {
            game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>("Levels/level_01").text));yield return null;
            bool limit;var solution=PuzzleModel.Solve(game.Model.Level,25000,out limit);Assert.IsNotNull(solution);
            foreach(var i in solution)
            {
                game.ClickArrow(i);
                // 最后一枚包括滑出、三消和 1.8 秒胜利波纹，单独给完整结算留出时间。
                float timeout=Time.realtimeSinceStartup+(i==solution[solution.Count-1]?6:3);
                while(game.Busy && Time.realtimeSinceStartup<timeout)yield return null;
                Assert.IsFalse(game.Busy,"箭头动画或胜利结算未在期限内完成");
            }
            Assert.AreEqual("Won",game.Page);
            ScreenCapture.CaptureScreenshot(Application.dataPath+"/../Captures/result.png");yield return null;
            var next=Object.FindObjectsOfType<Button>().First(b=>Caption(b)=="下一关");next.onClick.Invoke();yield return null;Assert.AreEqual("Game",game.Page);Assert.AreEqual(2,game.LevelNumber);
        }
        [UnityTest] public IEnumerator FullTrayDisplaysFailureAndRetryResets()
        {
            var d=new LevelData{gridWidth=26,gridHeight=8};int[] colors={1,1,2,2,3,3,4,1,2,3,4,4};
            for(int i=0;i<colors.Length;i++)d.initialSegments.Add(new ArrowData{colorDisplayIndex=colors[i],points=new System.Collections.Generic.List<Vector2Int>{new Vector2Int(i*2,1),new Vector2Int(i*2,3)}});
            game.StartLevel(d);yield return null;
            for(int i=0;i<7;i++){game.ClickArrow(i);float timeout=Time.realtimeSinceStartup+3;while(game.Busy && Time.realtimeSinceStartup<timeout)yield return null;}
            Assert.AreEqual("Lost",game.Page);
            var retry=Object.FindObjectsOfType<Button>().First(b=>Caption(b)=="再试一次");retry.onClick.Invoke();yield return null;Assert.AreEqual("Game",game.Page);Assert.IsEmpty(game.Model.Tray);
        }
        [UnityTest] public IEnumerator UndoRestoresBoardAndTrayAfterAnimatedCollection()
        {
            game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>("Levels/level_04").text));yield return null;
            int count=game.Model.Remaining;game.ClickArrow(game.Model.Available()[0]);
            float timeout=Time.realtimeSinceStartup+3;while(game.Busy && Time.realtimeSinceStartup<timeout)yield return null;
            Assert.AreEqual(count-1,game.Model.Remaining);
            Object.FindObjectsOfType<Button>().First(b=>b.GetComponentInChildren<Text>()!=null && Caption(b)=="撤回").onClick.Invoke();yield return null;
            Assert.AreEqual(count,game.Model.Remaining);Assert.IsEmpty(game.Model.Tray);Assert.AreEqual(0,game.Model.Moves);
        }
        [UnityTest] public IEnumerator RepeatedTapDuringFlightCannotCollectSameArrowTwice()
        {
            game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>("Levels/level_04").text));yield return null;
            int i=game.Model.Available()[0];game.ClickArrow(i);game.ClickArrow(i);game.ClickArrow(i);
            float timeout=Time.realtimeSinceStartup+3;while(game.Busy && Time.realtimeSinceStartup<timeout)yield return null;
            Assert.AreEqual(1,game.Model.Moves);Assert.AreEqual(1,game.Model.Removed.Count);Assert.AreEqual(1,game.Model.Tray.Count);
        }
        [UnityTest] public IEnumerator LeavingMidFlightCancelsPendingCollection()
        {
            game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>("Levels/level_04").text));yield return null;
            game.ClickArrow(game.Model.Available()[0]);game.Home();yield return new WaitForSeconds(.85f);
            Assert.AreEqual("Home",game.Page);Assert.IsFalse(game.Busy);Assert.AreEqual(1,Object.FindObjectsOfType<Canvas>().Length);
        }
        [UnityTest] public IEnumerator BlockedArrowTravelsToContactBeforeDamageAndReturns()
        {
            var d=new LevelData{gridWidth=10,gridHeight=10};
            d.initialSegments.Add(new ArrowData{points=new System.Collections.Generic.List<Vector2Int>{new Vector2Int(2,5),new Vector2Int(1,5)}});
            d.initialSegments.Add(new ArrowData{points=new System.Collections.Generic.List<Vector2Int>{new Vector2Int(6,4),new Vector2Int(6,6)}});
            // 保持每色数量为三的倍数，第三枚放在碰撞测试路径之外。
            d.initialSegments.Add(new ArrowData{points=new System.Collections.Generic.List<Vector2Int>{new Vector2Int(2,1),new Vector2Int(1,1)}});
            game.StartLevel(d);yield return null;var board=Object.FindObjectOfType<ArrowBoardGraphic>();
            game.ClickArrow(0);Assert.AreEqual(3,game.Model.Health);Assert.AreEqual(0,board.Moving);
            float deadline=Time.realtimeSinceStartup+2;
            while(board.ImpactTarget<0 && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.AreEqual(1,board.ImpactTarget);Assert.AreEqual(2,game.Model.Health);
            Assert.That(board.Travel,Is.GreaterThan(3).And.LessThan(4));
            while(game.Busy && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.IsFalse(game.Busy);Assert.AreEqual(-1,board.Moving);Assert.AreEqual(0,board.Travel);
            Assert.IsEmpty(game.Model.Removed);Assert.IsEmpty(game.Model.Tray);Assert.AreEqual(2,game.Model.Health);
        }
        [UnityTest] public IEnumerator FlightShowsLocalSpriteTrailAndCleansItUp()
        {
            game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>("Levels/level_04").text));yield return null;
            game.ClickArrow(game.Model.Available()[0]);yield return new WaitForSeconds(.15f);
            var board=Object.FindObjectOfType<ArrowBoardGraphic>();
            Assert.IsTrue(game.Busy);Assert.GreaterOrEqual(board.Moving,0);
            var trail=board.GetComponentsInChildren<Image>().FirstOrDefault(x=>x.name=="Arrow Trail");
            Assert.IsNotNull(trail);Assert.IsNotNull(trail.sprite);Assert.IsFalse(trail.raycastTarget);
            float deadline=Time.realtimeSinceStartup+3;while(game.Busy && Time.realtimeSinceStartup<deadline)yield return null;
            yield return new WaitForSeconds(.2f);
            Assert.IsFalse(board.GetComponentsInChildren<Image>().Any(x=>x.name=="Arrow Trail"));Assert.AreEqual(1,game.Model.Moves);
        }
        [UnityTest] public IEnumerator CampaignPassesTwentyAndWrapsAfterThirty()
        {
            var json=System.IO.File.ReadAllText(Application.dataPath+"/../Docs/source-levels.json");
            var sources=JsonUtility.FromJson<CampaignSources>("{\"items\":"+json+"}");
            foreach(int n in new[]{20,30})
            {
                game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>($"Levels/level_{n:00}").text));
                foreach(int i in sources.items.First(s=>s.level==n).knownSolution)game.Model.Collect(i);
                Assert.AreEqual(RoundState.Won,game.Model.State);
                typeof(TripleMazeGame).GetMethod("Result",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(game,null);
                yield return null;
                Object.FindObjectsOfType<Button>().First(b=>Caption(b)=="下一关").onClick.Invoke();yield return null;
                Assert.AreEqual(n==30?1:21,game.LevelNumber);Assert.AreEqual(game.LevelNumber,game.Model.Level.levelId);
            }
        }
        [UnityTest] public IEnumerator AllThirtyBoardsRenderWithoutMeshOverflow()
        {
            for(int n=1;n<=30;n++)
            {
                game.StartLevel(JsonUtility.FromJson<LevelData>(Resources.Load<TextAsset>($"Levels/level_{n:00}").text));yield return null;yield return null;
                Canvas.ForceUpdateCanvases();var board=Object.FindObjectOfType<ArrowBoardGraphic>();var mesh=board.canvasRenderer.GetMesh();Assert.Greater(mesh.vertexCount,0,"Level "+n);Assert.Less(mesh.vertexCount,65000,"Level "+n);Assert.AreEqual(n,game.LevelNumber);
            }
        }
    }
}
