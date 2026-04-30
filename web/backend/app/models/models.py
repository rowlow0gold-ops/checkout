from sqlalchemy import Column, Integer, String, Numeric, ForeignKey, DateTime, Text
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
