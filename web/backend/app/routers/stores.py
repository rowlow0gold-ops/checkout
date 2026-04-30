from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from app.core.database import get_db
from app.models.models import Store, Terminal
from app.routers.auth import get_current_user, require_super_admin
from pydantic import BaseModel
from typing import Optional

router = APIRouter()

class StoreIn(BaseModel):
    name: str
    address: Optional[str] = None

@router.get("/")
def list_stores(db: Session = Depends(get_db), _=Depends(get_current_user)):
    return db.query(Store).all()

@router.post("/")
def create_store(body: StoreIn, db: Session = Depends(get_db), _=Depends(require_super_admin)):
    store = Store(**body.model_dump())
    db.add(store)
    db.commit()
    db.refresh(store)
    return store

@router.get("/{store_id}/terminals")
def list_terminals(store_id: int, db: Session = Depends(get_db), _=Depends(get_current_user)):
    return db.query(Terminal).filter(Terminal.store_id == store_id).all()
