from sqlalchemy import create_engine
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


def ensure_lockout_columns():
    """Idempotently add account-lockout columns to the existing users table.
    create_all() doesn't ALTER existing tables; in a real project you'd use
    Alembic. This is a small one-shot migration helper."""
    from sqlalchemy import text
    with engine.begin() as conn:
        conn.execute(text("ALTER TABLE users ADD COLUMN IF NOT EXISTS failed_attempts INTEGER NOT NULL DEFAULT 0"))
        conn.execute(text("ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_until TIMESTAMP WITH TIME ZONE NULL"))


def ensure_refresh_token_table():
    """Idempotently create refresh_tokens table (create_all() should handle this
    but be defensive for existing deployments)."""
    from sqlalchemy import text
    with engine.begin() as conn:
        conn.execute(text("""
            CREATE TABLE IF NOT EXISTS refresh_tokens (
                id          SERIAL PRIMARY KEY,
                token_hash  VARCHAR(64) NOT NULL UNIQUE,
                user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                expires_at  TIMESTAMP WITH TIME ZONE NOT NULL,
                revoked     BOOLEAN NOT NULL DEFAULT FALSE,
                created_at  TIMESTAMP WITH TIME ZONE DEFAULT now()
            )
        """))
        conn.execute(text("CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id ON refresh_tokens(user_id)"))
