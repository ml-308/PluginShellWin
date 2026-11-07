"""依赖解析：拓扑排序、循环检测、缺失依赖与反向依赖计算。"""
from __future__ import annotations

from typing import Mapping

from .errors import DependencyError
from .manifest import Manifest, version_satisfies


def resolve_load_order(
    desired_ids: set[str], manifests: Mapping[str, Manifest]
) -> list[str]:
    """返回按依赖拓扑排序的完整加载序列（自动把依赖闭包纳入）。

    - 依赖插件未安装 -> DependencyError（明确提示缺失的 ID 与约束）
    - 版本约束不满足 -> DependencyError
    - 循环依赖 -> DependencyError
    """
    desired = set(desired_ids)
    for pid in list(desired):
        if pid not in manifests:
            raise DependencyError(f"插件 {pid} 未安装，无法启用")

    # 1) 依赖闭包：启用 B 会自动把其依赖 A 一起纳入
    changed = True
    while changed:
        changed = False
        for pid in sorted(desired):
            manifest = manifests[pid]
            for dep_id, constraint in sorted(manifest.dependencies.items()):
                dep = manifests.get(dep_id)
                if dep is None:
                    raise DependencyError(
                        f"插件 {pid} 缺少依赖插件 {dep_id} ({constraint})；"
                        f"请先安装 {dep_id} 后再启用 {pid}"
                    )
                try:
                    satisfied = version_satisfies(dep.version, constraint)
                except Exception as exc:
                    raise DependencyError(f"插件 {pid} 的依赖约束非法: {exc}") from exc
                if not satisfied:
                    raise DependencyError(
                        f"插件 {pid} 需要 {dep_id} ({constraint})，"
                        f"但已安装版本为 {dep.version}，版本不满足"
                    )
                if dep_id not in desired:
                    desired.add(dep_id)
                    changed = True

    # 2) Kahn 拓扑排序（同层按 ID 排序，保证加载顺序确定可复现）
    indegree = {pid: 0 for pid in desired}
    successors: dict[str, list[str]] = {pid: [] for pid in desired}
    for pid in desired:
        for dep_id in manifests[pid].dependencies:
            if dep_id in desired:
                successors[dep_id].append(pid)
                indegree[pid] += 1

    queue = sorted(pid for pid, degree in indegree.items() if degree == 0)
    order: list[str] = []
    while queue:
        current = queue.pop(0)
        order.append(current)
        for nxt in sorted(successors[current]):
            indegree[nxt] -= 1
            if indegree[nxt] == 0:
                queue.append(nxt)
                queue.sort()

    if len(order) != len(desired):
        loop = sorted(set(desired) - set(order))
        raise DependencyError(f"检测到循环依赖，涉及的插件: {', '.join(loop)}")
    return order


def reverse_dependents(target: str, manifests: Mapping[str, Manifest]) -> tuple[str, ...]:
    """直接依赖 target 的插件 ID（升序）。"""
    return tuple(
        sorted(pid for pid, manifest in manifests.items() if target in manifest.dependencies)
    )


def dependent_closure(target: str, manifests: Mapping[str, Manifest]) -> tuple[str, ...]:
    """target 的全部（传递）依赖方，不含 target 自身。"""
    found: set[str] = set()
    frontier = [target]
    while frontier:
        current = frontier.pop()
        for dep in reverse_dependents(current, manifests):
            if dep != target and dep not in found:
                found.add(dep)
                frontier.append(dep)
    return tuple(sorted(found))
