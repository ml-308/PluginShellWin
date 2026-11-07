"""微内核异常体系。

所有对插件作者可见的错误都继承自 KernelError，调用方（GUI / CLI / 测试）
只需捕获 KernelError 即可给出明确提示，不会因为插件故障而崩溃。
"""
from __future__ import annotations


class KernelError(Exception):
    """所有内核异常的基类。"""


class ManifestError(KernelError):
    """插件清单缺失、格式错误或字段非法。"""


class DependencyError(KernelError):
    """依赖缺失、版本不满足、循环依赖，或卸载/停用被依赖方阻止。"""


class PluginLoadError(KernelError):
    """插件导入、实例化或激活失败（已被内核隔离）。"""


class PluginStateError(KernelError):
    """对插件当前状态执行了非法操作。"""


class DeclarationError(KernelError):
    """插件运行期行为超出了 manifest 的静态声明。"""


class ServiceError(KernelError):
    """服务容器相关错误。"""


class ExtensionError(KernelError):
    """扩展点相关错误。"""
