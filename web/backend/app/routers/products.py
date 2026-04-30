import json
from fastapi import APIRouter, Depends, HTTPException, UploadFile, File
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session
from app.core.database import get_db
from app.core.redis import get_cached, set_cached, delete_cached
from app.models.models import Product
from app.routers.auth import get_current_user, require_super_admin
from pydantic import BaseModel
from typing import Optional
import io, openpyxl

router = APIRouter()

class ProductIn(BaseModel):
    barcode: str
    name: str
    price: float
    category: Optional[str] = None

@router.get("/")
async def list_products(db: Session = Depends(get_db)):
    cached = await get_cached("products:all")
    if cached:
        return json.loads(cached)
    products = db.query(Product).all()
    data = [{"id": p.id, "barcode": p.barcode, "name": p.name,
             "price": float(p.price), "category": p.category} for p in products]
    await set_cached("products:all", json.dumps(data))
    return data

@router.get("/barcode/{barcode}")
async def get_by_barcode(barcode: str, db: Session = Depends(get_db)):
    cached = await get_cached(f"product:{barcode}")
    if cached:
        return json.loads(cached)
    p = db.query(Product).filter(Product.barcode == barcode).first()
    if not p:
        raise HTTPException(status_code=404, detail="Product not found")
    data = {"id": p.id, "barcode": p.barcode, "name": p.name,
            "price": float(p.price), "category": p.category}
    await set_cached(f"product:{barcode}", json.dumps(data))
    return data

@router.post("/")
async def create_product(body: ProductIn, db: Session = Depends(get_db), _=Depends(get_current_user)):
    p = Product(**body.model_dump())
    db.add(p)
    db.commit()
    db.refresh(p)
    await delete_cached("products:all")
    return p

@router.put("/{product_id}")
async def update_product(product_id: int, body: ProductIn, db: Session = Depends(get_db), _=Depends(get_current_user)):
    p = db.query(Product).filter(Product.id == product_id).first()
    if not p:
        raise HTTPException(status_code=404, detail="Not found")
    for k, v in body.model_dump().items():
        setattr(p, k, v)
    db.commit()
    await delete_cached("products:all")
    await delete_cached(f"product:{p.barcode}")
    return p

@router.delete("/{product_id}")
async def delete_product(product_id: int, db: Session = Depends(get_db), _=Depends(get_current_user)):
    p = db.query(Product).filter(Product.id == product_id).first()
    if not p:
        raise HTTPException(status_code=404, detail="Not found")
    db.delete(p)
    db.commit()
    await delete_cached("products:all")
    return {"ok": True}

@router.post("/import")
async def import_products(file: UploadFile = File(...), db: Session = Depends(get_db), _=Depends(require_super_admin)):
    content = await file.read()
    wb = openpyxl.load_workbook(io.BytesIO(content))
    ws = wb.active
    count = 0
    for row in ws.iter_rows(min_row=2, values_only=True):
        if not row or not row[0]:
            continue
        barcode = str(row[0])
        name     = row[1]
        price    = float(row[2])
        category = row[3] if len(row) > 3 else None
        p = db.query(Product).filter(Product.barcode == barcode).first()
        if p:
            p.name = name; p.price = price; p.category = category
        else:
            db.add(Product(barcode=barcode, name=name, price=price, category=category))
        count += 1
    db.commit()
    await delete_cached("products:all")
    return {"imported": count}

@router.get("/export/excel")
def export_excel(db: Session = Depends(get_db), _=Depends(get_current_user)):
    products = db.query(Product).all()
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Products"
    ws.append(["ID", "Barcode", "Name", "Price", "Category"])
    for p in products:
        ws.append([p.id, p.barcode, p.name, float(p.price), p.category])
    buf = io.BytesIO()
    wb.save(buf)
    buf.seek(0)
    return StreamingResponse(buf, media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                             headers={"Content-Disposition": "attachment; filename=products.xlsx"})
