"""
Run from web/backend:  python seed_data.py
Seeds 3 stores, terminals, 4 products, and daily transactions Jan 1 2026 → today.
"""
import random
from datetime import date, datetime, timedelta
from app.core.database import engine
from app.models.models import Store, Terminal, Product, Transaction, TransactionItem
from sqlalchemy.orm import Session

random.seed(42)

with Session(engine) as db:

    # ── Stores ────────────────────────────────────────────────────────────
    stores = [
        Store(name='J Mart Gangnam', address='123 Teheran-ro, Gangnam-gu, Seoul'),
        Store(name='J Mart Hongdae', address='45 Wausan-ro, Mapo-gu, Seoul'),
        Store(name='J Mart Sinchon', address='88 Sinchon-ro, Seodaemun-gu, Seoul'),
    ]
    for s in stores:
        db.add(s)
    db.flush()
    print(f'Stores: {[s.id for s in stores]}')

    # ── Terminals (3 per store) ───────────────────────────────────────────
    term_map = {}
    for store in stores:
        ts = []
        for _ in range(3):
            t = Terminal(store_id=store.id, hardware_version='1.0', status='active')
            db.add(t)
            ts.append(t)
        term_map[store.id] = ts
    db.flush()
    print('Terminals seeded')

    # ── Products ──────────────────────────────────────────────────────────
    products_data = [
        ('1234567890',    'Apple',               1.99,  'Produce'),
        ('9780201379624', 'Programming Book',    39.99, 'Books'),
        ('5901234123457', 'Dark Chocolate',       3.49, 'Snacks'),
        ('0012000001086', 'Pepsi 500ml',          1.79, 'Drinks'),
    ]
    products = []
    for barcode, name, price, cat in products_data:
        p = Product(barcode=barcode, name=name, price=price, category=cat)
        db.add(p)
        products.append(p)
    db.flush()
    print(f'Products: {len(products)}')

    # ── Transactions: Jan 1 2026 → today ─────────────────────────────────
    start    = date(2026, 1, 1)
    end      = date.today()
    total_tx = 0
    BATCH    = 500

    tx_batch   = []
    item_batch = []

    d = start
    while d <= end:
        is_weekend = d.weekday() >= 5
        for store in stores:
            num_tx = random.randint(18, 35) if is_weekend else random.randint(10, 25)
            for _ in range(num_tx):
                hour   = random.choices(range(8, 23), weights=[3,4,6,8,10,12,14,12,10,8,6,5,4,3,2])[0]
                ts     = datetime(d.year, d.month, d.day, hour,
                                  random.randint(0, 59), random.randint(0, 59))
                method = random.choices(['cash', 'card', 'mobile'], weights=[3, 5, 2])[0]
                term   = random.choice(term_map[store.id])

                tx = Transaction(
                    terminal_id=term.id,
                    store_id=store.id,
                    payment_method=method,
                    total_amount=0,
                    created_at=ts,
                )
                db.add(tx)
                db.flush()

                chosen = random.sample(products, k=random.randint(1, 5))
                total  = 0.0
                for p in chosen:
                    qty = random.randint(1, 3)
                    sub = round(float(p.price) * qty, 2)
                    total += sub
                    db.add(TransactionItem(
                        transaction_id=tx.id,
                        product_id=p.id,
                        quantity=qty,
                        unit_price=float(p.price),
                        subtotal=sub,
                    ))
                tx.total_amount = round(total, 2)
                total_tx += 1

                if total_tx % BATCH == 0:
                    db.commit()
                    print(f'  {total_tx} transactions committed...')

        d += timedelta(days=1)

    db.commit()
    print(f'\nDone — {total_tx} transactions across {(end - start).days + 1} days')
