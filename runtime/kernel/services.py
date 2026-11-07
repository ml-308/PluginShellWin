"""服务容器：插件间通信通道之二，用于共享有状态的对象 / API。

服务名全局唯一，注册者身份被记录；注销只接受注册时返回的 token，
避免插件误删别人的服务。所有 token 都会被 PluginContext 记入撤销账本。
"""
from __future__ import annotations

import itertools
from typing import Any

from .errors import ServiceError


class ServiceContainer:
    def __init__(self):
        self._items: dict[str, Any] = {}
        self._owners: dict[str, str] = {}
        self._tokens: dict[str, tuple[str, str]] = {}
        self._counter = itertools.count(1)

    def register(self, name: str, provider: Any, owner: str = "") -> str:
        if not name:
            raise ServiceError("服务名不能为空")
        if name in self._items:
            raise ServiceError(
                f"服务 {name!r} 已由插件 {self._owners.get(name) or '宿主'} 注册，拒绝重复注册"
            )
        token = f"svc-{next(self._counter)}"
        self._items[name] = provider
        self._owners[name] = owner
        self._tokens[token] = (name, owner)
        return token

    def unregister(self, token: str) -> bool:
        location = self._tokens.pop(token, None)
        if location is None:
            return False
        name, _owner = location
        self._items.pop(name, None)
        self._owners.pop(name, None)
        return True

    def get(self, name: str, default: Any = None) -> Any:
        return self._items.get(name, default)

    def require(self, name: str) -> Any:
        if name not in self._items:
            raise ServiceError(
                f"所需服务 {name!r} 尚未注册；请确认提供该服务的插件已安装并成功启用"
            )
        return self._items[name]

    def has(self, name: str) -> bool:
        return name in self._items

    def names(self) -> tuple[str, ...]:
        return tuple(sorted(self._items))

    def owner(self, name: str) -> str | None:
        return self._owners.get(name)
