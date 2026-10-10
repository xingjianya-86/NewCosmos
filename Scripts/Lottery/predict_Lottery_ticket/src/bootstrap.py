"""Bootstrap helpers run early to prepare runtime compatibility (e.g. TensorFlow shims).

This module should be imported as early as possible by entry scripts so that
third-party libraries (Keras) see the compatibility shims at import-time.
"""
from __future__ import annotations

import importlib
import types
import warnings
import logging


def _ensure_ragged_compat() -> None:
    """Ensure tf.ragged.RaggedTensorValue is available or mapped to compat.v1.

    This helps older third-party code that references the deprecated symbol.
    It's best-effort and will not raise on failure. Suppress warnings/logging
    while performing the mapping to avoid emitting deprecation warnings.
    """
    try:
        tf_spec = importlib.util.find_spec('tensorflow')
        if tf_spec is None:
            return
        try:
            import tensorflow as tf  # type: ignore
        except Exception:
            return

        # Temporarily suppress DeprecationWarning and TensorFlow logger output
        tf_logger = logging.getLogger('tensorflow')
        old_level = tf_logger.level
        try:
            with warnings.catch_warnings():
                warnings.filterwarnings('ignore', category=DeprecationWarning, message='.*RaggedTensorValue.*')
                tf_logger.setLevel(logging.ERROR)

                # Ensure tf.ragged namespace exists
                if not hasattr(tf, 'ragged'):
                    tf.ragged = types.SimpleNamespace()

                # If RaggedTensorValue is missing, prefer compat.v1 mapping
                if not hasattr(tf.ragged, 'RaggedTensorValue'):
                    mapped = None
                    if hasattr(tf, 'compat') and hasattr(tf.compat, 'v1'):
                        compat_v1 = getattr(tf.compat, 'v1')
                        if hasattr(compat_v1, 'ragged') and hasattr(compat_v1.ragged, 'RaggedTensorValue'):
                            mapped = compat_v1.ragged.RaggedTensorValue

                    # Fallback: try to use tf.RaggedTensor class if available
                    if mapped is None and hasattr(tf, 'RaggedTensor'):
                        mapped = getattr(tf, 'RaggedTensor')

                    if mapped is not None:
                        setattr(tf.ragged, 'RaggedTensorValue', mapped)
        finally:
            tf_logger.setLevel(old_level)
    except Exception:
        # Swallow all exceptions; this is a best-effort compatibility layer
        return


_ensure_ragged_compat()


def _ensure_keras_shim() -> None:
    """If standalone `keras` is not installed, expose `keras` name pointing to `tf.keras`.

    TensorFlow's lazy loader may attempt `import keras` when accessing `tf.keras`.
    Creating a best-effort shim avoids ImportError in environments where only
    `tensorflow` is installed.
    """
    try:
        import importlib
        import sys
        import types

        # If keras is already importable, nothing to do
        if importlib.util.find_spec("keras") is not None:
            return

        # Try to import tensorflow; if unavailable, skip
        tf_spec = importlib.util.find_spec("tensorflow")
        if tf_spec is None:
            return
        import tensorflow as tf  # type: ignore

        if not hasattr(tf, "keras"):
            return

        # Create a lightweight stub module named 'keras' to satisfy import
        # and basic version checks performed by TensorFlow's lazy loader.
        # The stub intentionally does NOT delegate into tf.keras to avoid
        # triggering recursive import logic in environments where tf.keras
        # initialization itself tries to import `keras`.
        fake = types.ModuleType("keras")
        # Use a non-3.x version string to avoid Keras-3 specific branches.
        fake.__version__ = getattr(tf, "__version__", "0.0.0")
        fake.__name__ = "keras"
        # Add a few common submodule placeholders so attribute access succeeds
        fake.layers = types.ModuleType("keras.layers")
        fake.models = types.ModuleType("keras.models")
        fake.utils = types.ModuleType("keras.utils")
        fake.backend = types.ModuleType("keras.backend")

        sys.modules.setdefault("keras", fake)
    except Exception:
        # Best-effort only
        return


def _ensure_keras_tensor_ops_compat() -> None:
    """Keras 3 (TF >= 2.16) 禁止把 KerasTensor 直接喂给 tf.* 算子。

    原版 modeling.py 以 ``tf.transpose(embedded, perm=(0, 2, 1, 3))`` 交换
    「球位」与「窗口」两个维度，在 TF 2.15 (Keras 2) 下可用；Keras 3 下会抛
    ``ValueError: A KerasTensor cannot be used as input to a TensorFlow function``。

    这里只包一层 ``tf.transpose``：入参含 KerasTensor 时改走 Keras 3 的符号
    算子 ``keras.ops.transpose``，其余情况原样透传给原函数（真实张量行为不变）。
    模型定义本身不做任何改动。
    """
    try:
        import tensorflow as tf  # type: ignore
    except Exception:
        return

    try:
        from keras.src.backend.common.keras_tensor import KerasTensor  # type: ignore
    except Exception:
        return  # 没有 Keras 3（或只有 stub keras）：无需兼容

    original_transpose = tf.transpose

    def _is_keras_tensor(value) -> bool:
        return isinstance(value, KerasTensor)

    def _transpose(value, *args, **kwargs):
        if not (_is_keras_tensor(value)
                or any(_is_keras_tensor(a) for a in args)
                or any(_is_keras_tensor(v) for v in kwargs.values())):
            return original_transpose(value, *args, **kwargs)

        axes = kwargs.get("perm", args[0] if args else None)
        ops = getattr(getattr(tf, "keras", None), "ops", None)
        if ops is None or not hasattr(ops, "transpose"):
            try:
                import keras  # type: ignore
                ops = keras.ops
            except Exception:
                return original_transpose(value, *args, **kwargs)
        try:
            return ops.transpose(value, axes=axes)
        except Exception:
            return original_transpose(value, *args, **kwargs)

    tf.transpose = _transpose


# Run the shims early（先补 keras 名字，再做 Keras 3 算子兼容）
_ensure_keras_shim()
_ensure_keras_tensor_ops_compat()
