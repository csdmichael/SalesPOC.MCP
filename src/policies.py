import json
import os
import re
import threading
import time
from collections import defaultdict, deque
from typing import Any


class PolicyError(ValueError):
    pass


NEGATIVE_PROMPT_PATTERNS = [
    r"ignore\s+(all\s+)?(previous|prior)\s+instructions",
    r"bypass\s+(safety|policy|guardrails?)",
    r"jailbreak",
    r"system\s+prompt",
    r"do\s+anything\s+now",
]

HARMFUL_HATE_PATTERNS = [
    r"\b(hate|racist|sexist|genocide)\b",
    r"\b(kill|murder|bomb|terrorist|attack)\b",
    r"\b(self\s*harm|suicide)\b",
]


class ToolRateLimiter:
    def __init__(self, max_requests: int, window_seconds: int = 60):
        self.max_requests = max_requests
        self.window_seconds = window_seconds
        self._requests: dict[str, deque[float]] = defaultdict(deque)
        self._lock = threading.Lock()

    def allow(self, key: str) -> bool:
        now = time.monotonic()
        with self._lock:
            queue = self._requests[key]
            while queue and now - queue[0] > self.window_seconds:
                queue.popleft()
            if len(queue) >= self.max_requests:
                return False
            queue.append(now)
            return True


MAX_OUTPUT_CHARS = int(os.getenv("POLICY_MAX_OUTPUT_CHARS", "12000"))
RATE_LIMIT_PER_MINUTE = int(os.getenv("POLICY_RATE_LIMIT_PER_MINUTE", "60"))

_rate_limiter = ToolRateLimiter(max_requests=RATE_LIMIT_PER_MINUTE, window_seconds=60)


def _find_policy_violation(text: str) -> str | None:
    lowered = (text or "").lower()
    for pattern in NEGATIVE_PROMPT_PATTERNS:
        if re.search(pattern, lowered, flags=re.IGNORECASE):
            return "prompt-injection"
    for pattern in HARMFUL_HATE_PATTERNS:
        if re.search(pattern, lowered, flags=re.IGNORECASE):
            return "harmful-or-hateful-content"
    return None


def enforce_text_policy(value: str | None, field_name: str) -> None:
    if value is None:
        return
    violation = _find_policy_violation(value)
    if violation:
        raise PolicyError(f"Blocked by content policy ({violation}) in field '{field_name}'.")


def enforce_rate_limit(tool_name: str) -> None:
    if not _rate_limiter.allow(tool_name):
        raise PolicyError("Rate limit exceeded for this tool. Please retry shortly.")


def enforce_output_policy(payload: Any) -> Any:
    serialized = json.dumps(payload, ensure_ascii=False, default=str)

    violation = _find_policy_violation(serialized)
    if violation:
        raise PolicyError(f"Blocked by content policy ({violation}) in tool output.")

    if len(serialized) <= MAX_OUTPUT_CHARS:
        return payload

    return {
        "truncated": True,
        "max_output_chars": MAX_OUTPUT_CHARS,
        "content": serialized[:MAX_OUTPUT_CHARS],
    }
