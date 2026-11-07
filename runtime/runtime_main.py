"""PluginRuntime —— 被 C# 宿主以子进程方式启动的插件运行时。

职责边界很窄：加载 PluginShell 插件（py 版协议 1.0），把 main.content 渲染成一个
原生 Qt 窗口，并把窗口句柄（HWND）交给 C# 宿主去 SetParent 嵌入。

协议：stdin/stdout 上的 JSON-Lines（一行一个请求/响应），日志一律走 stderr，
绝不污染 stdout 的协议流。

    PluginRuntime.exe --plugins <dir>
"""
from __future__ import annotations

import json
import os
import queue
import sys
import threading
import traceback
from pathlib import Path

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

PROTOCOL = "1.0"
APP_TITLE = "PluginShellWin"


def log(message: str) -> None:
    """运行时诊断信息只能写 stderr —— stdout 是协议流。"""
    try:
        sys.stderr.write(f"[runtime] {message}\n")
        sys.stderr.flush()
    except Exception:  # noqa: BLE001
        pass


class RuntimeServer:
    def __init__(self, plugins_dir: Path):
        from kernel import Kernel
        from kernel.errors import KernelError  # noqa: F401

        self.plugins_dir = plugins_dir
        self.state_path = plugins_dir / ".win-state.json"
        self.kernel = Kernel(
            plugins_dir=plugins_dir,
            project_root=HERE,
            state_path=self.state_path,
            log_sink=log,
        )
        self.app = None
        self.window = None
        self.inbox: "queue.Queue[str | None]" = queue.Queue()
        self.had_state = self.state_path.exists()

    # ------------------------------------------------------------------
    def boot(self) -> None:
        self.kernel.bootstrap()
        if not self.had_state and self.kernel.records and not self.kernel.enabled:
            for plugin_id in sorted(self.kernel.records):
                try:
                    self.kernel.enable(plugin_id)
                except Exception as exc:  # noqa: BLE001
                    log(f"自动启用 {plugin_id} 失败: {exc!r}")
            log(f"首次采用插件目录：已启用 {len(self.kernel.enabled)} 个")

    # ------------------------------------------------------------------
    def serve(self) -> int:
        from PySide6.QtCore import QTimer
        from PySide6.QtWidgets import QApplication

        self.app = QApplication(sys.argv[:1])
        self.boot()
        log(
            f"就绪 protocol={PROTOCOL} installed={len(self.kernel.records)} "
            f"enabled={len(self.kernel.enabled)}"
        )

        threading.Thread(target=self._read_stdin, daemon=True).start()
        self._timer = QTimer()
        self._timer.timeout.connect(self._pump)
        self._timer.start(15)
        code = self.app.exec()
        try:
            self.kernel.shutdown()
        except Exception:  # noqa: BLE001
            pass
        return code

    def _read_stdin(self) -> None:
        stream = sys.stdin
        if stream is None:
            self.inbox.put(None)
            return
        try:
            for line in stream:
                line = line.strip()
                if line:
                    self.inbox.put(line)
        except Exception:  # noqa: BLE001
            pass
        self.inbox.put(None)

    def _pump(self) -> None:
        while True:
            try:
                line = self.inbox.get_nowait()
            except queue.Empty:
                return
            if line is None:
                self.app.quit()
                return
            try:
                request = json.loads(line)
            except Exception as exc:  # noqa: BLE001
                self._write({"id": None, "ok": False, "error": f"非法 JSON: {exc}"})
                continue
            self._dispatch(request)

    # ------------------------------------------------------------------
    def _dispatch(self, request: dict) -> None:
        request_id = request.get("id")
        command = str(request.get("cmd", ""))
        handler = getattr(self, f"_cmd_{command}", None)
        if handler is None:
            self._write({"id": request_id, "ok": False, "error": f"未知命令: {command}"})
            return
        try:
            result = handler(request) or {}
            self._write({"id": request_id, "ok": True, "result": result})
        except Exception as exc:  # noqa: BLE001 - 任何插件/命令异常都不能让运行时退出
            log(f"命令 {command} 失败: {exc!r}\n{traceback.format_exc(limit=4)}")
            self._write({"id": request_id, "ok": False, "error": str(exc)})

    def _write(self, payload: dict) -> None:
        try:
            sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
            sys.stdout.flush()
        except Exception:  # noqa: BLE001
            pass

    # ------------------------------------------------------------------
    # 命令实现
    # ------------------------------------------------------------------
    def _cmd_hello(self, request: dict) -> dict:
        import PySide6

        return {
            "protocol": PROTOCOL,
            "app": APP_TITLE,
            "qt": PySide6.__version__,
            "python": sys.version.split()[0],
            "pid": os.getpid(),
            "pluginsDir": str(self.plugins_dir),
        }

    def _cmd_plugins(self, request: dict) -> dict:
        items = []
        for plugin_id, record in sorted(self.kernel.records.items()):
            manifest = record.manifest
            items.append(
                {
                    "id": plugin_id,
                    "name": manifest.name,
                    "version": manifest.version,
                    "state": record.state,
                    "message": record.message,
                    "categoryId": manifest.category.id,
                    "categoryLabel": manifest.category.label,
                    "description": manifest.description,
                    "dependencies": dict(manifest.dependencies),
                    "requiresServices": list(manifest.requires_services),
                }
            )
        return {"plugins": items, "enabled": sorted(self.kernel.enabled)}

    def _cmd_categories(self, request: dict) -> dict:
        contributions = self.kernel.extensions.contributions("main.content")
        counts: dict[str, int] = {}
        for item in contributions:
            category = self._category_of(item)
            counts[category] = counts.get(category, 0) + 1
        return {
            "categories": [
                {
                    "id": category.id,
                    "label": category.label,
                    "order": category.order,
                    "count": counts.get(category.id, 0),
                }
                for category in self.kernel.categories(enabled_only=True)
            ],
            "total": len(contributions),
        }

    def _run(self, action, success_message: str) -> dict:
        """业务拒绝（依赖保护、清单非法……）是正常结果，不是协议错误。"""
        from kernel.errors import KernelError

        try:
            action()
        except KernelError as exc:
            return {"ok": False, "message": str(exc)}
        return {"ok": True, "message": success_message}

    def _cmd_install(self, request: dict) -> dict:
        from kernel.errors import KernelError

        try:
            result = self.kernel.install(Path(str(request["path"])))
        except KernelError as exc:
            return {"ok": False, "message": str(exc)}
        return {"ok": result.ok, "message": result.message}

    def _cmd_enable(self, request: dict) -> dict:
        plugin_id = str(request["pluginId"])
        return self._run(lambda: self.kernel.enable(plugin_id), "已启用")

    def _cmd_disable(self, request: dict) -> dict:
        plugin_id = str(request["pluginId"])
        return self._run(lambda: self.kernel.disable(plugin_id), "已禁用")

    def _cmd_uninstall(self, request: dict) -> dict:
        plugin_id = str(request["pluginId"])
        return self._run(lambda: self.kernel.uninstall(plugin_id), "已卸载")

    def _cmd_logs(self, request: dict) -> dict:
        limit = int(request.get("limit", 120))
        return {"logs": list(self.kernel.logs[-limit:])}

    def _cmd_show(self, request: dict) -> dict:
        from PySide6.QtCore import Qt
        from PySide6.QtWidgets import QGroupBox, QLabel, QVBoxLayout, QWidget

        category = request.get("category") or None
        contributions = [
            item
            for item in self.kernel.extensions.contributions("main.content")
            if category is None or self._category_of(item) == category
        ]

        if self.window is None:
            window = QWidget(None)
            window.setWindowFlags(
                Qt.WindowType.Window | Qt.WindowType.FramelessWindowHint
            )
            window.resize(int(request.get("width", 820)), int(request.get("height", 520)))
            window.setLayout(QVBoxLayout(window))
            self.window = window

        layout = self.window.layout()
        while layout.count():
            item = layout.takeAt(0)
            widget = item.widget()
            if widget is not None:
                widget.setParent(None)
                widget.deleteLater()

        for contribution in contributions:
            payload = contribution.payload
            box = QGroupBox(str(payload.get("title") or contribution.owner), self.window)
            box_layout = QVBoxLayout(box)
            try:
                factory = payload.get("factory")
                widget = factory(box) if callable(factory) else None
                if widget is None:
                    raise RuntimeError("插件未返回可渲染的控件")
                box_layout.addWidget(widget)
            except Exception as exc:  # noqa: BLE001
                box_layout.addWidget(QLabel(f"[插件渲染失败，已隔离] {exc!r}", box))
            layout.addWidget(box)

        if not contributions:
            placeholder = QLabel("当前分类暂无内容", self.window)
            placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(placeholder)

        self.window.show()
        self.app.processEvents()
        return {"hwnd": int(self.window.winId()), "count": len(contributions)}

    def _cmd_resize(self, request: dict) -> dict:
        if self.window is not None:
            self.window.resize(int(request["width"]), int(request["height"]))
            self.app.processEvents()
        return {"ok": True}

    def _cmd_shutdown(self, request: dict) -> dict:
        self.app.quit()
        return {"ok": True}

    # ------------------------------------------------------------------
    def _category_of(self, contribution) -> str:
        declared = contribution.payload.get("category")
        if declared:
            return str(declared)
        record = self.kernel.records.get(contribution.owner)
        return record.manifest.category.id if record else "general"


def main(argv: list[str]) -> int:
    plugins_dir = None
    index = 0
    while index < len(argv):
        if argv[index] == "--plugins" and index + 1 < len(argv):
            plugins_dir = Path(argv[index + 1]).expanduser().resolve()
            index += 2
            continue
        index += 1
    if plugins_dir is None:
        plugins_dir = HERE / "plugins"
    plugins_dir.mkdir(parents=True, exist_ok=True)
    return RuntimeServer(plugins_dir).serve()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
