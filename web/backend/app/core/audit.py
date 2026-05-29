"""Audit log helper. Writes a row per security-relevant event.

Uses its own short-lived session so the row commits independently of the
caller's transaction (mirrors REQUIRES_NEW in the Spring backends).
"""
from typing import Optional
from sqlalchemy.orm import Session
from app.core.database import SessionLocal
from app.models.models import AuditEvent


def record(action: str, *, email: Optional[str] = None, ip: Optional[str] = None,
           success: bool = True, details: Optional[str] = None) -> None:
    db: Session = SessionLocal()
    try:
        db.add(AuditEvent(
            email=email,
            action=action,
            ip=ip,
            success=success,
            details=(details[:500] if details and len(details) > 500 else details),
        ))
        db.commit()
    except Exception:
        db.rollback()
        # Never fail the caller because audit logging hiccupped
    finally:
        db.close()
