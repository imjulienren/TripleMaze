using UnityEditor;
using UnityEngine;
namespace TripleMaze.Editor
{
    public sealed class LevelControl : EditorWindow
    {
        int level;
        public static int LevelCount
        {get{int count=0;while(Resources.Load<TextAsset>($"Levels/level_{count+1:00}"))count++;return Mathf.Max(1,count);}}
        [MenuItem("TripleMaze/关卡控制台")]
        public static void Open(){GetWindow<LevelControl>("关卡控制台").Show();}
        [MenuItem("TripleMaze/一键通关")]
        public static void WinCurrentLevel()
        {
            if(!EditorApplication.isPlaying)return;
            var game=Object.FindObjectOfType<TripleMazeGame>();
            if(game)game.DebugWinCurrentLevel();
        }
        [MenuItem("TripleMaze/一键通关",true)]
        public static bool CanWinCurrentLevel()
        {
            if(!EditorApplication.isPlaying)return false;
            var game=Object.FindObjectOfType<TripleMazeGame>();
            return game && game.Page=="Game" && game.Model!=null && game.Model.State==RoundState.Playing && !game.DebugAutoPlaying;
        }
        void OnEnable(){level=EditorPrefs.GetInt("TripleMaze.DebugLevel",1);minSize=new Vector2(320,150);}
        public static void SwitchTo(int level)
        {
            level=Mathf.Clamp(level,1,LevelCount);EditorPrefs.SetInt("TripleMaze.DebugLevel",level);
            if(!EditorApplication.isPlaying){SessionState.SetInt("TripleMaze.NextDebugLevel",level);return;}
            var game=Object.FindObjectOfType<TripleMazeGame>();var asset=Resources.Load<TextAsset>($"Levels/level_{level:00}");
            if(game && asset)game.StartLevel(JsonUtility.FromJson<LevelData>(asset.text));
        }
        void OnGUI()
        {
            GUILayout.Space(12);level=EditorGUILayout.IntSlider("切换到关卡",level,1,LevelCount);
            if(GUILayout.Button(EditorApplication.isPlaying?"立即切换关卡":"仅下次从此关启动",GUILayout.Height(40)))
            {SwitchTo(level);}
            EditorGUILayout.HelpBox("仅在 Unity 编辑器中显示，不会出现在玩家界面或导出的游戏里。",MessageType.Info);
        }
    }
}
