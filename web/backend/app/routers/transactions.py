from fastapi import APIRouter, Depends, Query
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session
from sqlalchemy import func, or_
from app.core.database import get_db
from app.core.redis import push_transaction_event
from app.models.models import Transaction, TransactionItem, Product, Terminal, Store
from app.routers.auth import get_current_user
from pydantic import BaseModel
from typing import List, Optional
from datetime import datetime
import io, openpyxl

router = APIRouter()

class ItemIn(BaseModel):
    barcode: str
    quantity: int
    unit_price: float

class TransactionIn(BaseModel):
    terminal_id: int
    store_id: int
    payment_method: str
    created_at: Optional[datetime] = None
    items: List[ItemIn]

@router.post("/sync")
async def sync_transaction(body: TransactionIn, db: Session = Depends(get_db)):
    """Store server calls this to sync a completed transaction."""
    store = db.query(Store).filter(Store.id == body.store_id).first()
    if store is None:
        store = db.query(Store).first()
    store_id = store.id if store else body.store_id

    terminal = db.query(Terminal).filter(Terminal.id == body.terminal_id).first()
    if terminal is None:
        terminal = db.query(Terminal).filter(Terminal.store_id == store_id).first()
    if terminal is None:
        terminal = Terminal(store_id=store_id, hardware_version="1.0", status="active")
        db.add(terminal)
        db.flush()
    terminal_id = terminal.id

    total = sum(i.unit_price * i.quantity for i in body.items)
    tx = Transaction(
        terminal_id=terminal_id,
        store_id=store_id,
        total_amount=total,
        payment_method=body.payment_method,
        # always use cloud server time — ignore created_at from store client
    )
    db.add(tx)
    db.flush()
    for item in body.items:
        product = db.query(Product).filter(Product.barcode == item.barcode).first()
        if product is None:
            continue
        db.add(TransactionItem(
            transaction_id=tx.id,
            product_id=product.id,
            quantity=item.quantity,
            unit_price=item.unit_price,
            subtotal=item.unit_price * item.quantity,
        ))
    db.commit()
    await push_transaction_event({
        "transaction_id": str(tx.id),
        "store_id": str(body.store_id),
        "total": str(total),
        "method": body.payment_method,
    })
    return {"transaction_id": tx.id}

def _suspicion_score(tx: Transaction) -> int:
    score = 0
    amount = float(tx.total_amount)
    hour = tx.created_at.hour if tx.created_at else 12
    if amount > 80:   score += 30
    elif amount > 40: score += 15
    if 0 <= hour <= 5:    score += 40
    elif 22 <= hour <= 23: score += 10
    if tx.payment_method == "cash" and amount > 30: score += 20
    return min(score, 100)

def _build_query(db, current_user, store_id, search, method, suspicion, from_date, to_date):
    q = db.query(Transaction, Store.name.label("store_name"))\
          .join(Store, Store.id == Transaction.store_id, isouter=True)

    # Role-based store filter
    if current_user.role == "admin" and current_user.store_id:
        q = q.filter(Transaction.store_id == current_user.store_id)
    elif store_id:
        q = q.filter(Transaction.store_id == store_id)

    # Search by ID or store name
    if search:
        try:
            tid = int(search)
            q = q.filter(Transaction.id == tid)
        except ValueError:
            q = q.filter(Store.name.ilike(f"%{search}%"))

    # Payment method
    if method and method != "all":
        q = q.filter(Transaction.payment_method == method)

    # Date range
    if from_date:
        q = q.filter(Transaction.created_at >= from_date)
    if to_date:
        q = q.filter(Transaction.created_at <= to_date)

    return q

@router.get("/")
def list_transactions(
    store_id: Optional[int] = None,
    search: Optional[str] = None,
    method: Optional[str] = None,
    suspicion: Optional[str] = None,
    from_date: Optional[datetime] = None,
    to_date: Optional[datetime] = None,
    page: int = Query(1, ge=1),
    per_page: int = Query(50, ge=1, le=200),
    db: Session = Depends(get_db),
    current_user=Depends(get_current_user),
):
    q = _build_query(db, current_user, store_id, search, method, suspicion, from_date, to_date)

    # Count before pagination
    total = q.with_entities(func.count(Transaction.id)).scalar()

    rows = q.order_by(Transaction.created_at.desc())\
            .offset((page - 1) * per_page)\
            .limit(per_page)\
            .all()

    items = []
    for tx, store_name in rows:
        score = _suspicion_score(tx)
        # Apply suspicion filter (computed, not stored)
        if suspicion == "high"   and score < 61:  continue
        if suspicion == "medium" and not (31 <= score < 61): continue
        if suspicion == "normal" and score >= 31: continue
        items.append({
            "id": tx.id,
            "store_id": tx.store_id,
            "store_name": store_name or f"Store {tx.store_id}",
            "terminal_id": tx.terminal_id,
            "total_amount": float(tx.total_amount),
            "payment_method": tx.payment_method,
            "status": tx.status,
            "created_at": tx.created_at,
            "suspicion_score": score,
        })

    return {
        "total": total,
        "page": page,
        "per_page": per_page,
        "pages": max(1, -(-total // per_page)),  # ceil division
        "items": items,
    }

@router.get("/export/excel")
def export_excel(store_id: Optional[int] = None, db: Session = Depends(get_db), _=Depends(get_current_user)):
    q = db.query(Transaction)
    if store_id:
        q = q.filter(Transaction.store_id == store_id)
    transactions = q.order_by(Transaction.created_at.desc()).all()
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Transactions"
    ws.append(["ID", "Store", "Terminal", "Total", "Payment", "Status", "Date"])
    for t in transactions:
        ws.append([t.id, t.store_id, t.terminal_id, float(t.total_amount),
                   t.payment_method, t.status, str(t.created_at)])
    buf = io.BytesIO()
    wb.save(buf)
    buf.seek(0)
    return StreamingResponse(buf, media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                             headers={"Content-Disposition": "attachment; filename=transactions.xlsx"})
