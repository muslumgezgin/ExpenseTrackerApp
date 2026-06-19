from __future__ import annotations
from enum import Enum
from pydantic import BaseModel, Field


class Severity(str, Enum):
    HIGH = "HIGH"
    MEDIUM = "MEDIUM"
    LOW = "LOW"
    INFO = "INFO"


class OWASPCategory(str, Enum):
    API1 = "API1:2023 Broken Object Level Authorization"
    API2 = "API2:2023 Broken Authentication"
    API3 = "API3:2023 Broken Object Property Level Authorization"
    API4 = "API4:2023 Unrestricted Resource Consumption"
    API5 = "API5:2023 Broken Function Level Authorization"
    API6 = "API6:2023 Unrestricted Access to Sensitive Business Flows"
    API7 = "API7:2023 Server Side Request Forgery"
    API8 = "API8:2023 Security Misconfiguration"
    API9 = "API9:2023 Improper Inventory Management"
    API10 = "API10:2023 Unsafe Consumption of APIs"


class RawFinding(BaseModel):
    scanner: str
    file: str
    line: int
    snippet: str
    description: str


class TriagedFinding(BaseModel):
    confirmed: bool = Field(
        description="True if genuine security issue; False if false positive (e.g. debug-only guard, test code)"
    )
    title: str = Field(description="Short, descriptive title of the vulnerability")
    severity: Severity
    owasp_category: OWASPCategory
    description: str = Field(description="Full explanation of the risk and its business impact")
    file: str
    line: int
    snippet: str
    recommendation: str = Field(description="Concrete, actionable fix recommendation")


class TriageOutput(BaseModel):
    """Structured output returned by the LLM triage node."""
    findings: list[TriagedFinding]
