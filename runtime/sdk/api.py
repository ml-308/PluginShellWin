"""插件 SDK —— 第三方插件作者唯一需要 import 的公共协议。

本模块是纯 Python、零 Qt 依赖、零内核实现细节。插件只依赖这里定义的接口，
因此内核实现可以自由演进；插件也拿不到 EventBus / Kernel 等内部对象。
"""
from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any, Callable, Protocol, runtime_checkable


class Plugin(ABC):
    """所有插件的基类。核心与官方插件、第三方插件使用完全相同的这个协议。"""

    @abstractmethod
    def activate(self, context: "PluginContext") -> None:
        """插件激活：在这里注册事件/服务/内容贡献。不要在这里做不可逆操作。"""

    def deactivate(self) -> None:
        """可选：释放 activate 之外的自有资源。上下文副作用由内核负责回滚。"""


@runtime_checkable
class EventsView(Protocol):
    def subscribe(self, event: str, handler: Callable[[str, Any], None]) -> str: ...
    def unsubscribe(self, token: str) -> None: ...
    def publish(self, event: str, payload: Any = None) -> int: ...


@runtime_checkable
class ServicesView(Protocol):
    def register(self, name: str, provider: Any) -> str: ...
    def unregister(self, token: str) -> None: ...
    def require(self, name: str) -> Any: ...
    def get(self, name: str, default: Any = None) -> Any: ...
    def names(self) -> tuple[str, ...]: ...


@runtime_checkable
class ExtensionsView(Protocol):
    def define_point(self, name: str, description: str = "") -> str: ...
    def undefine_point(self, token: str) -> None: ...
    def contribute(self, point: str, payload: dict[str, Any]) -> str: ...
    def withdraw(self, token: str) -> None: ...
    def contributions(self, point: str) -> tuple[Any, ...]: ...
    def points(self) -> tuple[str, ...]: ...


@runtime_checkable
class PluginContext(Protocol):
    """传给 activate() 的上下文。插件的全部能力边界。"""

    plugin_id: str

    @property
    def manifest(self) -> Any: ...
    @property
    def events(self) -> EventsView: ...
    @property
    def services(self) -> ServicesView: ...
    @property
    def extensions(self) -> ExtensionsView: ...
    def log(self, message: str) -> None: ...


def content_panel(
    panel_id: str,
    title: str,
    factory: Callable[[Any], Any],
    category: str | None = None,
    order: int = 100,
    **extra: Any,
) -> dict[str, Any]:
    """构造 main.content 扩展点的内容贡献载荷（不改变其纯数据本质）。

    factory(parent_widget) -> QWidget 在壳子需要渲染时才被调用，因此插件
    模块本身无需在导入期 import Qt，方便无界面环境下做内核级测试。
    category 省略时回落到插件清单声明的分类；order 决定同分类内的展示顺序。
    """
    payload: dict[str, Any] = {
        "id": panel_id,
        "title": title,
        "factory": factory,
        "order": order,
    }
    if category is not None:
        payload["category"] = category
    payload.update(extra)
    return payload
