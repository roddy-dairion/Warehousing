"""
Demo data for the HELLO Cherie toilet-roll range (packs of 4, 8, 30 and 60 rolls).

Creates, through the API (so every rule, audit entry and stock movement is real):
  * category "Toilet paper", 4 products (one SKU per pack size, counted in packs "PK"),
    with carton / pallet units and demo barcodes;
  * storage bins, 3 demo customers with delivery addresses;
  * a demo manager and operator (to approve dispatches and to sign in on the phone);
  * a posted goods receipt (with production lots) and 4 dispatches in different states.

Running it again is safe: what already exists (same SKU, bin code, customer reference, e-mail) is skipped, and the
stock and dispatches are only created once (when the 4-roll pack has no stock yet).

Only needs Python 3.9+ (standard library). Run it with the API started:
    python tools/seed/hello_cherie_demo.py
    python tools/seed/hello_cherie_demo.py --api http://192.168.100.70:5080/api/v1 --email admin@... --password ...

The barcodes are demo codes (prefix 20, reserved for in-store use, so they never clash with real products). To use the
real barcode printed on a box: scan it on the phone as a manager and choose "Add this barcode to a product".
"""
import argparse
import datetime as dt
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

DEMO_PASSWORD = "Demo-Password1"

DESCRIPTION = (
    "HELLO Cherie Switzerland premium toilet roll with Bio Sweet Jasmin fragrance in the core. "
    "75% bamboo, 25% virgin wood. 4 ply, extra soft, 150 sheets per roll. "
    "No plastic: 100% paper packaging, recyclable. 100% BIO."
)

# sku, rolls per pack, name, extra unit (code, packs per unit), minimum stock in packs, demo barcode for one pack,
# demo barcode for the extra unit
PRODUCTS = [
    ("HC-TR4-JAS", 4, "HELLO Cherie toilet roll Bio Sweet Jasmin, 4 rolls", ("CTN", 12), 120, "2000000000046", "2000000001043"),
    ("HC-TR8-JAS", 8, "HELLO Cherie toilet roll Bio Sweet Jasmin, 8 rolls", ("CTN", 6), 60, "2000000000084", "2000000001081"),
    ("HC-TR30-JAS", 30, "HELLO Cherie toilet roll Bio Sweet Jasmin, 30 rolls", ("PAL", 24), 24, "2000000000305", "2000000001302"),
    ("HC-TR60-JAS", 60, "HELLO Cherie toilet roll Bio Sweet Jasmin, 60 rolls", ("PAL", 12), 60, "2000000000602", "2000000001609"),
]

BINS = [("A-01", "BIN-A01"), ("A-02", "BIN-A02"), ("A-03", "BIN-A03"), ("B-01", "BIN-B01"), ("B-02", "BIN-B02")]

CUSTOMERS = [
    {"reference": "DEMO-HOTEL", "companyName": "Demo Hotel Lakeview SA", "contactName": "Reception", "phone": "+41 21 000 00 01",
     "email": "orders@hotel-lakeview.example", "billingLine1": "Quai Demo 1", "billingCity": "Lausanne", "billingCountry": "CH",
     "addresses": [{"label": "Goods entrance", "line1": "Quai Demo 1", "city": "Lausanne", "country": "CH",
                    "contactName": "Housekeeping", "phone": "+41 21 000 00 02", "isDefault": True}]},
    {"reference": "DEMO-SPA", "companyName": "Demo Spa Bellevue Sàrl", "contactName": "Front desk", "phone": "+41 22 000 00 01",
     "email": "contact@spa-bellevue.example", "billingLine1": "Rue Exemple 12", "billingCity": "Genève", "billingCountry": "CH",
     "addresses": [{"label": "Spa", "line1": "Rue Exemple 12", "city": "Genève", "country": "CH", "isDefault": True}]},
    {"reference": "DEMO-OFFICE", "companyName": "Demo Office Services GmbH", "contactName": "Facility management",
     "phone": "+41 44 000 00 01", "email": "facility@office-services.example", "billingLine1": "Musterstrasse 5",
     "billingCity": "Zürich", "billingCountry": "CH",
     "addresses": [{"label": "Head office", "line1": "Musterstrasse 5", "city": "Zürich", "country": "CH", "isDefault": True},
                   {"label": "Branch Bern", "line1": "Beispielweg 9", "city": "Bern", "country": "CH", "isDefault": False}]},
]

USERS = [
    ("manager.demo@hellocherie.test", "Demo manager", "WH_MANAGER"),
    ("operator.demo@hellocherie.test", "Demo operator", "WH_OPERATOR"),
]


class Api:
    def __init__(self, base: str):
        self.base = base.rstrip("/")
        self.token = None

    def call(self, method: str, path: str, body=None, query=None, idempotent=False):
        url = self.base + path + ("?" + urllib.parse.urlencode(query) if query else "")
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Accept", "application/json")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("Authorization", f"Bearer {self.token}")
        if idempotent:
            req.add_header("Idempotency-Key", str(uuid.uuid4()))
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                raw = r.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode(errors="replace")
            sys.exit(f"\n{method} {path} failed: HTTP {e.code}\n{detail}")
        except urllib.error.URLError as e:
            sys.exit(f"\nCannot reach the API at {self.base} ({e.reason}). Is it running?")

    def login(self, email: str, password: str):
        self.token = None
        self.token = self.call("POST", "/auth/login", {"email": email, "password": password, "client": "mobile"})["accessToken"]


def log(msg: str):
    print(msg, flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--api", default="http://localhost:5080/api/v1")
    ap.add_argument("--email", default="admin@hellocherie.test", help="an administrator")
    ap.add_argument("--password", default="Admin-Password1")
    ap.add_argument("--warehouse", default=None, help="warehouse code (default: the first one)")
    a = ap.parse_args()

    api = Api(a.api)
    api.login(a.email, a.password)
    log(f"Signed in to {a.api} as {a.email}")

    warehouses = api.call("GET", "/warehouses")
    wh = next((w for w in warehouses if a.warehouse is None or w["code"] == a.warehouse), None)
    if wh is None:
        sys.exit(f"Warehouse {a.warehouse} not found.")
    log(f"Warehouse {wh['code']} · {wh['name']}")

    units = {u["code"]: u["id"] for u in api.call("GET", "/units")}

    # Category
    cats = api.call("GET", "/categories")
    cat = next((c for c in cats if c["code"] == "TOILET-PAPER"), None)
    if cat is None:
        cat = api.call("POST", "/categories", {"code": "TOILET-PAPER", "name": "Toilet paper", "parentId": None})
        log("+ category Toilet paper")

    # Products
    products = {}
    for sku, rolls, name, (unit_code, factor), min_stock, ean_pack, ean_unit in PRODUCTS:
        found = api.call("GET", "/products", query={"search": sku, "pageSize": 10})["items"]
        existing = next((p for p in found if p["sku"] == sku), None)
        if existing:
            products[sku] = api.call("GET", f"/products/{existing['id']}")
            log(f"= product {sku} already exists")
            continue
        products[sku] = api.call("POST", "/products", {
            "sku": sku, "name": name,
            "description": f"Pack of {rolls} rolls ({rolls * 150} sheets). " + DESCRIPTION,
            "categoryId": cat["id"], "baseUnitId": units["PK"], "minStockQty": min_stock,
            "isLotTracked": True, "hasExpiry": False, "shelfLifeDays": None,
            "units": [{"unitId": units[unit_code], "factorToBase": factor}],
            "barcodes": [{"value": ean_pack, "unitId": None}, {"value": ean_unit, "unitId": units[unit_code]}],
        })
        log(f"+ product {sku}: 1 {unit_code} = {factor} PK, barcodes {ean_pack} (pack), {ean_unit} ({unit_code})")

    # Bins
    bins = {}
    for code, barcode in BINS:
        found = api.call("GET", f"/warehouses/{wh['id']}/locations", query={"search": code, "pageSize": 50})["items"]
        b = next((x for x in found if x["code"] == code), None)
        if b is None:
            b = api.call("POST", f"/warehouses/{wh['id']}/locations", {"code": code, "type": "storage", "barcode": barcode})
            log(f"+ bin {code} (label {barcode})")
        bins[code] = b

    # Customers
    customers = {}
    for c in CUSTOMERS:
        found = api.call("GET", "/customers", query={"search": c["reference"], "pageSize": 10})["items"]
        cu = next((x for x in found if x["reference"] == c["reference"]), None)
        if cu is None:
            cu = api.call("POST", "/customers", c)
            log(f"+ customer {c['reference']} · {c['companyName']}")
        customers[c["reference"]] = cu

    # Demo users (the dispatch approver must not be the person who created it)
    for email, display, role in USERS:
        found = api.call("GET", "/users", query={"search": email, "pageSize": 10})["items"]
        if not any(u["email"].lower() == email for u in found):
            api.call("POST", "/users", {"email": email, "displayName": display, "password": DEMO_PASSWORD, "roles": [role],
                                        "allWarehouses": False, "warehouseIds": [wh["id"]]})
            log(f"+ user {email} ({role}), password {DEMO_PASSWORD}")

    # Stock and dispatches: only once
    stock = api.call("GET", f"/inventory/products/{products['HC-TR4-JAS']['id']}", query={"warehouseId": wh["id"]})
    here = next((w for w in stock["warehouses"] if w["warehouseId"] == wh["id"]), None)
    if here and here["onHand"] > 0:
        log("= stock already received: receipt and dispatches skipped")
        log("\nDone.")
        return

    def unit_of(sku: str, code: str) -> str:
        return next(u["id"] for u in products[sku]["units"] if u["unitCode"] == code)

    today = dt.date.today()
    lot = today.strftime("%y%m")
    # (sku, unit, quantity in that unit, bin, lot)
    lines = [
        ("HC-TR4-JAS", "CTN", 30, "A-01", f"L{lot}-04A"),
        ("HC-TR4-JAS", "CTN", 10, "A-01", f"L{lot}-04B"),
        ("HC-TR8-JAS", "CTN", 25, "A-02", f"L{lot}-08A"),
        ("HC-TR30-JAS", "PAL", 3, "B-01", f"L{lot}-30A"),
        ("HC-TR60-JAS", "PAL", 4, "B-02", f"L{lot}-60A"),
    ]
    receipt = api.call("POST", "/receipts", {
        "warehouseId": wh["id"], "receiptDate": today.isoformat(), "supplierName": "HELLO Cherie production",
        "supplierReference": f"DEMO-{lot}", "notes": "Demo data",
        "lines": [{"productId": products[s]["id"], "productUnitId": unit_of(s, u), "quantity": q, "expectedQuantity": q,
                   "locationId": bins[b]["id"], "lotNo": l, "expiryDate": None, "notes": None} for s, u, q, b, l in lines],
    })
    api.call("POST", f"/receipts/{receipt['id']}/post", idempotent=True)
    log(f"+ receipt {receipt['number']} posted: 480 PK of 4 rolls, 150 PK of 8 rolls, 72 PK of 30 rolls, 48 PK of 60 rolls")

    # Dispatches: (customer, days until requested date, lines [(sku, unit or None, qty)], final state)
    plans = [
        ("DEMO-HOTEL", 1, [("HC-TR4-JAS", "CTN", 5), ("HC-TR8-JAS", "CTN", 2)], "approved"),
        ("DEMO-SPA", 2, [("HC-TR30-JAS", None, 6)], "approved"),
        ("DEMO-OFFICE", 3, [("HC-TR60-JAS", None, 10), ("HC-TR4-JAS", None, 24)], "submitted"),
        ("DEMO-HOTEL", 7, [("HC-TR8-JAS", None, 12)], "draft"),
    ]
    created = []
    for i, (ref, days, dlines, state) in enumerate(plans, start=1):
        d = api.call("POST", "/dispatches", {
            "warehouseId": wh["id"], "customerId": customers[ref]["id"], "deliveryAddressId": None,
            "requestedDate": (today + dt.timedelta(days=days)).isoformat(), "customerOrderReference": f"PO-DEMO-{i:03d}",
            "notes": "Demo data",
            "lines": [{"productId": products[s]["id"], "productUnitId": unit_of(s, u) if u else None, "quantity": q, "notes": None}
                      for s, u, q in dlines],
        })
        if state != "draft":
            api.call("POST", f"/dispatches/{d['id']}/submit")
        created.append((d, state, ref))

    api.login(USERS[0][0], DEMO_PASSWORD)
    for d, state, ref in created:
        if state == "approved":
            api.call("POST", f"/dispatches/{d['id']}/approve", {"note": "Demo data"}, idempotent=True)
        log(f"+ dispatch {d['number']} for {ref}: {state}")

    log("\nDone. Sign in on the phone as operator.demo@hellocherie.test (or manager.demo@...) with "
        f"{DEMO_PASSWORD}: two dispatches are ready to pick, one waits for approval.")


if __name__ == "__main__":
    main()
