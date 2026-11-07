"""PluginShellExe 微内核包：纯 Python，不 import 任何 GUI / Web 技术。

与 PluginShell 1.0 的 core/ 保持同一套插件协议与语义：插件发现、依赖拓扑、
生命周期、事件总线、服务容器、扩展点注册、副作用撤销。因此 py 版插件无需
任何修改即可在本宿主中加载。
"""
from .kernel import Kernel, OperationResult, PluginRecord

__all__ = ["Kernel", "OperationResult", "PluginRecord"]
