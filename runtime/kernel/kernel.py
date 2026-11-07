"""微内核门面：把发现、依赖、生命周期、事件、服务、扩展点组合在一起。

对外只有 Kernel 一个类；GUI / CLI / 测试都通过它操作，任何插件故障都被隔离，
不会让壳子退出。内核自身不贡献任何业务内容，也不认识任何具体插件分类。
"""
from __future__ import annotations

import json
import shutil
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable

from .bus import EventBus
from .context import PluginContext
from .errors import DependencyError, KernelError, ManifestError, PluginStateError
from .loader import load_plugin
from .manifest import Category, Manifest, load_manifest, version_satisfies
from .registry import ExtensionRegistry
from .resolver import dependent_closure, resolve_load_order, reverse_dependents
from .services import ServiceContainer


@dataclass
class PluginRecord:
    manifest: Manifest
    state: str = "disabled"  # disabled | enabled | failed
    message: str = ""
    instance: Any = None
    context: Any = None


@dataclass
class OperationResult:
    ok: bool
    message: str = ""


class Kernel:
    """微内核。

    plugins_dir  插件安装目录（文件夹 + manifest.json）
    project_root 项目根，加入 sys.path 以便插件 `from sdk import ...`
    state_path   启用集持久化文件；传 None 表示纯内存（测试用）
    """

    def __init__(
        self,
        plugins_dir: Path,
        project_root: Path | None = None,
        state_path: Path | None = None,
        log_sink: Callable[[str], None] | None = None,
    ):
        self.plugins_dir = Path(plugins_dir).resolve()
        self.project_root = (Path(project_root) if project_root else self.plugins_dir.parent).resolve()
        self.state_path = Path(state_path).resolve() if state_path else None
        self.plugins_dir.mkdir(parents=True, exist_ok=True)
        if str(self.project_root) not in sys.path:
            sys.path.insert(0, str(self.project_root))

        self.logs: list[str] = []
        self._log_sink = log_sink
        self.bus = EventBus(error_sink=self._log)
        self.services = ServiceContainer()
        self.extensions = ExtensionRegistry(error_sink=self._log)

        self.records: dict[str, PluginRecord] = {}
        self.enabled: set[str] = set()
        self._loaded: dict[str, tuple[Any, PluginContext, Any]] = {}
        self._load_order: list[str] = []
        self._observers: dict[str, Callable[[], None]] = {}
        self._observer_seq = 0

    # ==================================================================
    # 日志与观察者
    # ==================================================================
    def _log(self, message: str) -> None:
        line = f"[{time.strftime('%H:%M:%S')}] {message}"
        self.logs.append(line)
        if len(self.logs) > 500:
            self.logs.pop(0)
        if self._log_sink:
            try:
                self._log_sink(line)
            except Exception:  # noqa: BLE001 - 日志消费者异常不得影响内核
                pass

    def on_changed(self, callback: Callable[[], None]) -> str:
        self._observer_seq += 1
        token = f"kernel-obs-{self._observer_seq}"
        self._observers[token] = callback
        return token

    def off_changed(self, token: str) -> bool:
        return self._observers.pop(token, None) is not None

    def _notify(self) -> None:
        for callback in list(self._observers.values()):
            try:
                callback()
            except Exception as exc:  # noqa: BLE001
                self._log(f"内核观察者异常: {exc!r}（已隔离）")

    # ==================================================================
    # 发现 / 启动
    # ==================================================================
    def discover(self) -> dict[str, Manifest]:
        """扫描 plugins/，读取全部清单。单个清单损坏只跳过该插件。"""
        found: dict[str, Manifest] = {}
        if self.plugins_dir.is_dir():
            for child in sorted(self.plugins_dir.iterdir()):
                if not child.is_dir() or child.name.startswith("."):
                    continue
                if not (child / "manifest.json").is_file():
                    continue
                try:
                    manifest = load_manifest(child)
                except ManifestError as exc:
                    self._log(f"跳过清单非法的插件目录 {child.name}: {exc}")
                    continue
                if manifest.id in found:
                    self._log(f"插件 ID 冲突，忽略 {child.name}: {manifest.id}")
                    continue
                found[manifest.id] = manifest

        rebuilt: dict[str, PluginRecord] = {}
        for pid, manifest in found.items():
            old = self.records.get(pid)
            if old is None:
                rebuilt[pid] = PluginRecord(manifest=manifest)
            elif old.manifest != manifest:
                if pid in self._loaded:
                    self._unload_one(pid)
                self.enabled.discard(pid)
                rebuilt[pid] = PluginRecord(manifest=manifest)
            else:
                rebuilt[pid] = old
        self.records = rebuilt
        self.enabled &= set(found)
        return found

    def bootstrap(self) -> None:
        """启动：发现插件 → 按依赖拓扑顺序重放上次启用集。空目录即空壳状态。"""
        self.discover()
        persisted = self._load_state()
        desired = {pid for pid in persisted if pid in self.records}
        if desired:
            try:
                self._apply(desired)
            except KernelError as exc:
                self._log(f"启动恢复加载失败（已隔离，壳子继续运行）: {exc}")
        else:
            self._notify()
        self._log(
            f"微内核就绪：已安装 {len(self.records)} 个插件，"
            f"已启用 {len(self.enabled)} 个，内容贡献 "
            f"{len(self.extensions.contributions('main.content'))} 个"
        )

    def shutdown(self) -> None:
        """退出前逆序卸载全部插件（保留启用集，供下次启动恢复）。"""
        for pid in reversed(list(self._load_order)):
            self._unload_one(pid)
        self._save_state()

    # ==================================================================
    # 分类索引（纯通用，内核不认识任何具体分类 ID）
    # ==================================================================
    def categories(self, enabled_only: bool = False) -> tuple[Category, ...]:
        """聚合插件清单中自报的分类，按 order / id 排序。"""
        scope = self.enabled if enabled_only else set(self.records)
        merged: dict[str, Category] = {}
        for pid in sorted(scope):
            category = self.records[pid].manifest.category
            current = merged.get(category.id)
            if current is None or (category.order, category.label) < (current.order, current.label):
                merged[category.id] = category
        return tuple(sorted(merged.values(), key=lambda c: (c.order, c.id)))

    def plugins_by_category(self, category_id: str) -> tuple[str, ...]:
        return tuple(
            sorted(
                pid
                for pid, record in self.records.items()
                if record.manifest.category.id == category_id
            )
        )

    # ==================================================================
    # 安装 / 启用 / 停用 / 卸载
    # ==================================================================
    def install(self, source: Path, enable: bool = True) -> OperationResult:
        """把一个"文件夹 + manifest.json"安装进 plugins/，默认随即按拓扑序启用。

        不覆盖任何已存在的插件目录。启用失败不会回滚安装（插件仍可稍后启用），
        而是把失败原因记录在插件状态里并隔离，符合"加载失败不拖垮壳子"。
        """
        src = Path(source).resolve()
        if not src.is_dir():
            raise KernelError(f"插件源目录不存在或不是文件夹: {src}")
        try:
            manifest = load_manifest(src)
        except ManifestError as exc:
            raise KernelError(f"拒绝安装，插件清单非法: {exc}") from exc

        destination = self.plugins_dir / manifest.id
        if destination.exists():
            raise KernelError(f"插件 {manifest.id} 已安装，拒绝覆盖；请先卸载或使用新 ID")

        shutil.copytree(
            src,
            destination,
            ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
        )
        self.discover()
        self._log(
            f"已安装插件 {manifest.id} ({manifest.name})，"
            f"分类={manifest.category.id}/{manifest.category.label}"
        )
        if not enable:
            self._save_state()
            self._notify()
            return OperationResult(True, f"已安装 {manifest.id}（未启用）")

        try:
            self.enable(manifest.id)
        except KernelError as exc:
            record = self.records.get(manifest.id)
            if record:
                record.state = "failed"
                record.message = str(exc)
            self._log(f"插件 {manifest.id} 安装后加载失败（已隔离）: {exc}")
            self._notify()
            return OperationResult(False, str(exc))
        return OperationResult(True, f"已安装并按依赖拓扑顺序启用 {manifest.id}")

    def enable(self, plugin_id: str) -> None:
        if plugin_id not in self.records:
            raise PluginStateError(f"插件 {plugin_id} 未安装，无法启用")
        self._apply(self.enabled | {plugin_id})
        record = self.records[plugin_id]
        if record.state == "failed":
            raise KernelError(record.message or f"插件 {plugin_id} 加载失败")

    def disable(self, plugin_id: str, cascade: bool = False) -> None:
        if plugin_id not in self.records:
            raise PluginStateError(f"插件 {plugin_id} 未安装，无法停用")
        manifests = {pid: rec.manifest for pid, rec in self.records.items()}
        dependents = [
            pid for pid in dependent_closure(plugin_id, manifests) if pid in self.enabled
        ]
        if dependents and not cascade:
            raise DependencyError(
                f"无法停用 {plugin_id}：以下已启用插件依赖它 → {', '.join(dependents)}；"
                f"请先停用依赖方，或使用 cascade=True 级联停用"
            )
        self._apply(self.enabled - (set(dependents) | {plugin_id}))

    def uninstall(self, plugin_id: str) -> None:
        """卸载：先检查反向依赖；有已安装依赖方时阻止卸载并明确提示。"""
        if plugin_id not in self.records:
            raise PluginStateError(f"插件 {plugin_id} 未安装")
        manifests = {pid: rec.manifest for pid, rec in self.records.items()}
        blockers = [pid for pid in reverse_dependents(plugin_id, manifests)]
        if blockers:
            raise DependencyError(
                f"无法卸载 {plugin_id}：以下已安装插件依赖它 → {', '.join(blockers)}；"
                f"请先卸载这些插件"
            )
        if plugin_id in self.enabled:
            self._apply(self.enabled - {plugin_id})
        target = Path(self.records[plugin_id].manifest.path).resolve()
        if target.parent != self.plugins_dir or not target.is_dir():
            raise PluginStateError(f"插件目录异常，拒绝删除: {target}")
        shutil.rmtree(target)
        self.records.pop(plugin_id, None)
        self.enabled.discard(plugin_id)
        self.discover()
        self._save_state()
        self._notify()
        self._log(f"已卸载插件 {plugin_id}，目录与全部运行时副作用均已清理")

    # ==================================================================
    # 生命周期内部实现
    # ==================================================================
    def _apply(self, desired: set[str]) -> None:
        manifests = {pid: rec.manifest for pid, rec in self.records.items()}
        order = resolve_load_order(desired, manifests)  # 可能抛 DependencyError

        # 先卸载不再需要的插件：逆拓扑序，保证依赖方先于被依赖方退出
        for pid in reversed(list(self._load_order)):
            if pid not in order:
                self._unload_one(pid)

        for pid in order:
            if pid in self._loaded:
                self.records[pid].state = "enabled"
                continue
            if not self._load_one(pid):
                desired.discard(pid)

        self.enabled = set(desired) & set(order)
        self._save_state()
        self._notify()

    def _load_one(self, plugin_id: str) -> bool:
        record = self.records[plugin_id]
        manifest = record.manifest
        context = PluginContext(self, manifest)
        handle = None

        missing_services = [s for s in manifest.requires_services if not self.services.has(s)]
        if missing_services:
            return self._fail(
                plugin_id,
                context,
                handle,
                "所需服务尚未注册: "
                + ", ".join(missing_services)
                + "（提供服务的插件可能未安装或加载失败）",
            )

        for dep_id, constraint in manifest.dependencies.items():
            if dep_id not in self._loaded:
                return self._fail(
                    plugin_id, context, handle,
                    f"依赖插件 {dep_id} 未处于激活状态，拒绝加载 {plugin_id}",
                )
            try:
                if not version_satisfies(self.records[dep_id].manifest.version, constraint):
                    return self._fail(
                        plugin_id, context, handle,
                        f"依赖插件 {dep_id} 版本不满足约束 {constraint}",
                    )
            except ManifestError as exc:
                return self._fail(plugin_id, context, handle, f"依赖约束非法: {exc}")

        try:
            plugin_class, handle = load_plugin(manifest, self.project_root)
            instance = plugin_class()
            instance.activate(context)
        except Exception as exc:  # noqa: BLE001 - 激活失败必须隔离并完整回滚
            return self._fail(plugin_id, context, handle, f"加载失败: {exc!r}")

        self._loaded[plugin_id] = (instance, context, handle)
        self._load_order.append(plugin_id)
        record.instance = instance
        record.context = context
        record.state = "enabled"
        record.message = ""
        self._log(f"已启用插件 {plugin_id} ({manifest.name})")
        return True

    def _fail(
        self,
        plugin_id: str,
        context: PluginContext,
        handle: Any,
        message: str,
    ) -> bool:
        for error in context.dispose():
            self._log(f"回滚 {plugin_id} 时出错: {error}（已隔离）")
        if handle is not None:
            try:
                handle.unload()
            except Exception as exc:  # noqa: BLE001
                self._log(f"清理 {plugin_id} 模块时出错: {exc!r}（已隔离）")
        record = self.records[plugin_id]
        record.state = "failed"
        record.message = message
        record.instance = None
        record.context = None
        self._log(f"插件 {plugin_id} 加载失败并已回滚: {message}")
        return False

    def _unload_one(self, plugin_id: str) -> None:
        entry = self._loaded.pop(plugin_id, None)
        if entry is not None:
            instance, context, handle = entry
            for error in context.dispose():
                self._log(f"回滚插件 {plugin_id} 时出错: {error}（已隔离）")
            try:
                instance.deactivate()
            except Exception as exc:  # noqa: BLE001
                self._log(f"插件 {plugin_id} 的 deactivate() 异常: {exc!r}（已隔离）")
            if handle is not None:
                handle.unload()
            if plugin_id in self._load_order:
                self._load_order.remove(plugin_id)
            self._log(f"已停用插件 {plugin_id}，事件/服务/内容/扩展点/模块均已回滚")
        record = self.records.get(plugin_id)
        if record:
            record.state = "disabled"
            record.message = ""
            record.instance = None
            record.context = None
        self._notify()

    # ==================================================================
    # 启用集持久化
    # ==================================================================
    def _load_state(self) -> list[str]:
        if not self.state_path or not self.state_path.is_file():
            return []
        try:
            data = json.loads(self.state_path.read_text(encoding="utf-8"))
            return list(data.get("enabled", []))
        except Exception as exc:  # noqa: BLE001
            self._log(f"启用集读取失败，按空壳启动: {exc!r}")
            return []

    def _save_state(self) -> None:
        if not self.state_path:
            return
        try:
            self.state_path.parent.mkdir(parents=True, exist_ok=True)
            self.state_path.write_text(
                json.dumps({"enabled": sorted(self.enabled)}, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
        except Exception as exc:  # noqa: BLE001
            self._log(f"启用集保存失败: {exc!r}")

    # ==================================================================
    # 自检快照（供 --check 与自动化测试使用）
    # ==================================================================
    def describe(self) -> dict[str, Any]:
        """把内核当前状态导出为可序列化字典，不涉及任何 GUI。"""
        contributions = self.extensions.contributions("main.content")
        return {
            "plugins_dir": str(self.plugins_dir),
            "installed": len(self.records),
            "enabled": sorted(self.enabled),
            "load_order": list(self._load_order),
            "categories": [
                {"id": c.id, "label": c.label, "order": c.order}
                for c in self.categories()
            ],
            "categories_enabled": [c.id for c in self.categories(enabled_only=True)],
            "extension_points": list(self.extensions.points()),
            "contributions": [
                {
                    "id": item.payload.get("id"),
                    "title": item.payload.get("title"),
                    "owner": item.owner,
                    "category": item.payload.get("category"),
                    "order": item.payload.get("order", 100),
                }
                for item in contributions
            ],
            "services": list(self.services.names()),
            "event_handlers": {name: self.bus.handler_count(name) for name in self.bus.events()},
            "event_handler_total": self.bus.handler_count(),
            "plugins": [
                {
                    "id": pid,
                    "version": record.manifest.version,
                    "state": record.state,
                    "message": record.message,
                    "category": record.manifest.category.id,
                    "dependencies": dict(record.manifest.dependencies),
                    "requires_services": list(record.manifest.requires_services),
                }
                for pid, record in sorted(self.records.items())
            ],
            "failed": sorted(
                pid for pid, record in self.records.items() if record.state == "failed"
            ),
            "logs": list(self.logs[-40:]),
        }
