"""插件模块加载器：用 importlib 把插件目录作为独立包隔离装载。

副作用（sys.modules 条目、sys.path 条目）封装在 ModuleHandle 里，
卸载时由内核调用 unload() 一并清理。
"""
from __future__ import annotations

import importlib.util
import itertools
import re
import sys
from pathlib import Path
from typing import Any

from .errors import PluginLoadError
from .manifest import Manifest
from sdk.api import Plugin

_counter = itertools.count(1)


class ModuleHandle:
    def __init__(self, module_name: str, added_syspath: str | None):
        self.module_name = module_name
        self.added_syspath = added_syspath
        self._unloaded = False

    def unload(self) -> None:
        if self._unloaded:
            return
        self._unloaded = True
        prefix = self.module_name + "."
        for key in [k for k in sys.modules if k == self.module_name or k.startswith(prefix)]:
            sys.modules.pop(key, None)
        if self.added_syspath and self.added_syspath in sys.path:
            try:
                sys.path.remove(self.added_syspath)
            except ValueError:
                pass


def load_plugin(
    manifest: Manifest, project_root: Path
) -> tuple[type[Plugin], ModuleHandle]:
    """加载并返回 (插件类, 模块句柄)。任何失败都转成 PluginLoadError。"""
    plugin_dir = Path(manifest.path).resolve() if manifest.path else None
    if plugin_dir is None or not plugin_dir.is_dir():
        raise PluginLoadError(f"插件 {manifest.id} 的目录不存在")

    entry = (plugin_dir / manifest.entry).resolve()
    if not entry.is_relative_to(plugin_dir):
        raise PluginLoadError(f"插件 {manifest.id} 的 entry 越界: {manifest.entry}")
    if not entry.is_file():
        raise PluginLoadError(f"插件 {manifest.id} 找不到入口文件 {manifest.entry}")

    root = str(Path(project_root).resolve())
    added_syspath: str | None = None
    if root not in sys.path:
        sys.path.insert(0, root)
        added_syspath = root

    safe_id = re.sub(r"[^a-z0-9_]", "_", manifest.id.lower())
    module_name = f"_pluginshell_{safe_id}_{next(_counter)}"
    handle = ModuleHandle(module_name, added_syspath)

    try:
        spec = importlib.util.spec_from_file_location(
            module_name,
            entry,
            submodule_search_locations=[str(plugin_dir)],
        )
        if spec is None or spec.loader is None:
            raise PluginLoadError(f"插件 {manifest.id} 无法创建模块 spec")
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        try:
            spec.loader.exec_module(module)
        except BaseException as exc:  # noqa: BLE001 - 导入期故障必须隔离
            raise PluginLoadError(f"插件 {manifest.id} 导入失败: {exc!r}") from exc

        plugin_class: Any = getattr(module, manifest.class_name, None)
        if not isinstance(plugin_class, type) or not issubclass(plugin_class, Plugin):
            raise PluginLoadError(
                f"插件 {manifest.id} 的 {manifest.entry} 中未找到继承自 sdk.Plugin 的 "
                f"类 {manifest.class_name}"
            )
        return plugin_class, handle
    except PluginLoadError:
        handle.unload()
        raise
    except Exception as exc:  # noqa: BLE001
        handle.unload()
        raise PluginLoadError(f"插件 {manifest.id} 装载失败: {exc!r}") from exc
