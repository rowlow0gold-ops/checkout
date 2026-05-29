from fastapi import APIRouter, Depends, HTTPException, Request, status
from app.core.rate_limit import limiter
from fastapi.security import OAuth2PasswordBearer, OAuth2PasswordRequestForm
from sqlalchemy.orm import Session
from datetime import datetime, timedelta, timezone
from app.core.database import get_db
from app.core.config import settings
from app.core import audit
from app.core.security import verify_password, create_access_token, decode_token, generate_refresh_token, hash_refresh_token
from app.models.models import User, RefreshToken
from pydantic import BaseModel

router = APIRouter()
oauth2_scheme = OAuth2PasswordBearer(tokenUrl="/auth/login")

class Token(BaseModel):
    access_token: str
    refresh_token: str
    token_type: str
    role: str
    store_id: int | None

class RefreshIn(BaseModel):
    refresh_token: str

class TokenPair(BaseModel):
    access_token: str
    refresh_token: str
    token_type: str = "bearer"

def _issue_refresh(user_id: int, db: Session) -> str:
    raw = generate_refresh_token()
    rt = RefreshToken(
        token_hash=hash_refresh_token(raw),
        user_id=user_id,
        expires_at=datetime.now(timezone.utc) + timedelta(days=settings.JWT_REFRESH_EXPIRE_DAYS),
        revoked=False,
    )
    db.add(rt)
    db.commit()
    return raw

LOCKOUT_THRESHOLD = 5
LOCKOUT_DURATION = timedelta(minutes=15)

@router.post("/login", response_model=Token)
@limiter.limit("5/minute")
def login(request: Request, form: OAuth2PasswordRequestForm = Depends(), db: Session = Depends(get_db)):
    user = db.query(User).filter(User.username == form.username).first()
    if not user:
        audit.record("LOGIN_FAILURE", email=form.username, ip=request.client.host if request.client else None, success=False, details="unknown_user")
        # Same generic error so we don't leak which usernames exist
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid credentials")

    now = datetime.now(timezone.utc)
    if user.locked_until and user.locked_until > now:
        mins = int((user.locked_until - now).total_seconds() / 60) + 1
        audit.record("LOGIN_LOCKED", email=user.username, ip=request.client.host if request.client else None, success=False, details=f"minutes_remaining={mins}")
        raise HTTPException(
            status_code=status.HTTP_423_LOCKED,
            detail=f"Account locked. Try again in {mins} minute(s)."
        )

    if not verify_password(form.password, user.password_hash):
        user.failed_attempts = (user.failed_attempts or 0) + 1
        if user.failed_attempts >= LOCKOUT_THRESHOLD:
            user.locked_until = now + LOCKOUT_DURATION
            user.failed_attempts = 0
        db.commit()
        audit.record("LOGIN_FAILURE", email=user.username, ip=request.client.host if request.client else None, success=False, details=f"attempts={user.failed_attempts}")
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid credentials")

    # Success — reset state
    if user.failed_attempts or user.locked_until:
        user.failed_attempts = 0
        user.locked_until = None
        db.commit()

    audit.record("LOGIN_SUCCESS", email=user.username, ip=request.client.host if request.client else None, success=True)
    token = create_access_token({"sub": str(user.id), "username": user.username, "role": user.role, "store_id": user.store_id})
    refresh_raw = _issue_refresh(user.id, db)
    return {"access_token": token, "refresh_token": refresh_raw, "token_type": "bearer", "role": user.role, "store_id": user.store_id}

def get_current_user(token: str = Depends(oauth2_scheme), db: Session = Depends(get_db)) -> User:
    try:
        payload = decode_token(token)
        user = db.query(User).filter(User.id == int(payload["sub"])).first()
        if not user:
            raise HTTPException(status_code=401, detail="User not found")
        return user
    except Exception:
        raise HTTPException(status_code=401, detail="Invalid token")

def require_super_admin(current_user: User = Depends(get_current_user)) -> User:
    if current_user.role != "super_admin":
        raise HTTPException(status_code=403, detail="Super admin required")
    return current_user


@router.post("/refresh", response_model=TokenPair)
def refresh(body: RefreshIn, db: Session = Depends(get_db)):
    rt = db.query(RefreshToken).filter(RefreshToken.token_hash == hash_refresh_token(body.refresh_token)).first()
    if not rt:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid refresh token")
    # REUSE DETECTION: a revoked token coming back almost certainly means
    # it was stolen and replayed. Revoke every active token for this user.
    if rt.revoked:
        db.query(RefreshToken).filter(
            RefreshToken.user_id == rt.user_id,
            RefreshToken.revoked == False,
        ).update({"revoked": True}, synchronize_session=False)
        db.commit()
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Refresh token replay detected — all sessions revoked")
    if rt.expires_at < datetime.now(timezone.utc):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Refresh token expired")
    # Rotation: revoke old, issue new
    rt.revoked = True
    db.commit()
    user = db.query(User).filter(User.id == rt.user_id).first()
    if not user:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="User no longer exists")
    new_access = create_access_token({"sub": str(user.id), "username": user.username, "role": user.role, "store_id": user.store_id})
    new_refresh = _issue_refresh(user.id, db)
    return {"access_token": new_access, "refresh_token": new_refresh, "token_type": "bearer"}

@router.post("/logout", status_code=status.HTTP_204_NO_CONTENT)
def logout(body: RefreshIn, db: Session = Depends(get_db)):
    rt = db.query(RefreshToken).filter(RefreshToken.token_hash == hash_refresh_token(body.refresh_token)).first()
    if rt and not rt.revoked:
        rt.revoked = True
        db.commit()
    return None
