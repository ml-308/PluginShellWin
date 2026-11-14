# PluginShellWin · C# 原生插件宿主

> **公开仓库**：https://github.com/ml-308/PluginShellWin
> **下载**：https://github.com/ml-308/PluginShellWin/releases/latest
> **开源许可**：[MIT](LICENSE) · Copyright (c) 2026 ml-308

用 **C# / .NET 8 / WinForms** 重写的 PluginShell 宿主。App 本体是编译型 C# 程序，
不再是 Python 脚本；同时 **100% 兼容 PluginShell 的 Python 插件** —— 同一份
`manifest.json` + `plugin.py`，一行不改就能在这里跑起来，连插件目录都能共用。

---

## 1. 为什么是「C# 宿主 + Python 插件运行时」

插件是 Python（`plugin.py`），内容用 PySide6 的 `QWidget` 渲染。要让它们**零改动**运行，
就必须有 CPython + Qt 来执行和绘制。因此采用**双进程架构**（类似 VS Code 的扩展宿主）：

```
┌────────────────────────── PluginShellWin.exe（C# / .NET 8 / WinForms）──────────────────────────┐
│  Presentation   MainForm · PluginManagerForm · AboutForm · RuntimePanelHost(SetParent 嵌入)     │
│  Application    PluginService（用例、DTO、异常翻译）                                            │
│  Domain         PluginInfo / CategoryInfo / OperationResult / PluginState                       │
│  Infrastructure RuntimeProcess(JSON-Lines IPC) · RuntimeLocator · AppPaths · AppConfig          │
│                 AppLogger · SingleInstance                                                      │
└───────────────────────────────────────┬─────────────────────────────────────────────────────────┘
                                        │ stdin/stdout JSON-Lines（请求-响应，带自增 id）
┌───────────────────────────────────────▼─────────────────────────────────────────────────────────┐
│  PluginRuntime.exe（Python 3.13 + PySide6，PyInstaller 单文件）                                  │
│  kernel/ 微内核（发现/依赖拓扑/事件总线/服务容器/扩展点/撤销账本）                                │
│  sdk/    插件协议 1.0（与 py 版同一份）                                                          │
│  把 main.content 渲染成一个原生 Qt 窗口 → 返回 HWND 给 C# 侧嵌入                                │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **App 是 C#**：UI、进程生命周期、IPC、配置、日志、单实例全部由 .NET 实现。
- **插件运行时是黑盒**：只暴露一个 JSON 协议和两个返回值（窗口句柄、面板数）。
- **不使用任何 Web 技术**：宿主是 WinForms，插件内容是 Qt 原生控件，没有 HTML/CSS/JS、没有 WebView。

> 之所以不让 C# 自己渲染插件：插件返回的是 PySide6 `QWidget`，任何跨框架控件混排都会
> 变成不可维护的胶水代码。窗口级嵌入（`SetParent`）是两个 UI 框架共存时最稳的边界。

---

## 2. 兼容性

| 项目 | 状态 |
| --- | --- |
| 插件协议 `api` | **1.0**，与 PluginShell 完全一致 |
| `manifest.json` 全部字段 | ✅ 同一套（id/版本/依赖/分类/扩展点/服务/事件） |
| 插件是否需要改代码 | **不需要** |
| 插件目录 | ✅ 可自动复用 `%LOCALAPPDATA%\PluginShell\plugins` |
| 依赖拓扑 / 反向依赖阻止 / 副作用回滚 | ✅ 由同一份微内核保证 |
| 插件渲染 | ✅ 运行时创建原生 Qt 窗口，C# 用 `SetParent` 嵌入 |

---

## 3. 快速开始

### 3.1 用发布包

```text
解压 release 包 →
  PluginShellWin.exe        主程序（WinForms，窗口）
  runtime/PluginRuntime.exe 插件运行时（随包分发，无需 Python 环境）
  plugins/                  插件目录（初始为空）
```

首次启动是空壳：只显示「请安装插件」与「插件管理…」入口，符合"未安装插件时无任何业务内容"。

```bat
PluginShellWin.exe                                  启动窗口
PluginShellWin.exe --plugins D:\my\plugins          指定插件目录
PluginShellWin.exe --install D:\my\plugins\foo      启动前安装（可重复）
PluginShellWin.exe --check --report r.json          无界面自检并写 JSON 报告
PluginShellWin.exe --check --require-embed          自检并把"窗口嵌入失败"视为失败
PluginShellWin.exe --version
```

### 3.2 源码构建

```bash
# 1) C# 宿主
dotnet build PluginShellWin.sln -c Release

# 2) 插件运行时（需要 Python 3.10+ 与 PySide6-Essentials）
pip install -r runtime/requirements.txt pyinstaller
python runtime/build_runtime.py        # → runtime/dist/PluginRuntime.exe

# 3) 发布（自包含单文件，目标机不需要装 .NET）
dotnet publish src/PluginShellWin.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/app
```

### 3.3 测试

```bash
dotnet run --project tests/PluginShellWin.Tests -c Release
# 或直接运行
tests/PluginShellWin.Tests/bin/Release/net8.0/PluginShellWin.Tests.exe
```

### 3.4 原生安装包 PluginShellWin-Setup-1.2.0.exe

```bash
winget install --id JRSoftware.InnoSetup --silent   # 仅首次需要
python installer/build_installer.py                # → dist/PluginShellWin-Setup-1.2.0.exe
```

安装向导页面固定为：**欢迎 → 许可协议 → 安装位置 → 附加任务 → 准备安装 → 安装进度 → 完成**
（界面简体中文，系统语言非中文时自动 English），与项目内其它应用统一。

| 页面 | 选项 |
| --- | --- |
| License Agreement | MIT（必须接受） |
| **Select Destination Location** | **可修改安装目录**（默认 `%LOCALAPPDATA%\Programs\PluginShellWin`，支持 Browse，"准备安装"页复查路径） |
| **Select Additional Tasks** | ☑ 桌面快捷方式 · ☑ 开始菜单快捷方式（默认勾选）<br>☐ **安装示例插件**（lesson / tool / lab，默认不勾选，保持空壳） |
| Completing | ☐ 安装后启动 |
| 卸载 | 注册标准卸载项，设置 → 应用 → 已安装的应用 → PluginShellWin |

静默安装等价参数：

```bat
PluginShellWin-Setup-1.2.0.exe /VERYSILENT /DIR="D:\Apps\PluginShellWin" ^
          /TASKS="desktopicon,startmenuicon,samples"
PluginShellWin-Setup-1.2.0.exe /VERYSILENT /NOICONS /DIR="D:\ps"     :: 完全不要快捷方式
```

- `/DIR=` 覆盖安装位置；`/NOICONS` 跳过全部快捷方式；
- `/TASKS=` 只选中列出的任务（示例插件任务名为 `samples`），`/MERGETASKS=` 在默认基础上追加；
- `/VERYSILENT` 自动接受许可、跳过完成页启动；
- `/LOG="install.log"` 写安装日志。

---

## 4. 目录结构

```
PluginShellWin/
├── PluginShellWin.sln
├── src/
│   ├── PluginShellWin.Core/            领域 + 应用层（无 UI 依赖，可单测）
│   │   ├── Domain/                     AppInfo, PluginModels, Errors
│   │   └── Application/                IPluginRuntime(端口), PluginService(用例)
│   ├── PluginShellWin.Infrastructure/  适配器：进程+IPC、路径、配置、日志、单实例
│   └── PluginShellWin.App/             WinForms 表现层 + 组合根
│       ├── Program.cs                  启动编排 / --check
│       ├── CheckRunner.cs              自检报告 + 嵌入校验
│       ├── CommandLineOptions.cs       命令行
│       ├── NativeMethods.cs            SetParent / GetParent / MoveWindow
│       ├── Presentation/               MainForm / PluginManagerForm / AboutForm / RuntimePanelHost
│       └── config/appsettings.json     默认配置
├── runtime/                            插件运行时（Python）
│   ├── runtime_main.py                 JSON-Lines 服务 + Qt 窗口宿主
│   ├── kernel/  sdk/                   与 PluginShell 同一份微内核与协议
│   └── build_runtime.py                PyInstaller 打包 + 协议探针
├── installer/                          Inno Setup 6 安装包
│   ├── setup.iss                       应用身份与文件清单（含示例插件任务）
│   ├── common.iss                      统一向导：页面顺序、任务项、文案、卸载行为
│   ├── ChineseSimplified.isl           简体中文语言包
│   ├── assets/wizard-small.png         向导头图（由 app.ico 生成）
│   ├── build_installer.py              构建 dist/PluginShellWin-Setup-1.2.0.exe
│   └── staging/                        中文安装说明
├── tests/PluginShellWin.Tests/         零依赖测试运行器（8 项）
├── assets/app.ico                      应用图标
└── dist/                               发布产物（不入库）
```

---

## 5. IPC 协议（C# ↔ 运行时）

一行一个 JSON。请求 `{"id":1,"cmd":"plugins"}`，响应 `{"id":1,"ok":true,"result":{...}}`
或 `{"id":1,"ok":false,"error":"..."}`。

| 命令 | 参数 | 返回 |
| --- | --- | --- |
| `hello` | — | `protocol` / `qt` / `python` / `pid` / `pluginsDir` |
| `plugins` | — | 插件列表（含 state、category、dependencies） |
| `categories` | — | 分类聚合 + 内容贡献总数 |
| `install` | `path` | `ok` / `message` |
| `enable` / `disable` / `uninstall` | `pluginId` | `ok` / `message` |
| `show` | `category` / `width` / `height` | `hwnd` / `count` |
| `resize` | `width` / `height` | `ok` |
| `logs` | `limit` | 运行时日志 |
| `shutdown` | — | 退出 |

**关键约定**：业务拒绝（依赖保护、清单非法）返回 `ok:false` + `message`，属于正常结果；
只有协议/进程级故障才对应异常与超时。C# 侧据此把结果翻译成 `OperationResult`，UI 不做 try/catch 分支。

---

## 6. 截图式的验收证据（`--check`）

```json
{
  "app": "PluginShellWin",
  "version": "1.1.0",
  "protocol": "1.0",
  "host": ".NET 8.0.33 / WinForms",
  "runtime": { "qt": "6.12.0", "python": "3.13.14", "pid": 18400 },
  "installed": 3,
  "enabled": 3,
  "contributions": 3,
  "categories": [
    { "id": "lesson", "label": "知识点讲解", "count": 1 },
    { "id": "tool", "label": "工具", "count": 1 },
    { "id": "lab", "label": "实验室", "count": 1 }
  ],
  "embed": { "skip": false, "ok": true, "hwnd": "0x240376", "panels": 3, "error": "" },
  "failed": []
}
```

`embed.ok = true` 意味着：运行时真的创建了 Qt 窗口，C# 用 `SetParent` 嵌入成功，
并且 `GetParent(child) == 面板句柄` 校验通过。这不是"看起来能跑"，是可断言的证据。

---

## 7. 测试覆盖

| # | 用例 | 说明 |
| --- | --- | --- |
| 1 | Config 默认值 | autoAdopt / singleInstance / 窗口尺寸 / 日志级别 |
| 2 | 非法 logLevel 被拒绝 | 抛 `ConfigurationException`，不静默降级 |
| 3 | 窗口尺寸校验 | 过小尺寸直接拒绝 |
| 4 | 插件目录识别 | 有 `manifest.json` 才算插件 |
| 5 | `--plugins` 优先 | 覆盖策略正确 |
| 6 | 命令失败返回结果 | 运行时异常 → `OperationResult(false, ...)` |
| 7 | 列表异常翻译 | → `PluginRuntimeException` |
| 8 | **协议握手 + 真插件装载**（集成） | 启动真实运行时，装载 3 个 py 插件、校验分类、依赖保护与卸载回滚 |

外加 `--check --require-embed` 作为窗口与嵌入的端到端验收。

---

## 8. 已知边界

- **运行时依赖 Python 生态**：`PluginRuntime.exe` 内含 CPython + PySide6（约 37 MB）。
  这是"插件是 Python"的必然结果，不是 App 本体的依赖。
- **Qt 子模块**：运行时只打包 QtCore/QtGui/QtWidgets。插件若用非常规模块（如 QtMultimedia），
  需在 `runtime/build_runtime.py` 的 `HIDDEN_IMPORTS` 里补充后重新打包。
- **窗口嵌入是 Windows 专有**：`SetParent` 方案天然绑定 Win32；跨平台需要换渲染策略。
- **单实例**：命名互斥体 `Local\PluginShellWin.instance`，第二个实例会提示已在运行。
- **配置/日志位置**：默认 `%LOCALAPPDATA%\PluginShellWin`，可用 `PLUGINSHELLWIN_DATA_DIR` 覆盖；
  只读环境下自动回退到 exe 同级或临时目录。
- **插件目录解析与只读安装目录**：按
  `--plugins` → `PLUGINSHELL_PLUGINS` → 配置 `pluginsDir` → `exe\plugins`（可写）→
  py 版目录 → 数据目录 → 临时目录 依次尝试。**如果程序被装到 `C:\Program Files`
  这类普通用户不可写的位置，会自动回退到 `%LOCALAPPDATA%\PluginShellWin\plugins`**，
  界面状态栏与自检报告的 `pluginsSource` 会注明「已回退」，不会再出现"无法创建文件夹"。
  显式指定的目录若不可写，会给出明确错误并提示改用哪个参数。

---

## 9. 开源许可

MIT License，见 [LICENSE](LICENSE)。Copyright (c) 2026 ml-308。
第三方插件为独立作品，许可由插件作者自行声明。
