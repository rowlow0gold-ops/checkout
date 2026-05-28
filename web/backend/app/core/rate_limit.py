"""Shared rate limiter instance. Lives in its own module to avoid a
circular import between app/main.py and the routers that decorate
endpoints with @limiter.limit(...)."""
from slowapi import Limiter
from slowapi.util import get_remote_address

limiter = Limiter(key_func=get_remote_address, default_limits=["120/minute"])
