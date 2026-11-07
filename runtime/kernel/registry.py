"""扩展点注册表：静态扩展点定义 + 运行期内容贡献。

扩展点由内核定义基础点（main.content），插件也可通过清单声明并定义自己的
扩展点。每一次 define_point / contribute 都返回 token，供卸载时精确撤销。
注册表变化会通知观察者（壳子 UI 据此重绘）。
"""
from __future__ import annotations

import itertools
from dataclasses import dataclass
from typing import Any, Callable

from .errors import ExtensionError


@dataclass(frozen=True)
class Contribution:
    token: str
    point: str
    payload: Any
    owner: str
    seq: int


class ExtensionRegistry:
    BUILTIN_POINTS: dict[str, str] = {
        "main.content": "壳子主内容区",
    }

    def __init__(self, error_sink: Callable[[str], None] | None = None):
        self._points: dict[str, str] = dict(self.BUILTIN_POINTS)
        self._builtin = set(self.BUILTIN_POINTS)
        self._items: dict[str, list[Contribution]] = {}
        self._tokens: dict[str, tuple[str, str]] = {}
        self._point_tokens: dict[str, tuple[str, str]] = {}
        self._observers: dict[str, Callable[[], None]] = {}
        self._counter = itertools.count(1)
        self._error_sink = error_sink

    # ---- 扩展点定义 -------------------------------------------------
    def define_point(self, name: str, description: str = "", owner: str = "") -> str:
        if not name:
            raise ExtensionError("扩展点名称不能为空")
        if name in self._points:
            raise ExtensionError(
                f"扩展点 {name!r} 已存在（{'内核内置' if name in self._builtin else '已被其它插件定义'}）"
            )
        token = f"pt-{next(self._counter)}"
        self._points[name] = description or name
        self._point_tokens[token] = (name, owner)
        self._notify()
        return token

    def undefine_point(self, token: str) -> bool:
        location = self._point_tokens.pop(token, None)
        if location is None:
            return False
        name, _owner = location
        if name in self._builtin:
            self._point_tokens[token] = location
            return False
        # 撤销扩展点时，顺带撤下挂在它上面的全部贡献，避免悬挂内容
        for item in list(self._items.get(name, [])):
            self.withdraw(item.token)
        self._points.pop(name, None)
        self._notify()
        return True

    # ---- 内容贡献 ---------------------------------------------------
    def contribute(self, point: str, payload: Any, owner: str = "") -> str:
        if point not in self._points:
            raise ExtensionError(
                f"扩展点 {point!r} 未定义；插件必须先在 manifest 中声明 provides_extension_points"
            )
        if not isinstance(payload, dict):
            raise ExtensionError("贡献内容必须是一个 dict（main.content 契约见 README）")
        token = f"ext-{next(self._counter)}"
        item = Contribution(
            token=token, point=point, payload=dict(payload), owner=owner, seq=next(self._counter)
        )
        self._items.setdefault(point, []).append(item)
        self._tokens[token] = (point, owner)
        self._notify()
        return token

    def withdraw(self, token: str) -> bool:
        location = self._tokens.pop(token, None)
        if location is None:
            return False
        point, _owner = location
        bucket = self._items.get(point)
        if bucket is not None:
            self._items[point] = [item for item in bucket if item.token != token]
            if not self._items[point]:
                self._items.pop(point, None)
        self._notify()
        return True

    def contributions(self, point: str = "") -> tuple[Contribution, ...]:
        if point:
            return tuple(self._items.get(point, []))
        return tuple(
            sorted(
                (item for bucket in self._items.values() for item in bucket),
                key=lambda item: item.seq,
            )
        )

    def points(self) -> tuple[str, ...]:
        return tuple(sorted(self._points))

    def point_description(self, name: str) -> str:
        return self._points.get(name, "")

    # ---- 变化通知 ---------------------------------------------------
    def on_changed(self, callback: Callable[[], None]) -> str:
        token = f"obs-{next(self._counter)}"
        self._observers[token] = callback
        return token

    def off_changed(self, token: str) -> bool:
        return self._observers.pop(token, None) is not None

    def _notify(self) -> None:
        for callback in list(self._observers.values()):
            try:
                callback()
            except Exception as exc:  # noqa: BLE001 - 观察者异常不得影响内核
                if self._error_sink:
                    self._error_sink(f"扩展点观察者异常: {exc!r}（已隔离）")
