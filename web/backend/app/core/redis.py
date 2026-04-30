import redis.asyncio as aioredis
from app.core.config import settings

redis = aioredis.from_url(
    settings.REDIS_URL,
    decode_responses=True,
    retry_on_timeout=True,
    socket_connect_timeout=5,
    socket_timeout=5,
    health_check_interval=30,
)

PRODUCT_CACHE_TTL = 60 * 10  # 10 minutes

async def get_cached(key: str):
    return await redis.get(key)

async def set_cached(key: str, value: str, ttl: int = PRODUCT_CACHE_TTL):
    await redis.set(key, value, ex=ttl)

async def delete_cached(key: str):
    await redis.delete(key)

# Redis Streams — store server pushes transaction events here
TRANSACTION_STREAM = "checkout:transactions"

async def push_transaction_event(data: dict):
    # Use a fresh connection each time to avoid stale-socket issues after reboots.
    client = aioredis.from_url(
        settings.REDIS_URL,
        decode_responses=True,
        socket_connect_timeout=5,
        socket_timeout=5,
    )
    try:
        await client.xadd(TRANSACTION_STREAM, data)
    finally:
        await client.aclose()
