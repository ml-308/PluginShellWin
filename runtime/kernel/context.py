"""PluginContext —— 插件唯一能拿到的内核句柄，也是"副作用可逆"的核心。

两层职责：
1) 声明校验：任何订阅/发布/服务注册/扩展点贡献都必须先在 manifest 里静态声明，
   否则直接拒绝。插件拿不到 EventBus / ServiceContainer / Kernel 本身，无法绕过。
2) 撤销账本：每次注册动作都登记一个逆操作。dispose() 逆序执行全部逆操作，
   因此卸载后系统一定回到安装前的状态（订阅、服务、UI 内容、自定义扩展点、模块）。
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable

from .errors import DeclarationError


@dataclass
class _Undo:
    key: str
    label: str
    action: Callable[[], Any]
    active: bool = True


class _EventsView:
    def __init__(self, context: "PluginContext"):
        self._c = context

    def subscribe(self, event: str, handler: Callable[[str, Any], None]) -> str:
        c = self._c
        if event not in c.manifest.subscribes:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.subscribes 声明事件 {event!r}，拒绝订阅"
            )
        token = c._kernel.bus.subscribe(event, handler)
        c._record(token, f"退订事件 {event}", lambda t=token: c._kernel.bus.unsubscribe(t))
        return token

    def unsubscribe(self, token: str) -> None:
        c = self._c
        c._kernel.bus.unsubscribe(token)
        c._cancel(token)

    def publish(self, event: str, payload: Any = None) -> int:
        c = self._c
        if event not in c.manifest.publishes:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.publishes 声明事件 {event!r}，拒绝发布"
            )
        return c._kernel.bus.publish(event, payload, publisher=c.plugin_id)


class _ServicesView:
    def __init__(self, context: "PluginContext"):
        self._c = context

    def register(self, name: str, provider: Any) -> str:
        c = self._c
        if name not in c.manifest.provides_services:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.provides_services 声明服务 {name!r}，拒绝注册"
            )
        token = c._kernel.services.register(name, provider, owner=c.plugin_id)
        c._record(token, f"注销服务 {name}", lambda t=token: c._kernel.services.unregister(t))
        return token

    def unregister(self, token: str) -> None:
        c = self._c
        c._kernel.services.unregister(token)
        c._cancel(token)

    def require(self, name: str) -> Any:
        c = self._c
        if name not in c.manifest.requires_services:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.requires_services 声明服务 {name!r}，拒绝注入"
            )
        return c._kernel.services.require(name)

    def get(self, name: str, default: Any = None) -> Any:
        c = self._c
        if name not in c.manifest.requires_services:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.requires_services 声明服务 {name!r}，拒绝读取"
            )
        return c._kernel.services.get(name, default)

    def names(self) -> tuple[str, ...]:
        return self._c._kernel.services.names()


class _ExtensionsView:
    def __init__(self, context: "PluginContext"):
        self._c = context

    def define_point(self, name: str, description: str = "") -> str:
        c = self._c
        if name not in c.manifest.provides_extension_points:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.provides_extension_points 声明扩展点 "
                f"{name!r}，拒绝定义"
            )
        token = c._kernel.extensions.define_point(name, description, owner=c.plugin_id)
        c._record(token, f"撤销扩展点 {name}", lambda t=token: c._kernel.extensions.undefine_point(t))
        return token

    def undefine_point(self, token: str) -> None:
        c = self._c
        c._kernel.extensions.undefine_point(token)
        c._cancel(token)

    def contribute(self, point: str, payload: Any) -> str:
        c = self._c
        if point not in c.manifest.contributes:
            raise DeclarationError(
                f"插件 {c.plugin_id} 未在 manifest.contributes 声明扩展点 {point!r}，拒绝贡献"
            )
        if not isinstance(payload, dict) or not payload.get("id"):
            raise DeclarationError("贡献内容必须是含 id 的 dict")
        declared_ids = {item.get("id") for item in c.manifest.contributes[point]}
        if payload["id"] not in declared_ids:
            raise DeclarationError(
                f"贡献 id {payload['id']!r} 未在 manifest.contributes.{point} 中静态声明"
            )
        token = c._kernel.extensions.contribute(point, payload, owner=c.plugin_id)
        c._record(token, f"撤回内容 {payload['id']}", lambda t=token: c._kernel.extensions.withdraw(t))
        return token

    def withdraw(self, token: str) -> None:
        c = self._c
        c._kernel.extensions.withdraw(token)
        c._cancel(token)

    def contributions(self, point: str) -> tuple[Any, ...]:
        return self._c._kernel.extensions.contributions(point)

    def points(self) -> tuple[str, ...]:
        return self._c._kernel.extensions.points()


class PluginContext:
    """传给插件 activate() 的上下文。属性视图即插件的全部能力边界。"""

    def __init__(self, kernel: Any, manifest: Any):
        self._kernel = kernel
        self._manifest = manifest
        self._ledger: list[_Undo] = []
        self.plugin_id: str = manifest.id
        self.events = _EventsView(self)
        self.services = _ServicesView(self)
        self.extensions = _ExtensionsView(self)

    @property
    def manifest(self):
        return self._manifest

    def log(self, message: str) -> None:
        self._kernel._log(f"[{self.plugin_id}] {message}")

    def _record(self, key: str, label: str, action: Callable[[], Any]) -> None:
        self._ledger.append(_Undo(key=key, label=label, action=action))

    def _cancel(self, key: str) -> None:
        for item in self._ledger:
            if item.key == key:
                item.active = False

    def dispose(self) -> list[str]:
        """逆序回滚本插件产生的所有副作用，返回（已隔离的）回滚错误列表。"""
        errors: list[str] = []
        for item in reversed(self._ledger):
            if not item.active:
                continue
            item.active = False
            try:
                item.action()
            except Exception as exc:  # noqa: BLE001 - 单个回滚失败不影响其余回滚
                errors.append(f"{item.label}: {exc!r}")
        self._ledger.clear()
        return errors
