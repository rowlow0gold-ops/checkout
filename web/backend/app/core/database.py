from sqlalchemy import create_engine, text
from sqlalchemy.orm import declarative_base, sessionmaker
from app.core.config import settings

engine = create_engine(settings.DATABASE_URL)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
Base = declarative_base()

def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


# ---------------- Idempotent migrations ----------------
# These run on every startup. They MUST be safe to call when the app's DB
# user lacks DDL privileges — in that case we just verify the schema is
# already what we need.

def _column_exists(conn, table: str, column: str) -> bool:
    row = conn.execute(text(
        "SELECT 1 FROM information_schema.columns "
        "WHERE table_name = :t AND column_name = :c"
    ), {"t": table, "c": column}).first()
    return row is not None

def _table_exists(conn, table: str) -> bool:
    row = conn.execute(text(
        "SELECT 1 FROM information_schema.tables WHERE table_name = :t"
    ), {"t": table}).first()
    return row is not None

def ensure_lockout_columns():
    """Add failed_attempts + locked_until to users if missing. No-op (and zero
    permission requirement) when both columns already exist."""
    with engine.begin() as conn:
        if not _column_exists(conn, "users", "failed_attempts"):
            conn.execute(text("ALTER TABLE users ADD COLUMN failed_attempts INTEGER NOT NULL DEFAULT 0"))
        if not _column_exists(conn, "users", "locked_until"):
            conn.execute(text("ALTER TABLE users ADD COLUMN locked_until TIMESTAMP WITH TIME ZONE NULL"))

def ensure_refresh_token_table():
    """Create refresh_tokens if missing. No-op when it already exists."""
    with engine.begin() as conn:
        if _table_exists(conn, "refresh_tokens"):
            return
        conn.execute(text("""
            CREATE TABLE refresh_tokens (
                id          SERIAL PRIMARY KEY,
                token_hash  VARCHAR(64) NOT NULL UNIQUE,
                user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                expires_at  TIMESTAMP WITH TIME ZONE NOT NULL,
                revoked     BOOLEAN NOT NULL DEFAULT FALSE,
                created_at  TIMESTAMP WITH TIME ZONE DEFAULT now()
            )
        """))
        conn.execute(text("CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id ON refresh_tokens(user_id)"))
