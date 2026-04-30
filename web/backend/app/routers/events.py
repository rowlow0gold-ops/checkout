import asyncio
import json
from fastapi import APIRouter, Query
from fastapi.responses import StreamingResponse
from app.core.redis import redis, TRANSACTION_STREAM
from app.core.security import decode_token

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
        last_id = "$"   # only events that arrive after the connection opens
        yield "data: connected\n\n"
        while True:
            try:
                messages = await asyncio.wait_for(
                    redis.xread({TRANSACTION_STREAM: last_id}, block=10000, count=10),
                    timeout=12,
                )
                if messages:
                    for _, events in messages:
                        for event_id, data in events:
                            last_id = event_id
                            yield f"data: {json.dumps(data)}\n\n"
                else:
                    yield ": ping\n\n"   # keep-alive
            except (asyncio.TimeoutError, asyncio.CancelledError):
                yield ": ping\n\n"
            except Exception:
                break

    return StreamingResponse(
        generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",   # tell nginx not to buffer
        },
    )
