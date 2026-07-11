"""
lib/security.py — process-level hardening applied before any untrusted input
is touched. This is defense-in-depth: the .NET wrapper is expected to run
this whole process inside its own sandboxed container (see
dotnet_wrapper/README.md), but the Python side does not trust that and
enforces its own limits too.
"""

from __future__ import annotations
import os
import signal
import sys

try:
    import resource  # POSIX only — not available on Windows
except ImportError:
    resource = None


def apply_resource_limits(max_cpu_seconds: int = 60, max_memory_mb: int = 1536) -> None:
    """Best-effort rlimits — no-op on platforms without the POSIX `resource`
    module (Windows). On Windows, rely on the .NET wrapper's container-level
    limits (Docker/K8s cgroups) instead; this call becomes a documented no-op
    rather than a crash."""
    if resource is None or not hasattr(resource, "RLIMIT_CPU"):
        return
    try:
        resource.setrlimit(resource.RLIMIT_CPU, (max_cpu_seconds, max_cpu_seconds))
        mem_bytes = max_memory_mb * 1024 * 1024
        resource.setrlimit(resource.RLIMIT_AS, (mem_bytes, mem_bytes))
        # Cap file descriptors and forbid core dumps (avoid leaking data to disk).
        resource.setrlimit(resource.RLIMIT_NOFILE, (256, 256))
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    except (ValueError, OSError):
        pass  # container may already impose stricter limits via cgroups


def install_alarm_timeout(seconds: int) -> None:
    """Hard wall-clock timeout as a last resort in addition to the .NET
    wrapper's own Process timeout/kill. SIGALRM is Unix-only."""
    if not hasattr(signal, "SIGALRM"):
        return

    def _handler(signum, frame):  # noqa: ARG001
        sys.stderr.write(f"Aborting: exceeded {seconds}s wall-clock limit.\n")
        os._exit(124)

    signal.signal(signal.SIGALRM, _handler)
    signal.alarm(seconds)


def assert_no_path_traversal(path: str, allowed_root: str) -> str:
    """Resolve a path and refuse it if it escapes allowed_root. Used any time
    a filename arrives from outside the process (upload names, CLI args)."""
    resolved_root = os.path.realpath(allowed_root)
    resolved = os.path.realpath(os.path.join(allowed_root, path))
    if not (resolved == resolved_root or resolved.startswith(resolved_root + os.sep)):
        raise ValueError(f"Refusing path outside sandbox root: {path}")
    return resolved
