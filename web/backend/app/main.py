import os
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from app.core.database import engine, Base
from app.routers import auth, stores, products, transactions, dashboard, events

Base.metadata.create_all(bind=engine)

app = FastAPI(title="Checkout Management API", version="1.0.0")

# Rate limiting (per remote address). Login endpoint applies a stricter
# limit via decorator. Default limit catches general abuse / scraping.
from slowapi import Limiter, _rate_limit_exceeded_handler
from slowapi.util import get_remote_address
from slowapi.errors import RateLimitExceeded
limiter = Limiter(key_func=get_remote_address, default_limits=["120/minute"])
app.state.limiter = limiter
app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)

# CORS: explicit allowlist. `*` is forbidden when allow_credentials=True
# (browsers reject the combination), and would let any site make
# credentialed cross-origin requests against this API.
_default_origins = [
    "https://checkout.minhojan-world.site",
    "http://localhost:5173",   # vite dev
    "http://localhost:3000",   # alt dev port
]
_env_origins = [o.strip() for o in os.environ.get("CORS_ALLOWED_ORIGINS", "").split(",") if o.strip()]
ALLOWED_ORIGINS = _env_origins or _default_origins

app.add_middleware(
    CORSMiddleware,
    allow_origins=ALLOWED_ORIGINS,
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
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
