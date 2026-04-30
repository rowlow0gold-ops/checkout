"""
daily_tx.py  —  Run once per cron invocation.
Generates a realistic batch of transactions for "now" across all 3 stores.
Designed to be called 12× / day.  Stops writing after 2026-12-31.
"""
import random, sys, os
from datetime import date, datetime, timedelta
from sqlalchemy.orm import Session
from sqlalchemy import create_engine, text

DATABASE_URL = os.environ.get("DATABASE_URL", "")
if not DATABASE_URL:
    # fallback: read from .env next to this file
    env_path = os.path.join(os.path.dirname(__file__), ".env")
    if os.path.exists(env_path):
        for line in open(env_path):
            line = line.strip()
            if line.startswith("DATABASE_URL="):
                DATABASE_URL = line[len("DATABASE_URL="):]
                break

if not DATABASE_URL:
    print("ERROR: DATABASE_URL not set", flush=True)
    sys.exit(1)

engine = create_engine(DATABASE_URL)

# ── seasonal helpers ──────────────────────────────────────────────────────────
def get_season(d: date) -> str:
    m = d.month
    if m in (3, 4, 5):   return "spring"
    if m in (6, 7, 8):   return "summer"
    if m in (9, 10, 11): return "fall"
    return "winter"

SEASONAL_ITEM = {
    "spring": ("SEAS_SPRING", "Blueberries",  3.49, "Produce"),
    "summer": ("SEAS_SUMMER", "Watermelon",   4.99, "Produce"),
    "fall":   ("SEAS_FALL",   "Apple",         1.99, "Produce"),
    "winter": ("SEAS_WINTER", "Oranges",       2.79, "Produce"),
}

BASE_PRODUCTS = [
    ("9780201379624", "Programming Book",    39.99, "Books"),
    ("5901234123457", "Dark Chocolate",       3.49, "Snacks"),
    ("4006381333931", "Staedtler Pen",        2.99, "Stationery"),
    ("0012000001086", "Pepsi 500ml",          1.79, "Drinks"),
    ("5000112546415", "Cadbury Dairy Milk",   2.49, "Snacks"),
    ("8801062573158", "Shin Ramyun",          1.29, "Instant Food"),
    ("0038000845031", "Kelloggs Corn Flakes", 4.99, "Breakfast"),
    ("1234567890",    "Apple",                1.99, "Produce"),
]

def get_week_products(d: date):
    """Pick 5–6 base products active this week (deterministic per week)."""
    week_seed = d.year * 100 + d.isocalendar()[1]
    rng = random.Random(week_seed)
    k = rng.randint(5, 6)
    return rng.sample(BASE_PRODUCTS, k)

def get_price_drift(barcode: str, d: date) -> float:
    """Return a week-stable ±5–15% multiplier (±20% for seasonal)."""
    week_seed = hash(barcode) ^ (d.year * 100 + d.isocalendar()[1])
    rng = random.Random(week_seed)
    is_seasonal = barcode.startswith("SEAS_")
    spread = 0.20 if is_seasonal else 0.15
    # drift is cumulative from a baseline — simulate gentle random walk
    drift_weeks = (d - date(2026, 1, 1)).days // 7
    drift_rng = random.Random(hash(barcode) ^ 999)
    multiplier = 1.0
    for _ in range(drift_weeks):
        multiplier *= 1.0 + drift_rng.uniform(-spread * 0.4, spread * 0.4)
    multiplier = max(0.70, min(1.40, multiplier))
    return multiplier

# ── main ──────────────────────────────────────────────────────────────────────
def main():
    today = date.today()
    if today > date(2026, 12, 31):
        print(f"{datetime.now()}: Past end-date, nothing to do.", flush=True)
        return

    now = datetime.now()
    season = get_season(today)
    week_products = get_week_products(today)

    # seasonal product replaces one base if same barcode exists, else appended
    seas_item = SEASONAL_ITEM[season]
    # don't duplicate if "Apple" is already in week_products under base barcode
    active_products = list(week_products)
    if seas_item[1] not in [p[1] for p in active_products]:
        active_products.append(seas_item)

    with Session(engine) as db:
        # resolve store & terminal IDs
        stores = db.execute(text("SELECT id FROM stores ORDER BY id")).fetchall()
        if not stores:
            print("No stores found — run seed_data.py first.", flush=True)
            return
        store_ids = [r[0] for r in stores]

        # get or create products by barcode
        def get_or_create_product(barcode, name, base_price, category):
            row = db.execute(text("SELECT id, price FROM products WHERE barcode = :b"), {"b": barcode}).fetchone()
            drift = get_price_drift(barcode, today)
            price = round(base_price * drift, 2)
            if row is None:
                db.execute(text(
                    "INSERT INTO products (barcode, name, price, category) VALUES (:b,:n,:p,:c)"
                ), {"b": barcode, "n": name, "p": price, "c": category})
                db.flush()
                row = db.execute(text("SELECT id, price FROM products WHERE barcode = :b"), {"b": barcode}).fetchone()
            else:
                # Update price with drift (silent — matches price integrity policy)
                db.execute(text("UPDATE products SET price = :p WHERE barcode = :b"), {"p": price, "b": barcode})
            return row[0], price

        product_rows = [get_or_create_product(*p) for p in active_products]

        is_weekend = today.weekday() >= 5
        total_tx = 0

        for store_id in store_ids:
            terminals = db.execute(text("SELECT id FROM terminals WHERE store_id = :s"), {"s": store_id}).fetchall()
            if not terminals:
                continue
            term_ids = [r[0] for r in terminals]

            # 1–3 transactions per store per cron run  (12 runs/day × ~2 = ~24/store/day)
            num_tx = random.randint(2, 4) if is_weekend else random.randint(1, 3)
            for _ in range(num_tx):
                # timestamp = random past moment today (0:00 up to now), so never future
                elapsed_seconds = int((now - now.replace(hour=0, minute=0, second=0, microsecond=0)).total_seconds())
                past_seconds = random.randint(0, max(elapsed_seconds - 1, 0))
                ts = now.replace(hour=0, minute=0, second=0, microsecond=0) + timedelta(seconds=past_seconds)
                method = random.choices(["cash", "card", "mobile"], weights=[3, 5, 2])[0]
                term_id = random.choice(term_ids)

                db.execute(text(
                    "INSERT INTO transactions (terminal_id, store_id, payment_method, total_amount, created_at) "
                    "VALUES (:t, :s, :m, 0, :ts)"
                ), {"t": term_id, "s": store_id, "m": method, "ts": ts})
                db.flush()
                tx_id = db.execute(text("SELECT lastval()")).scalar()

                chosen = random.sample(product_rows, k=random.randint(1, min(5, len(product_rows))))
                total = 0.0
                for prod_id, unit_price in chosen:
                    qty = random.randint(1, 3)
                    sub = round(unit_price * qty, 2)
                    total += sub
                    db.execute(text(
                        "INSERT INTO transaction_items (transaction_id, product_id, quantity, unit_price, subtotal) "
                        "VALUES (:tx, :p, :q, :u, :s)"
                    ), {"tx": tx_id, "p": prod_id, "q": qty, "u": unit_price, "s": sub})

                db.execute(text("UPDATE transactions SET total_amount = :t WHERE id = :id"),
                           {"t": round(total, 2), "id": tx_id})
                total_tx += 1

        db.commit()
        print(f"{datetime.now().strftime('%Y-%m-%d %H:%M:%S')}  +{total_tx} transactions  season={season}  products={len(active_products)}", flush=True)

if __name__ == "__main__":
    main()
