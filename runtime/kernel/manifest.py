"""插件清单（manifest.json）的解析、校验与版本约束求值。

设计要点：清单是插件的**静态契约**。插件在运行期能做什么（订阅/发布哪些事件、
注册哪些服务、向哪些扩展点贡献内容、属于哪个分类）必须全部在这里声明，
内核通过 PluginContext 强制执行。
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from .errors import ManifestError

KERNEL_API_VERSION = "1.0"

_ID_RE = re.compile(r"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$")
_VERSION_RE = re.compile(r"^\d+(?:\.\d+)*$")
_CONSTRAINT_RE = re.compile(r"^(>=|<=|==|!=|>|<|=|\*)?\s*(\d+(?:\.\d+)*|\*)$")
_IDENT_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


def parse_version(text: str) -> tuple[int, ...]:
    value = (text or "").strip()
    if not _VERSION_RE.match(value):
        raise ManifestError(f"非法版本号 {value!r}（应形如 1.0.0）")
    return tuple(int(part) for part in value.split("."))


def _aligned(a: tuple[int, ...], b: tuple[int, ...]):
    size = max(len(a), len(b))
    return a + (0,) * (size - len(a)), b + (0,) * (size - len(b))


def version_satisfies(version: str, constraint: str) -> bool:
    """判断 version 是否满足 constraint（支持 >= <= == != > < = 与 *）。"""
    expr = (constraint or "*").strip()
    if expr in ("", "*"):
        return True
    match = _CONSTRAINT_RE.match(expr)
    if not match:
        raise ManifestError(f"非法版本约束 {constraint!r}")
    op = match.group(1) or "=="
    target = match.group(2)
    if op == "*" or target == "*":
        return True
    if op == "=":
        op = "=="
    left, right = _aligned(parse_version(version), parse_version(target))
    return {
        ">=": left >= right,
        "<=": left <= right,
        "==": left == right,
        "!=": left != right,
        ">": left > right,
        "<": left < right,
    }[op]


@dataclass(frozen=True)
class Category:
    """插件自报的分类元数据。内核不内置任何具体分类 ID。"""

    id: str = "general"
    label: str = "未分类"
    order: int = 100
    icon: str = ""


@dataclass(frozen=True)
class Manifest:
    id: str
    name: str
    version: str
    api: str
    entry: str
    class_name: str
    description: str = ""
    category: Category = field(default_factory=Category)
    dependencies: dict[str, str] = field(default_factory=dict)
    requires_services: tuple[str, ...] = ()
    provides_services: tuple[str, ...] = ()
    subscribes: tuple[str, ...] = ()
    publishes: tuple[str, ...] = ()
    provides_extension_points: tuple[str, ...] = ()
    contributes: dict[str, tuple[dict[str, Any], ...]] = field(default_factory=dict)
    path: Path | None = None

    @property
    def contributed_points(self) -> tuple[str, ...]:
        return tuple(self.contributes.keys())


def _require_str(raw: dict, key: str, where: str) -> str:
    value = raw.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ManifestError(f"{where}: 字段 {key!r} 必须是非空字符串")
    return value.strip()


def _opt_str(raw: dict, key: str, default: str = "") -> str:
    value = raw.get(key, default)
    if not isinstance(value, str):
        raise ManifestError(f"字段 {key!r} 必须是字符串")
    return value


def _opt_str_list(raw: dict, key: str) -> tuple[str, ...]:
    value = raw.get(key, [])
    if not isinstance(value, list) or any(not isinstance(x, str) or not x for x in value):
        raise ManifestError(f"字段 {key!r} 必须是字符串数组")
    if len(set(value)) != len(value):
        raise ManifestError(f"字段 {key!r} 存在重复项")
    return tuple(value)


def _opt_map(raw: dict, key: str) -> dict[str, str]:
    value = raw.get(key, {})
    if not isinstance(value, dict):
        raise ManifestError(f"字段 {key!r} 必须是对象")
    out: dict[str, str] = {}
    for name, constraint in value.items():
        if not isinstance(name, str) or not name or not isinstance(constraint, str):
            raise ManifestError(f"字段 {key!r} 的键值必须是 字符串: 字符串")
        try:
            version_satisfies("1.0.0", constraint)
        except ManifestError as exc:
            raise ManifestError(f"{key}.{name}: {exc}") from exc
        out[name] = constraint
    return out


def _parse_category(raw: dict, where: str) -> Category:
    value = raw.get("category", {})
    if not isinstance(value, dict):
        raise ManifestError(f"{where}: 字段 'category' 必须是对象")
    cid = value.get("id", "general")
    label = value.get("label", "未分类")
    order = value.get("order", 100)
    icon = value.get("icon", "")
    if not isinstance(cid, str) or not _ID_RE.match(cid):
        raise ManifestError(f"{where}: category.id {cid!r} 非法")
    if not isinstance(label, str) or not label.strip():
        raise ManifestError(f"{where}: category.label 必须是非空字符串")
    if not isinstance(order, int) or isinstance(order, bool):
        raise ManifestError(f"{where}: category.order 必须是整数")
    if not isinstance(icon, str):
        raise ManifestError(f"{where}: category.icon 必须是字符串")
    return Category(id=cid, label=label.strip(), order=order, icon=icon)


def _parse_contributes(raw: dict, where: str) -> dict[str, tuple[dict[str, Any], ...]]:
    value = raw.get("contributes", {})
    if not isinstance(value, dict):
        raise ManifestError(f"{where}: 字段 'contributes' 必须是对象")
    out: dict[str, tuple[dict[str, Any], ...]] = {}
    for point, items in value.items():
        if not isinstance(point, str) or not point:
            raise ManifestError(f"{where}: 扩展点名称必须是非空字符串")
        if not isinstance(items, list):
            raise ManifestError(f"{where}: contributes.{point} 必须是数组")
        seen: set[str] = set()
        descriptors: list[dict[str, Any]] = []
        for item in items:
            if not isinstance(item, dict):
                raise ManifestError(f"{where}: contributes.{point} 的元素必须是对象")
            cid = item.get("id")
            if not isinstance(cid, str) or not cid:
                raise ManifestError(f"{where}: contributes.{point} 的元素缺少 id")
            if cid in seen:
                raise ManifestError(f"{where}: contributes.{point} 的 id 重复: {cid}")
            seen.add(cid)
            descriptors.append(dict(item))
        out[point] = tuple(descriptors)
    return out


def parse_manifest(raw: dict[str, Any], plugin_dir: Path | None = None) -> Manifest:
    """把 dict 解析并校验为 Manifest；任何不合法之处都给出明确中文提示。"""
    where = f"清单 {plugin_dir.name}" if plugin_dir else "清单"
    if not isinstance(raw, dict):
        raise ManifestError(f"{where}: 根元素必须是对象")

    plugin_id = _require_str(raw, "id", where)
    if not _ID_RE.match(plugin_id):
        raise ManifestError(f"{where}: id {plugin_id!r} 非法（只允许小写字母/数字/.-_）")
    if plugin_dir is not None and plugin_dir.name != plugin_id:
        raise ManifestError(f"{where}: 目录名 {plugin_dir.name!r} 必须与 id {plugin_id!r} 一致")

    version = _require_str(raw, "version", where)
    parse_version(version)

    api = _require_str(raw, "api", where)
    if api != KERNEL_API_VERSION:
        raise ManifestError(
            f"{where}: 插件协议版本 api={api!r} 与内核 {KERNEL_API_VERSION!r} 不兼容"
        )

    entry = _require_str(raw, "entry", where)
    entry_path = Path(entry)
    if entry_path.is_absolute() or ".." in entry_path.parts:
        raise ManifestError(f"{where}: entry 必须是插件目录内的相对路径")

    class_name = _require_str(raw, "class", where)
    if not _IDENT_RE.match(class_name):
        raise ManifestError(f"{where}: class {class_name!r} 不是合法的类名")

    return Manifest(
        id=plugin_id,
        name=_require_str(raw, "name", where),
        version=version,
        api=api,
        entry=entry,
        class_name=class_name,
        description=_opt_str(raw, "description"),
        category=_parse_category(raw, where),
        dependencies=_opt_map(raw, "dependencies"),
        requires_services=_opt_str_list(raw, "requires_services"),
        provides_services=_opt_str_list(raw, "provides_services"),
        subscribes=_opt_str_list(raw, "subscribes"),
        publishes=_opt_str_list(raw, "publishes"),
        provides_extension_points=_opt_str_list(raw, "provides_extension_points"),
        contributes=_parse_contributes(raw, where),
        path=plugin_dir,
    )


def load_manifest(plugin_dir: Path) -> Manifest:
    path = Path(plugin_dir)
    manifest_file = path / "manifest.json"
    if not manifest_file.is_file():
        raise ManifestError(f"{path.name}: 缺少 manifest.json")
    try:
        raw = json.loads(manifest_file.read_text(encoding="utf-8"))
    except Exception as exc:
        raise ManifestError(f"{manifest_file}: 不是合法 JSON / 无法读取: {exc}") from exc
    return parse_manifest(raw, path)
