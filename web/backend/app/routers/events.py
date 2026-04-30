import asyncio
import json
import logging
import redis.asyncio as aioredis
from fastapi import APIRouter, Query
from fastapi.responses import StreamingResponse
from app.core.config import settings
from app.core.redis import TRANSACTION_STREAM
from app.core.security import decode_token

logger = logging.getLogger(__name__)
router = APIRouter()

@router.get("/stream")
async def event_stream(token: str = Query(...)):
    """SSE endpoint — browser connects once, receives push on every new transaction."""
    try:
        decode_token(token)  # validate JWT
    except Exception:
        async def denied():
            yield "event: error\ndata: unauthorized\n\n"
        return StreamingResponse(denied(), media_type="text/event-stream")

    async def generator():
        # Fresh connection per stream — avoids dirty-state from cancelled xread
        client = aioredis.from_url(
            settings.REDIS_URL,
            decode_responses=True,
            socket_connect_timeout=5,
            socket_timeout=30,
        )
        last_id = "$"   # only events that arrive after the connection opens
        try:
            yield "data: connected\n\n"
            while True:
                try:
                    # block=500 ms — max 0.5s lag before event reaches browser
                    messages = await client.xread({TRANSACTION_STREAM: last_id}, block=500, count=10)
                    if messages:
                        for _, events in messages:
                            for event_id, data in events:
                                last_id = event_id
                                yield f"data: {json.dumps(data)}\n\n"
                    else:
                        yield ": ping\n\n"   # keep-alive
                except asyncio.CancelledError:
                    break
                except Exception as e:
                    logger.warning("SSE xread error: %s | type=%s", e, type(e).__name__)
                    yield ": ping\n\n"
                    await asyncio.sleep(1)
        finally:
            await client.aclose()

    return StreamingResponse(
        generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",   # tell nginx not to buffer
        },
    )
