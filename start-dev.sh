#!/bin/bash
# Launches all checkout apps.
# Ctrl+C in this terminal kills everything cleanly.
# Logs written to checkout/.logs/

CHECKOUT="$(cd "$(dirname "$0")" && pwd)"
LOGS="$CHECKOUT/.logs"
DB="$CHECKOUT/store-server/StoreServer/store.db"
mkdir -p "$LOGS"

PIDS=()

# Recursively kill a process and all its descendants.
kill_tree() {
  local pid=$1
  local children
  children=$(pgrep -P "$pid" 2>/dev/null) || true
  for child in $children; do
    kill_tree "$child"
  done
  kill -TERM "$pid" 2>/dev/null || true
}

cleanup() {
  echo ""
  echo "Shutting down..."
  for pid in "${PIDS[@]}"; do
    kill_tree "$pid"
  done
  # Give processes a moment to exit, then force-kill anything left
  sleep 1
  for pid in "${PIDS[@]}"; do
    kill -9 "$pid" 2>/dev/null || true
  done
  echo "All stopped."
  exit 0
}
trap cleanup INT TERM

# Kill any leftover process still holding port 5100 (stale server from a previous session)
echo "Clearing port 5100..."
lsof -ti :5100 | xargs kill -9 2>/dev/null || true
sleep 0.5

# Always delete store.db (and SQLite WAL companion files) so schema changes
# and seed data are applied cleanly. EnsureCreated re-seeds on startup.
rm -f "${DB}"*

# Store Server — always build first so code changes are never missed
echo "Building store server..."
(cd "$CHECKOUT/store-server/StoreServer" && dotnet build -c Debug -v q) > "$LOGS/store-server-build.log" 2>&1

(cd "$CHECKOUT/store-server/StoreServer" && dotnet run --no-build) \
  > "$LOGS/store-server.log" 2>&1 &
PIDS+=($!)

# Wait until the server is actually ready (health endpoint responds)
echo "Waiting for store server to start..."
until curl -s http://localhost:5100/health > /dev/null 2>&1; do
  sleep 1
done
echo "Store server ready."

# Emulator GUI
(cd "$CHECKOUT/emulator/Emulator" && dotnet run) \
  > "$LOGS/emulator.log" 2>&1 &
PIDS+=($!)

# Pre-build terminal
echo "Building terminal..."
(cd "$CHECKOUT/terminal/Terminal" && dotnet build -c Debug -v q) >> "$LOGS/terminal-build.log" 2>&1

# Checkout Terminal #1 only — use emulator ⏻ button to open others
(cd "$CHECKOUT/terminal/Terminal" && dotnet run --no-build) \
  > "$LOGS/terminal1.log" 2>&1 &
PIDS+=($!)

echo "All apps launched. Press Ctrl+C to stop everything."
wait
