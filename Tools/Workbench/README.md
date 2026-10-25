# Triple Maze 游戏工作台

统一入口：http://127.0.0.1:8877/

在 Unity 顶部选择 `TripleMaze > 游戏工作台`，自动启动本机服务并打开网页。服务已运行时会直接打开，避免重复启动。启动日志在 `Temp/Workbench.log`。

也可双击 `启动工作台.command` 启动；关闭该启动窗口会停止它启动的服务。

顶部“界面 / 音效”页签共用一个网页、一个本机服务和一个启动脚本。

- 界面：关卡、菜单、结算、设置弹窗，可调尺寸、位置、显隐和可编辑文案。
- 文案：右侧“编辑界面文案”，也可点击“修改界面文案”。`{level}` 和 `{level:00}` 是动态关卡编号。
- 设置：音乐、音效两条音量滑杆，拖至零可静音。关卡设置包含重新开始、返回菜单、关闭。
- 音效：逐项试听、组合试听、清爽方案、导入导出和应用到项目。

## 与项目同步

服务启动期间，网页每 1.5 秒检查当前项目源码、界面记录和音频美术资源。检测到变化后重新读取并刷新。当前界面中已映射的布局、文案和资源从项目读取；这是浏览器模拟，不是 Unity 渲染窗口。未来新增控件或改变界面生成结构时，需要一并更新 `server.py` 的映射和 `preview-fragment.html`。

网页刷新以项目为准。刷新前的预览参数会保留在浏览器的 `triplemaze-previous-draft` 中。

## 保存和应用

- “保存调节稿”写入 `Docs/ui-preview-draft.json`，不覆盖 Unity 界面代码。“载入调节稿”恢复这份参数。
- “导出参数”下载 UI JSON，供后续应用到项目。
- 音效页“应用音频配置到项目”只写入 `Assets/TripleMaze/Resources/Polish/AudioMix.json`，游戏下次启动时读取。
- 音乐和音效总音量只用于网页试听，游戏内玩家设置独立保存。

未运行 Unity 或游戏测试。界面视觉效果仍由用户在游戏中验收。

## 文件

- `server.py`：统一服务、项目参数读取、资源和配置接口。
- `preview-fragment.html`、`defaults.json`：交互预览和备用初始值。
- `local-shell.html`、`workbench.css`、`workbench.js`：统一网页外壳与同步、保存功能。
- `native-tweak.js`：本地尺寸、位置和显隐控制。
- `audio-fragment.html`、`audio.css`、`audio.js`：从原声音工作台合并的音效模块。

原两套工具的重复页面、独立截图和静态导出预览已移除，合并前备份位于 `/tmp/triplemaze-workbench-before-merge.zip`。
