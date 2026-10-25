using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TripleMaze.Editor
{
    [InitializeOnLoad] public static class DemoSetup
    {
        static DemoSetup(){EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingEditMode){var json=SessionState.GetString("TripleMaze.Preview","");if(!string.IsNullOrEmpty(json)){TripleMazeGame.PreviewLevel=JsonUtility.FromJson<LevelData>(json);SessionState.EraseString("TripleMaze.Preview");}}};}
        [MenuItem("TripleMaze/创建并打开 Demo 场景")]
        public static void CreateScene()
        {
            const string path="Assets/Scenes/TripleMaze.unity";
            if(System.IO.File.Exists(path)){EditorSceneManager.OpenScene(path);return;}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.transform.position=new Vector3(0,0,-10);camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color32(247,246,242,255);
            new GameObject("Directional Light",typeof(Light)).GetComponent<Light>().type=LightType.Directional;
            new GameObject("TripleMaze",typeof(TripleMazeGame));
            EditorSceneManager.SaveScene(scene,path);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true)};
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            PlayerSettings.companyName="TripleMaze Demo";PlayerSettings.productName="TripleMaze";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.demo.triplemaze");
        }
    }
}
