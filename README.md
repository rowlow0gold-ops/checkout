# Checkout System

A self-checkout retail system with a WPF terminal, local store server, and cloud management dashboard.

**Live dashboard:** https://checkout.minhojan-world.site

---

## Architecture

```
WPF Terminal  ──►  Store Server (.NET)  ──►  Cloud Backend (FastAPI)  ──►  PostgreSQL (RDS)
     ▲                    │                          │
     └── SignalR ─────────┘                   nginx + React SPA
```

- **Terminal** — WPF app on Windows. Handles barcode scan, cash/card/mobile payment, loyalty cards, weight checks, and cash drawer.
- **Store Server** — ASP.NET Core running locally per store. Stores transactions in SQLite, syncs to cloud, refreshes product catalog, and pushes SignalR events to terminals.
- **Cloud Backend** — FastAPI on AWS Lightsail VPS. Receives synced transactions, serves the web dashboard API, and streams live events via SSE.
- **Web Dashboard** — React SPA served by nginx. Shows transactions, analytics, suspicion scoring, and live activity.

---

## Projects

| Folder | Stack | Purpose |
|---|---|---|
| `terminal/` | C# / WPF | Customer-facing checkout terminal |
| `emulator/` | C# / WPF | Hardware emulator (barcode scanner, scale, card reader) |
| `store-server/` | ASP.NET Core 8 | Per-store local API + background sync |
| `web/backend/` | FastAPI + SQLAlchemy | Cloud API + cron job |
| `web/frontend/` | React + Vite + Tailwind | Management dashboard |

---

## Running Locally

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- A running store server (starts automatically via the script below)

### Start everything

```bash
cd checkout
./start-dev.sh
```

This script:
1. Clears port 5100 and deletes the local `store.db` (fresh seed on every run)
2. Builds and starts the store server at `http://localhost:5100`
3. Launches the emulator (barcode scanner / scale / card reader simulation)
4. Builds and starts Terminal #1
5. Use the emulator's power button to open additional terminals

Logs are written to `.logs/`.

### Default credentials (web dashboard)
| Role | Email | Password |
|---|---|---|
| Super admin | *(set in .env)* | *(set in .env)* |

### Default staff PIN
`4312` (used to unlock settings and override insufficient cash)

### Loyalty demo accounts
| Name | Phone | Card ID | Points | Tier |
|---|---|---|---|---|
| Kim Ji-woo | 01012345678 | 4910000000001 | 12,450 | ★ Gold |
| Park Soo-yeon | 01987654321 | — | 5,670 | ★ Gold |
| Lee Min-jun | 01198765432 | 4910000000002 | 3,280 | ◈ Silver |
| Choi Dong-hyun | 01099998888 | 4910000000004 | 420 | ◇ Bronze |

PIN pattern for all demo accounts: `1-2-3-6` (corners + bottom-right)

---

## Cloud Deployment (VPS)

**Server:** AWS Lightsail — `52.78.141.29` (ap-northeast-2)

### Services on VPS
- **nginx** — serves React SPA at `/`, proxies `/api/` to FastAPI
- **Docker Compose** — runs `checkout-backend` (FastAPI on port 8000) and `checkout-redis`
- **PostgreSQL** — AWS RDS in same region
- **Cron** — `daily_tx.py` runs every 2 hours, generates realistic transactions across all stores

### Deploy backend changes

```bash
ssh -i LightsailDefaultKey.pem ubuntu@52.78.141.29
cd /opt/checkout/web
# copy updated files, then:
docker compose restart backend
```

### Deploy frontend changes

```bash
# On your Mac:
cd web/frontend
npm run build

# Copy dist to VPS:
scp -i LightsailDefaultKey.pem -r dist/* ubuntu@52.78.141.29:/opt/checkout/web/frontend/dist/
```

### Environment variables

The backend reads from `/opt/checkout/web/backend/.env`:

```
DATABASE_URL=postgresql://...
SECRET_KEY=...
REDIS_URL=redis://checkout-redis:6379
```

The store server reads from `appsettings.json` (or environment overrides):

```json
{
  "CloudApiUrl": "https://checkout.minhojan-world.site/api/",
  "CloudApiKey": "",
  "StoreId": 8
}
```

---

## Stores

| ID | Name |
|---|---|
| 7 | J Mart Manhattan |
| 8 | J Mart LA West |
| 9 | J Mart Chicago |

---

## Key Features

- **Instant sync** — transactions appear in the dashboard within ~1 second of completion
- **Offline resilience** — if cloud is unreachable, transactions stay as `pending` in SQLite and retry automatically
- **Suspicion scoring** — transactions scored 0–100 based on amount, time of day, and payment method
- **Loyalty system** — points, tiers (Bronze/Silver/Gold), phone or card lookup, 9-dot pattern PIN
- **Weight checks** — scale verification per product; staff override available
- **Cash drawer** — denomination tracking with change calculation
- **Server-side pagination** — handles 10,000+ transactions with filters, search, and date range
