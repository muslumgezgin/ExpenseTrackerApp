from __future__ import annotations
from typing import Annotated, TypedDict
import operator

from pydantic import BaseModel, Field, ConfigDict


# ── API response shapes (match ExpenseTracker DTO camelCase serialisation) ────

class ExpenseRow(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    id: int
    amount: float
    date: str                                          # ISO-8601 from .NET DateTime
    description: str | None = None
    category_name: str = Field(alias="categoryName")


class BudgetSummary(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    id: int
    month: int
    year: int
    limit: float
    total_spent: float = Field(alias="totalSpent")
    remaining: float
    is_over_budget: bool = Field(alias="isOverBudget")


class CategorySpending(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    category_id: int = Field(alias="categoryId")
    category_name: str = Field(alias="categoryName")
    total_spent: float = Field(alias="totalSpent")


class BudgetBreakdown(BaseModel):
    budget: BudgetSummary
    categories: list[CategorySpending]


# ── Internal analysis types ───────────────────────────────────────────────────

class FraudFlag(BaseModel):
    rule: str
    severity: str = Field(description="HIGH | MEDIUM | LOW")
    description: str
    expense_ids: list[int] = Field(default_factory=list)


class FraudFinding(BaseModel):
    confirmed: bool = Field(description="True if genuine risk; False if benign edge case")
    title: str = Field(description="Short title, max 8 words")
    severity: str
    description: str = Field(description="Business risk explanation")
    expense_ids: list[int]
    recommendation: str = Field(description="Concrete remediation action")


class FraudAnalysisOutput(BaseModel):
    """Structured output returned by the LLM reasoning node."""
    findings: list[FraudFinding]


# ── LangGraph state ───────────────────────────────────────────────────────────

class FraudState(TypedDict):
    month: int
    year: int
    api_base_url: str
    expenses: list[dict]
    breakdown: dict | None                             # BudgetBreakdownDto or None
    flags: Annotated[list[FraudFlag], operator.add]   # parallel nodes append here
    llm_findings: list[FraudFinding]
    report_markdown: str
