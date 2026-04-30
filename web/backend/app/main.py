from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from app.core.database import engine, Base
from app.routers import auth, stores, products, transactions, dashboard, events

Base.metadata.create_all(bind=engine)

app = FastAPI(title="Checkout Management API", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(auth.router,         prefix="/auth",         tags=["auth"])
app.include_router(stores.router,       prefix="/stores",       tags=["stores"])
app.include_router(products.router,     prefix="/products",     tags=["products"])
app.include_router(transactions.router, prefix="/transactions", tags=["transactions"])
app.include_router(dashboard.router,    prefix="/dashboard",    tags=["dashboard"])
app.include_router(events.router,       prefix="/events",       tags=["events"])

@app.get("/health")
def health():
    return {"status": "ok"}
