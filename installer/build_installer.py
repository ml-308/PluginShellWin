"""构建原生安装程序 dist/PluginShellWin-Setup-1.1.0.exe（Inno Setup 6，统一套件）。

    python installer/build_installer.py

前置：已产出
    dist/app/PluginShellWin.exe       （dotnet publish 自包含单文件）
    runtime/dist/PluginRuntime.exe    （python runtime/build_runtime.py）

说明：安装向导的页面顺序、任务项、文案、卸载行为由 installer/common.iss 统一定义；
本目录的 setup.iss 只声明应用身份与文件清单。示例插件选项由 setup.iss 里的
`Tasks: samples` 驱动（样例源在 ../PluginShell/sample_plugins）。
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist"

APP_EXE = ROOT / "dist" / "app" / "PluginShellWin.exe"
RUNTIME_EXE = ROOT / "runtime" / "dist" / "PluginRuntime.exe"

ISCC_CANDIDATES = [
    Path(r"C:\Users\ThinkPad\AppData\Local\Programs\Inno Setup 6\ISCC.exe"),
    Path(r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"),
    Path(r"C:\Program Files\Inno Setup 6\ISCC.exe"),
]


def find_iscc() -> Path | None:
    for p in ISCC_CANDIDATES:
        if p.is_file():
            return p
    return None


def preflight() -> None:
    missing = [str(p) for p in (APP_EXE, RUNTIME_EXE) if not p.is_file()]
    if missing:
        raise SystemExit(
            "缺少安装源文件：\n  " + "\n  ".join(missing)
            + "\n\n请先执行：\n"
            "  dotnet publish src/PluginShellWin.App -c Release -r win-x64 "
            "--self-contained true -p:PublishSingleFile=true -o dist/app\n"
            "  python runtime/build_runtime.py"
        )


def build() -> Path:
    iscc = find_iscc()
    if iscc is None:
        raise SystemExit(
            "未找到 Inno Setup 6 编译器。\n"
            "  winget install --id JRSoftware.InnoSetup --silent"
        )
    OUT.mkdir(parents=True, exist_ok=True)
    script = ROOT / "installer" / "setup.iss"
    result = subprocess.run([str(iscc), str(script)], capture_output=True, text=True)
    out = (result.stdout + result.stderr).strip()
    if out:
        print(out[-3000:])
    exe = OUT / "PluginShellWin-Setup-1.1.0.exe"
    if result.returncode != 0 or not exe.is_file():
        raise SystemExit(f"ISCC 失败 rc={result.returncode}")
    print(f"\nSETUP_EXE   {exe}  {exe.stat().st_size:,} bytes "
          f"({exe.stat().st_size / 1048576:.1f} MB)")
    return exe


def main() -> int:
    preflight()
    build()
    print("BUILD_OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
