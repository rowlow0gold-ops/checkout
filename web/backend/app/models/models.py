from sqlalchemy import Boolean, Column, DateTime, ForeignKey, Integer, Numeric, String, Text
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.core.database import Base

class Store(Base):
    __tablename__ = "stores"
    id         = Column(Integer, primary_key=True)
    name       = Column(String(100), nullable=False)
    address    = Column(Text)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    terminals  = relationship("Terminal", back_populates="store")
    transactions = relationship("Transaction", back_populates="store")

class Terminal(Base):
    __tablename__ = "terminals"
    id               = Column(Integer, primary_key=True)
    store_id         = Column(Integer, ForeignKey("stores.id"))
    hardware_version = Column(String(50))
    status           = Column(String(20), default="active")
    last_seen        = Column(DateTime(timezone=True))
    store            = relationship("Store", back_populates="terminals")

class Product(Base):
    __tablename__ = "products"
    id         = Column(Integer, primary_key=True)
    barcode    = Column(String(50), unique=True, nullable=False)
    name       = Column(String(200), nullable=False)
    price      = Column(Numeric(10, 2), nullable=False)
    category   = Column(String(100))
    updated_at = Column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())

class Transaction(Base):
    __tablename__ = "transactions"
    id             = Column(Integer, primary_key=True)
    terminal_id    = Column(Integer, ForeignKey("terminals.id"))
    store_id       = Column(Integer, ForeignKey("stores.id"))
    total_amount   = Column(Numeric(10, 2), nullable=False)
    payment_method = Column(String(20))
    status         = Column(String(20), default="completed")
    created_at     = Column(DateTime(timezone=True), server_default=func.now())
    store          = relationship("Store", back_populates="transactions")
    items          = relationship("TransactionItem", back_populates="transaction")

class TransactionItem(Base):
    __tablename__ = "transaction_items"
    id             = Column(Integer, primary_key=True)
    transaction_id = Column(Integer, ForeignKey("transactions.id"))
    product_id     = Column(Integer, ForeignKey("products.id"))
    quantity       = Column(Integer, nullable=False)
    unit_price     = Column(Numeric(10, 2), nullable=False)
    subtotal       = Column(Numeric(10, 2), nullable=False)
    transaction    = relationship("Transaction", back_populates="items")
    product        = relationship("Product")

class User(Base):
    __tablename__ = "users"
    id            = Column(Integer, primary_key=True)
    username      = Column(String(50), unique=True, nullable=False)
    password_hash = Column(Text, nullable=False)
    role          = Column(String(20), default="admin")  # super_admin | admin
    store_id      = Column(Integer, ForeignKey("stores.id"), nullable=True)
    created_at    = Column(DateTime(timezone=True), server_default=func.now())
    # Account lockout state
    failed_attempts = Column(Integer, nullable=False, server_default="0", default=0)
    locked_until    = Column(DateTime(timezone=True), nullable=True)



class RefreshToken(Base):
    """Stores the SHA-256 hash of issued refresh tokens. Raw tokens never persisted.
    On /auth/refresh we verify, mark revoked (single-use), and issue a new pair.
    """
    __tablename__ = "refresh_tokens"
    id         = Column(Integer, primary_key=True)
    token_hash = Column(String(64), unique=True, nullable=False, index=True)
    user_id    = Column(Integer, ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    expires_at = Column(DateTime(timezone=True), nullable=False)
    revoked    = Column(Boolean, nullable=False, default=False, server_default="false")
    created_at = Column(DateTime(timezone=True), server_default=func.now())



class AuditEvent(Base):
    """Append-only log of security-relevant events (login attempts)."""
    __tablename__ = "audit_events"
    id         = Column(Integer, primary_key=True)
    email      = Column(String(200), index=True)
    action     = Column(String(50), nullable=False)
    ip         = Column(String(64))
    success    = Column(Boolean, nullable=False, default=True, server_default="true")
    details    = Column(String(500))
    created_at = Column(DateTime(timezone=True), server_default=func.now())
