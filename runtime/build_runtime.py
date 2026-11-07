"""把插件运行时打包成单文件 PluginRuntime.exe，并做一次协议自检。

    python runtime/build_runtime.py
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
DIST = HERE / "dist"
BUILD = HERE / "build"
NAME = "PluginRuntime"

HIDDEN_IMPORTS = (
    "kernel",
    "kernel.bus",
    "kernel.context",
    "kernel.kernel",
    "kernel.loader",
    "kernel.manifest",
    "kernel.registry",
    "kernel.resolver",
    "kernel.services",
    "sdk",
    "PySide6.QtCore",
    "PySide6.QtGui",
    "PySide6.QtWidgets",
)


def clean_pycache() -> None:
    for path in HERE.rglob("__pycache__"):
        if DIST not in path.parents:
            shutil.rmtree(path, ignore_errors=True)


def build() -> Path:
    command = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm", "--clean", "--onefile", "--console",
        "--name", NAME,
        "--distpath", str(DIST),
        "--workpath", str(BUILD),
        "--specpath", str(BUILD),
    ]
    for module in HIDDEN_IMPORTS:
        command += ["--hidden-import", module]
    command.append(str(HERE / "runtime_main.py"))

    print("BUILD", NAME)
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        print((result.stdout + result.stderr).strip()[-3000:])
        raise SystemExit("RUNTIME_BUILD_FAILED")
    exe = DIST / f"{NAME}.exe"
    if not exe.is_file():
        raise SystemExit("RUNTIME_BUILD_FAILED: exe 未生成")
    print(f"RUNTIME_EXE {exe} ({exe.stat().st_size:,} bytes)")
    return exe


def probe(exe: Path) -> bool:
    """启动运行时 → hello → 关闭，验证协议与 Qt 可用。"""
    env = dict(os.environ)
    runtime_temp = HERE / ".tmp" / "pyi-runtime"
    runtime_temp.mkdir(parents=True, exist_ok=True)
    env["TEMP"] = str(runtime_temp)
    env["TMP"] = str(runtime_temp)
    env["QT_QPA_PLATFORM"] = "offscreen"
    env["PYTHONIOENCODING"] = "utf-8"

    with tempfile.TemporaryDirectory(dir=HERE / ".tmp") as temp:
        plugins = Path(temp) / "plugins"
        process = subprocess.Popen(
            [str(exe), "--plugins", str(plugins)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=env,
        )
        try:
            process.stdin.write(json.dumps({"id": 1, "cmd": "hello"}) + "\n")
            process.stdin.flush()
            line = process.stdout.readline()
            payload = json.loads(line) if line.strip() else {}
            result = payload.get("result", {})
            ok = bool(payload.get("ok")) and result.get("protocol") == "1.0"
            print("HELLO", json.dumps(result, ensure_ascii=False))
            process.stdin.write(json.dumps({"id": 2, "cmd": "shutdown"}) + "\n")
            process.stdin.flush()
            process.wait(timeout=15)
            print("PROBE", "OK" if ok else "FAILED")
            return ok
        finally:
            if process.poll() is None:
                process.kill()


def main() -> int:
    clean_pycache()
    (HERE / ".tmp").mkdir(exist_ok=True)
    exe = build()
    return 0 if probe(exe) else 1


if __name__ == "__main__":
    sys.exit(main())
