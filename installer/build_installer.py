"""构建原生安装程序 dist/setup.exe（Inno Setup 6）。

    python installer/build_installer.py

前置：已产出
    dist/app/PluginShellWin.exe       （dotnet publish 自包含单文件）
    runtime/dist/PluginRuntime.exe    （python runtime/build_runtime.py）
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist"
BUILD = ROOT / "installer" / "_build"

APP_EXE = ROOT / "dist" / "app" / "PluginShellWin.exe"
RUNTIME_EXE = ROOT / "runtime" / "dist" / "PluginRuntime.exe"
SAMPLES = ROOT.parent / "PluginShell" / "sample_plugins"

OUTPUT_NAME = "setup.exe"
ALIAS_NAME = "PluginShellWin-1.0.0-setup.exe"

SAMPLE_SOURCE = (
    'Source: "{samples}\\*"; DestDir: "{{app}}\\plugins"; '
    "Flags: recursesubdirs createallsubdirs; Tasks: sampleplugins; "
    'Excludes: ".win-state.json,.exe-state.json,.gitignore"'
)


def find_iscc() -> Path | None:
    override = os.environ.get("ISCC_PATH")
    candidates: list[Path] = []
    if override:
        candidates.append(Path(override))
    local = os.environ.get("LOCALAPPDATA")
    if local:
        candidates.append(Path(local) / "Programs" / "Inno Setup 6" / "ISCC.exe")
    candidates.append(Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
                        / "Inno Setup 6" / "ISCC.exe")
    candidates.append(Path(r"C:\Program Files\Inno Setup 6\ISCC.exe"))
    for candidate in candidates:
        if candidate.is_file():
            return candidate
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


def sample_files_line() -> str:
    if not SAMPLES.is_dir():
        print(f"SAMPLES 未找到（{SAMPLES}），安装包将不包含示例插件选项的内容")
        return ""
    count = len([p for p in SAMPLES.iterdir() if (p / "manifest.json").is_file()])
    print(f"SAMPLES 打包 {count} 个示例插件")
    return SAMPLE_SOURCE.format(samples=SAMPLES)


def build() -> Path:
    iscc = find_iscc()
    if iscc is None:
        raise SystemExit(
            "未找到 Inno Setup 6 编译器。\n"
            "  winget install --id JRSoftware.InnoSetup --silent\n"
            "或设置环境变量 ISCC_PATH 指向 ISCC.exe"
        )

    OUT.mkdir(parents=True, exist_ok=True)
    BUILD.mkdir(parents=True, exist_ok=True)

    template = (ROOT / "installer" / "setup.iss.template").read_text(encoding="utf-8")
    script_text = (
        template.replace("@@SRC@@", str(ROOT))
        .replace("@@OUT@@", str(OUT))
        .replace("@@SAMPLE_FILES@@", sample_files_line())
    )
    script = BUILD / "setup.iss"
    script.write_text(script_text, encoding="utf-8-sig")

    print(f"ISCC {iscc}")
    result = subprocess.run([str(iscc), str(script)], capture_output=True, text=True)
    output = (result.stdout + result.stderr).strip()
    if output:
        print(output[-3000:])
    exe = OUT / OUTPUT_NAME
    if result.returncode != 0 or not exe.is_file():
        raise SystemExit(f"ISCC 失败 rc={result.returncode}")

    alias = OUT / ALIAS_NAME
    shutil.copyfile(exe, alias)
    print(f"\nSETUP_EXE   {exe}  {exe.stat().st_size:,} bytes "
          f"({exe.stat().st_size / 1048576:.1f} MB)")
    print(f"SETUP_ALIAS {alias}（推荐分发名，避开 setup.exe 的兼容层 shim）")

    manifest = OUT / "setup-manifest.txt"
    if manifest.is_file():
        entries = len(manifest.read_text(encoding="utf-8", errors="replace").splitlines())
        print(f"MANIFEST_ENTRIES {entries}")
    return exe


def main() -> int:
    preflight()
    build()
    print("BUILD_OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
