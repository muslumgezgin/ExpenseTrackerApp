from __future__ import annotations
import re
from pathlib import Path
from typing import NamedTuple

from .models import RawFinding

_SKIP_DIRS = {"obj", "bin", ".git", "__pycache__", "node_modules"}


class ScannerRule(NamedTuple):
    name: str
    description: str
    pattern: str
    glob: str  # passed directly to Path.rglob()


RULES: list[ScannerRule] = [
    ScannerRule(
        name="wildcard_cors",
        description="CORS policy allows any origin — cross-origin requests from untrusted domains are accepted",
        pattern=r"SetIsOriginAllowed\s*\(\s*_\s*=>\s*true\s*\)|\.AllowAnyOrigin\s*\(\s*\)",
        glob="*.cs",
    ),
    ScannerRule(
        name="hardcoded_secret",
        description="Potential hardcoded credential or secret in source code",
        pattern=r'(?i)(password|secret|apikey|api_key|connectionstring)\s*[=:]\s*"[^"]{8,}"',
        glob="*.cs",
    ),
    ScannerRule(
        name="sensitive_config_value",
        description="Sensitive identifier committed to appsettings — may expose tenant/subscription to anyone with repo access",
        pattern=r'"(TenantId|ClientSecret|Password|ApiKey|SubscriptionKey|InstrumentationKey)"\s*:\s*"[^"]{6,}"',
        glob="appsettings*.json",
    ),
    ScannerRule(
        name="allowed_hosts_wildcard",
        description="AllowedHosts:'*' disables host-header validation, enabling host-header injection attacks",
        pattern=r'"AllowedHosts"\s*:\s*"\*"',
        glob="appsettings*.json",
    ),
    ScannerRule(
        name="raw_sql_interpolation",
        description="Raw SQL with string interpolation — user-controlled data could reach the query, enabling SQL injection",
        pattern=r"(FromSqlRaw|ExecuteSqlRaw)\s*\(\s*\$\"[^\"]*\{",
        glob="*.cs",
    ),
    ScannerRule(
        name="weak_crypto",
        description="Deprecated cryptographic algorithm (MD5/SHA1/DES) — collision/brute-force attacks are feasible",
        pattern=r"\b(MD5|SHA1|DES|RC2|TripleDES)\s*\.\s*(Create|ComputeHash)",
        glob="*.cs",
    ),
    ScannerRule(
        name="exception_detail_exposure",
        description="exception.Message written directly to the HTTP response — internal details and stack paths may leak to clients",
        pattern=r"\bexception\.Message\b|\bex\.Message\b",
        glob="*.cs",
    ),
]


def scan_directory(source_path: str) -> list[RawFinding]:
    root = Path(source_path)
    findings: list[RawFinding] = []

    for rule in RULES:
        regex = re.compile(rule.pattern, re.IGNORECASE)
        for filepath in root.rglob(rule.glob):
            if any(part in _SKIP_DIRS for part in filepath.parts):
                continue
            try:
                lines = filepath.read_text(encoding="utf-8", errors="ignore").splitlines()
            except OSError:
                continue
            for lineno, line_text in enumerate(lines, start=1):
                if regex.search(line_text):
                    findings.append(
                        RawFinding(
                            scanner=rule.name,
                            file=str(filepath.relative_to(root)),
                            line=lineno,
                            snippet=line_text.strip()[:200],
                            description=rule.description,
                        )
                    )

    findings.extend(_check_missing_auth(root))
    return findings


def _check_missing_auth(root: Path) -> list[RawFinding]:
    """Project-level check: endpoints registered but no auth middleware wired up."""
    cs_files = [
        f for f in root.rglob("*.cs")
        if not any(part in _SKIP_DIRS for part in f.parts)
    ]
    all_source = "\n".join(
        f.read_text(encoding="utf-8", errors="ignore") for f in cs_files
    )

    has_endpoints = bool(re.search(r"\b(MapGet|MapPost|MapPut|MapDelete|MapGroup)\b", all_source))
    has_auth = bool(re.search(
        r"\b(AddAuthentication|AddAuthorization|RequireAuthorization|UseAuthentication|UseAuthorization)\b",
        all_source,
    ))

    if has_endpoints and not has_auth:
        return [
            RawFinding(
                scanner="missing_authentication",
                file="ExpenseTracker.Api/Program.cs",
                line=1,
                snippet="No AddAuthentication / AddAuthorization / RequireAuthorization found in project",
                description="All API endpoints are publicly accessible — no authentication or authorization middleware is configured",
            )
        ]
    return []
