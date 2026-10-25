using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TripleMaze.Editor
{
    public static class WorkbenchLauncher
    {
        const string MenuPath = "TripleMaze/游戏工作台";
        const string Address = "http://127.0.0.1:8877/";
        static bool opening;

        [MenuItem(MenuPath, false, 100)]
        public static async void Open()
        {
            if (opening) return;
            opening = true;
            string logPath = null;
            try
            {
                if (!await Task.Run(IsRunning))
                {
                    string project = Directory.GetParent(Application.dataPath).FullName;
                    string script = Path.Combine(project, "Tools/Workbench/server.py");
                    if (!File.Exists(script)) throw new FileNotFoundException("找不到游戏工作台服务文件", script);
                    string python = Application.platform == RuntimePlatform.OSXEditor ? "/usr/bin/python3" :
                        Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";
                    logPath = Path.Combine(project, "Temp/Workbench.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    var start = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = "-u \"" + script.Replace("\"", "\\\"") + "\" --port 8877",
                        WorkingDirectory = Path.GetDirectoryName(script),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var process = new Process { StartInfo = start };
                    string outputPath = logPath;
                    var outputLock = new object();
                    DataReceivedEventHandler output = (_, e) =>
                    {
                        if (e.Data == null) return;
                        try { lock (outputLock) File.AppendAllText(outputPath, e.Data + Environment.NewLine); }
                        catch (IOException) { }
                    };
                    process.OutputDataReceived += output;
                    process.ErrorDataReceived += output;
                    if (!process.Start()) throw new InvalidOperationException("工作台服务未能启动");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    bool ready = false;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        await Task.Delay(200);
                        if (await Task.Run(IsRunning)) { ready = true; break; }
                    }
                    if (!ready) throw new TimeoutException("工作台服务启动超时，请检查端口 8877 和 Python 环境");
                }
                Application.OpenURL(Address);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("游戏工作台：" + error.Message);
                EditorUtility.DisplayDialog("游戏工作台", error.Message +
                    (logPath == null ? "" : "\n\n启动日志：" + logPath), "确定");
            }
            finally { opening = false; }
        }

        [MenuItem(MenuPath, true)]
        static bool CanOpen() => !opening;

        static bool IsRunning()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(Address + "api/status");
                request.Timeout = 700;
                request.ReadWriteTimeout = 700;
                request.Proxy = null;
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd().Contains("\"revision\"");
            }
            catch (WebException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
