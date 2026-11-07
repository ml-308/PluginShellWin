"""事件总线：插件间通信通道之一（另一个是服务容器）。

插件不能拿到 EventBus 本身，只能通过 PluginContext 的受限视图访问；
每个订阅都有 token，供卸载时精确退订。回调异常被隔离，不会拖垮发布者。
"""
from __future__ import annotations

import itertools
from collections import defaultdict
from typing import Any, Callable

EventHandler = Callable[[str, Any], None]


class EventBus:
    def __init__(self, error_sink: Callable[[str], None] | None = None):
        self._handlers: dict[str, dict[str, EventHandler]] = defaultdict(dict)
        self._tokens: dict[str, str] = {}
        self._counter = itertools.count(1)
        self._error_sink = error_sink

    def subscribe(self, event: str, handler: EventHandler) -> str:
        if not event or not callable(handler):
            raise ValueError("subscribe 需要非空事件名与可调用处理器")
        token = f"evt-{next(self._counter)}"
        self._handlers[event][token] = handler
        self._tokens[token] = event
        return token

    def unsubscribe(self, token: str) -> bool:
        event = self._tokens.pop(token, None)
        if event is None:
            return False
        bucket = self._handlers.get(event)
        if bucket is not None:
            bucket.pop(token, None)
            if not bucket:
                self._handlers.pop(event, None)
        return True

    def publish(self, event: str, payload: Any = None, publisher: str = "") -> int:
        handlers = list(self._handlers.get(event, {}).items())
        for _token, handler in handlers:
            try:
                handler(event, payload)
            except Exception as exc:  # noqa: BLE001 - 故障隔离是本设计的核心要求
                message = (
                    f"事件 {event} 的处理器异常（发布者 {publisher or '宿主'}）: "
                    f"{exc!r}；已隔离，未影响壳子"
                )
                if self._error_sink:
                    self._error_sink(message)
        return len(handlers)

    def handler_count(self, event: str | None = None) -> int:
        if event is not None:
            return len(self._handlers.get(event, {}))
        return sum(len(bucket) for bucket in self._handlers.values())

    def events(self) -> tuple[str, ...]:
        return tuple(sorted(self._handlers.keys()))
