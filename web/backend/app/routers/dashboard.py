from fastapi import APIRouter, Depends, Query
from sqlalchemy.orm import Session
from sqlalchemy import func, cast, Date
from app.core.database import get_db
from app.models.models import Transaction, TransactionItem, Product
from app.routers.auth import get_current_user
from typing import Optional
from datetime import date, timedelta

router = APIRouter()

@router.get("/summary")
def summary(store_id: Optional[int] = None, db: Session = Depends(get_db), current_user=Depends(get_current_user)):
    q = db.query(Transaction)
    if current_user.role == "admin" and current_user.store_id:
        q = q.filter(Transaction.store_id == current_user.store_id)
    elif store_id:
        q = q.filter(Transaction.store_id == store_id)

    today = date.today()
    total_today = q.filter(cast(Transaction.created_at, Date) == today)\
                   .with_entities(func.sum(Transaction.total_amount)).scalar() or 0
    total_month = q.filter(func.extract("month", Transaction.created_at) == today.month)\
                   .with_entities(func.sum(Transaction.total_amount)).scalar() or 0
    count_today = q.filter(cast(Transaction.created_at, Date) == today).count()

    return {
        "total_today": float(total_today),
        "total_month": float(total_month),
        "transactions_today": count_today,
    }

@router.get("/daily-sales")
def daily_sales(
    days: int = Query(30),
    from_date: Optional[str] = None,
    to_date: Optional[str] = None,
    store_id: Optional[int] = None,
    db: Session = Depends(get_db),
    current_user=Depends(get_current_user),
):
    q = db.query(
        cast(Transaction.created_at, Date).label("date"),
        func.sum(Transaction.total_amount).label("total"),
        func.count(Transaction.id).label("count"),
    )
    if current_user.role == "admin" and current_user.store_id:
        q = q.filter(Transaction.store_id == current_user.store_id)
    elif store_id:
        q = q.filter(Transaction.store_id == store_id)

    start = date.fromisoformat(from_date) if from_date else date.today() - timedelta(days=days)
    end   = date.fromisoformat(to_date)   if to_date   else date.today()

    rows = q.filter(cast(Transaction.created_at, Date) >= start)\
            .filter(cast(Transaction.created_at, Date) <= end)\
            .group_by(cast(Transaction.created_at, Date))\
            .order_by(cast(Transaction.created_at, Date)).all()

    return [{"date": str(r.date), "total": float(r.total), "count": r.count} for r in rows]

@router.get("/top-products")
def top_products(
    limit: int = 10,
    store_id: Optional[int] = None,
    from_date: Optional[str] = None,
    to_date: Optional[str] = None,
    db: Session = Depends(get_db),
    current_user=Depends(get_current_user),
):
    q = db.query(
        Product.name,
        func.sum(TransactionItem.quantity).label("qty"),
        func.sum(TransactionItem.subtotal).label("revenue"),
    ).join(TransactionItem, TransactionItem.product_id == Product.id)\
     .join(Transaction, Transaction.id == TransactionItem.transaction_id)

    if current_user.role == "admin" and current_user.store_id:
        q = q.filter(Transaction.store_id == current_user.store_id)
    elif store_id:
        q = q.filter(Transaction.store_id == store_id)

    if from_date:
        q = q.filter(cast(Transaction.created_at, Date) >= date.fromisoformat(from_date))
    if to_date:
        q = q.filter(cast(Transaction.created_at, Date) <= date.fromisoformat(to_date))

    rows = q.group_by(Product.name).order_by(func.sum(TransactionItem.subtotal).desc()).limit(limit).all()
    return [{"name": r.name, "qty": int(r.qty), "revenue": float(r.revenue)} for r in rows]
