"""插件 SDK 公共导出（与 PluginShell 1.0 完全一致，api = "1.0"）。

插件作者只需：from sdk import Plugin, content_panel
同一份插件源码可同时运行在 PluginShell（py 版）与 PluginShellExe（exe 版）中。
"""
from .api import (
    EventsView,
    ExtensionsView,
    Plugin,
    PluginContext,
    ServicesView,
    content_panel,
)

__all__ = [
    "Plugin",
    "PluginContext",
    "EventsView",
    "ServicesView",
    "ExtensionsView",
    "content_panel",
]
