from __future__ import annotations
from collections import defaultdict
from datetime import datetime

from .models import ExpenseRow, BudgetSummary, BudgetBreakdown, FraudFlag

# ── Policy thresholds ─────────────────────────────────────────────────────────

MAX_SINGLE_EXPENSE_USD = 200.0      # single expense above this is flagged
MAX_EXPENSES_PER_DAY = 5            # more than this many submissions in one day
DUPLICATE_WINDOW_DAYS = 1           # same amount+category within N days = duplicate
ROUND_NUMBER_MIN_USD = 50.0         # round amounts at or above this are suspicious
BUDGET_OVERRUN_WARN_PCT = 0.90      # flag when 90 %+ of budget is consumed
CATEGORY_DOMINANCE_PCT = 0.70       # flag when one category > 70 % of all spending


def _date(expense: ExpenseRow) -> datetime:
    return datetime.fromisoformat(expense.date[:19])


# ── Individual checks ─────────────────────────────────────────────────────────

def check_duplicates(expenses: list[ExpenseRow]) -> list[FraudFlag]:
    """Same amount + same category submitted within DUPLICATE_WINDOW_DAYS."""
    flags: list[FraudFlag] = []
    groups: dict[tuple, list[ExpenseRow]] = defaultdict(list)
    for e in expenses:
        groups[(e.category_name, round(e.amount, 2))].append(e)

    for (category, amount), group in groups.items():
        if len(group) < 2:
            continue
        group.sort(key=lambda x: x.date)
        for i in range(len(group) - 1):
            delta = abs((_date(group[i + 1]) - _date(group[i])).days)
            if delta <= DUPLICATE_WINDOW_DAYS:
                flags.append(FraudFlag(
                    rule="duplicate_receipt",
                    severity="HIGH",
                    description=(
                        f"${amount:.2f} in '{category}' submitted on "
                        f"{group[i].date[:10]} and {group[i+1].date[:10]} — "
                        f"possible double-submission"
                    ),
                    expense_ids=[group[i].id, group[i + 1].id],
                ))
    return flags


def check_policy_violations(expenses: list[ExpenseRow]) -> list[FraudFlag]:
    """Single expense exceeding the per-transaction policy limit."""
    return [
        FraudFlag(
            rule="out_of_policy_amount",
            severity="MEDIUM",
            description=(
                f"${e.amount:.2f} in '{e.category_name}' on {e.date[:10]} "
                f"exceeds the ${MAX_SINGLE_EXPENSE_USD:.0f} per-transaction limit"
            ),
            expense_ids=[e.id],
        )
        for e in expenses
        if e.amount > MAX_SINGLE_EXPENSE_USD
    ]


def check_round_numbers(expenses: list[ExpenseRow]) -> list[FraudFlag]:
    """Suspiciously round amounts — may indicate estimated rather than actual costs."""
    flags: list[FraudFlag] = []
    for e in expenses:
        if e.amount < ROUND_NUMBER_MIN_USD:
            continue
        cents = round(e.amount * 100)
        if cents % 5000 == 0:                          # multiple of $50
            flags.append(FraudFlag(
                rule="round_number",
                severity="LOW",
                description=(
                    f"${e.amount:.0f} in '{e.category_name}' on {e.date[:10]} — "
                    "suspiciously round amount may indicate an estimate, not an actual receipt"
                ),
                expense_ids=[e.id],
            ))
    return flags


def check_velocity(expenses: list[ExpenseRow]) -> list[FraudFlag]:
    """Too many submissions on a single day."""
    by_date: dict[str, list[ExpenseRow]] = defaultdict(list)
    for e in expenses:
        by_date[e.date[:10]].append(e)

    return [
        FraudFlag(
            rule="velocity_anomaly",
            severity="MEDIUM",
            description=(
                f"{len(day_expenses)} expenses submitted on {date} "
                f"(policy limit: {MAX_EXPENSES_PER_DAY}/day)"
            ),
            expense_ids=[e.id for e in day_expenses],
        )
        for date, day_expenses in by_date.items()
        if len(day_expenses) > MAX_EXPENSES_PER_DAY
    ]


def check_budget_health(breakdown: BudgetBreakdown) -> list[FraudFlag]:
    """Budget overrun, near-limit warning, and category dominance."""
    flags: list[FraudFlag] = []
    b = breakdown.budget

    if b.is_over_budget:
        flags.append(FraudFlag(
            rule="budget_overrun",
            severity="HIGH",
            description=(
                f"Budget exceeded: ${b.total_spent:.2f} spent against "
                f"${b.limit:.2f} limit — ${abs(b.remaining):.2f} over"
            ),
        ))
    elif b.limit > 0 and b.total_spent / b.limit >= BUDGET_OVERRUN_WARN_PCT:
        flags.append(FraudFlag(
            rule="budget_near_limit",
            severity="LOW",
            description=(
                f"{b.total_spent / b.limit * 100:.0f}% of the ${b.limit:.2f} "
                f"budget consumed — ${b.remaining:.2f} remaining"
            ),
        ))

    if b.total_spent > 0:
        for cat in breakdown.categories:
            ratio = cat.total_spent / b.total_spent
            if ratio >= CATEGORY_DOMINANCE_PCT:
                flags.append(FraudFlag(
                    rule="category_dominance",
                    severity="LOW",
                    description=(
                        f"'{cat.category_name}' accounts for "
                        f"{ratio * 100:.0f}% of total spending (${cat.total_spent:.2f})"
                    ),
                ))

    return flags
