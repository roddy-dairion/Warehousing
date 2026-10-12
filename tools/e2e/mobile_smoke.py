"""
Browser smoke test of the warehouse mobile app (Phase 7) at phone size, with touch.

Covers: operator login -> home -> scan (carton barcode, bin label, name search) -> receive goods (scan, lot, expiry, bin,
save draft) -> manager posts it -> operator picks a dispatch (bin + carton scan, then a short pick) -> picking done
-> manager confirms shipment with backorder -> operator records delivery (photo + drawn signature) -> recent activity.

The camera is not available in a desktop browser, so codes are typed into the scan fields and confirmed with Enter
(exactly what a scanner gun does). Camera scanning must be tested on real phones (see docs/10-mobile-app.md).

Needs an EMPTY database (it creates users, products and bins), the API on :5080 and `ng serve mobile --port 4201`.
    CHROMIUM=/path/to/chromium SMOKE_OUT=./mobile-shots python tools/e2e/mobile_smoke.py
"""
import os
from playwright.sync_api import sync_playwright, expect

APP = os.environ.get("MOBILE_URL", "http://localhost:4201")
API = os.environ.get("API_URL", "http://localhost:5080/api/v1")
OUT = os.environ.get("SMOKE_OUT", "mobile-shots")
os.makedirs(OUT, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__))
ADMIN = ("admin@hellocherie.test", "Admin-Password1")
MANAGER = ("manager@hellocherie.test", "Manager-Password1")
OPERATOR = ("operator@hellocherie.test", "Operator-Password1")


def shot(page, name):
    page.screenshot(path=f"{OUT}/{name}.png", full_page=False)
    print("shot", name, flush=True)


def scan(page, testid, code):
    """Types a code into a scan field and presses Enter, like a scanner gun."""
    field = page.locator(f"[data-testid='{testid}'] input")
    field.fill(code)
    field.press("Enter")


with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.environ.get("CHROMIUM") or None, args=["--no-sandbox"])
    req = p.request.new_context()

    def token(who):
        return req.post(f"{API}/auth/login", data={"email": who[0], "password": who[1], "client": "mobile"}).json()["accessToken"]

    H = {"Authorization": f"Bearer {token(ADMIN)}"}
    wh = req.get(f"{API}/warehouses", headers=H).json()[0]
    units = {u["code"]: u["id"] for u in req.get(f"{API}/units", headers=H).json()}
    for who, role in ((MANAGER, "WH_MANAGER"), (OPERATOR, "WH_OPERATOR")):
        r = req.post(f"{API}/users", headers=H, data={"email": who[0], "displayName": "Maria Manager" if role == "WH_MANAGER" else "Oscar Operator",
                                                       "password": who[1], "roles": [role], "allWarehouses": False, "warehouseIds": [wh["id"]]})
        assert r.ok, r.text()
    soap = req.post(f"{API}/products", headers=H, data={"sku": "SOAP-100", "name": "Soap bar 100 g", "baseUnitId": units["EA"], "isLotTracked": True,
                                                         "hasExpiry": True, "minStockQty": 20, "units": [{"unitId": units["CTN"], "factorToBase": 12}],
                                                         "barcodes": [{"value": "7610000000017"}, {"value": "7610000000024", "unitId": units["CTN"]}]}).json()
    oil = req.post(f"{API}/products", headers=H, data={"sku": "OIL-1L", "name": "Sunflower oil 1 L", "baseUnitId": units["EA"], "isLotTracked": False,
                                                        "hasExpiry": False, "minStockQty": 10, "barcodes": [{"value": "7620000000016"}]}).json()
    a01 = req.post(f"{API}/warehouses/{wh['id']}/locations", headers=H, data={"code": "A-01", "type": "storage", "barcode": "BIN-A01"}).json()
    a02 = req.post(f"{API}/warehouses/{wh['id']}/locations", headers=H, data={"code": "A-02", "type": "storage", "barcode": "BIN-A02"}).json()
    customer = req.post(f"{API}/customers", headers=H, data={"reference": "SHOP-01", "companyName": "Le Petit Shop", "email": "orders@petitshop.test",
                                                             "addresses": [{"label": "Shop", "line1": "12 Royal Road", "city": "Curepipe", "country": "Mauritius",
                                                                            "isDefault": True}]}).json()
    # Opening stock: 5 cartons of soap (60) in A-01, 8 oil in A-02.
    receipt = req.post(f"{API}/receipts", headers=H, data={"warehouseId": wh["id"], "receiptDate": None, "supplierName": "Opening stock", "lines": [
        {"productId": soap["id"], "productUnitId": soap["units"][0]["id"], "quantity": 5, "locationId": a01["id"], "lotNo": "L2401", "expiryDate": "2027-12-31"},
        {"productId": oil["id"], "productUnitId": None, "quantity": 8, "locationId": a02["id"]}]}).json()
    assert req.post(f"{API}/receipts/{receipt['id']}/post", headers=H).ok
    # A dispatch created by the admin and approved by the manager: 24 soap, 8 oil.
    d = req.post(f"{API}/dispatches", headers=H, data={"warehouseId": wh["id"], "customerId": customer["id"], "deliveryAddressId": None,
                                                        "requestedDate": None, "customerOrderReference": "PO-77", "notes": "Fragile: oil on top",
                                                        "lines": [{"productId": soap["id"], "quantity": 24}, {"productId": oil["id"], "quantity": 8}]}).json()
    assert "id" in d, d
    assert req.post(f"{API}/dispatches/{d['id']}/submit", headers=H).ok
    HM = {"Authorization": f"Bearer {token(MANAGER)}"}
    r = req.post(f"{API}/dispatches/{d['id']}/approve", headers=HM, data={"note": None})
    assert r.ok, r.text()

    def phone(who):
        ctx = browser.new_context(viewport={"width": 390, "height": 844}, device_scale_factor=2, is_mobile=True, has_touch=True)
        page = ctx.new_page()
        errors = []
        page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
        page.goto(f"{APP}/login")
        page.locator("ion-input[type=email] input, ion-input[formcontrolname=email] input").first.fill(who[0])
        page.locator("ion-input[type=password] input, ion-input[formcontrolname=password] input").first.fill(who[1])
        page.keyboard.press("Enter")
        expect(page.get_by_text("Signed in as")).to_be_visible(timeout=15000)
        return page, errors

    op, op_errors = phone(OPERATOR)
    op.wait_for_timeout(1000)
    shot(op, "m01-home")

    # ---- Scan: carton barcode, bin label, name search
    op.get_by_text("Scan a product or bin").click()
    scan(op, "scan", "7610000000024")
    expect(op.get_by_text("1 CTN (12 EA)")).to_be_visible(timeout=10000)
    op.wait_for_timeout(400)
    shot(op, "m02-scan-carton")
    scan(op, "scan", "BIN-A02")
    expect(op.get_by_text("Bin A-02")).to_be_visible(timeout=10000)
    shot(op, "m03-scan-bin")
    scan(op, "scan", "nothing-like-this")
    expect(op.get_by_text('Unknown code "nothing-like-this"')).to_be_visible(timeout=10000)
    shot(op, "m04-scan-unknown")
    op.goto(f"{APP}/")

    # ---- Receive goods: 2 cartons of soap (lot L2402) into A-01, 3 oil (scanned 3 times) into RECEIVING
    expect(op.get_by_text("Receive goods")).to_be_visible(timeout=10000)
    op.get_by_text("Receive goods").click()
    op.locator("[data-testid=receipt-new]").click()
    op.locator("[data-testid=receipt-supplier] input").fill("Savonnerie Ltd")
    scan(op, "receipt-scan", "7610000000024")
    expect(op.locator("[data-testid='receipt-qty-SOAP-100']")).to_be_visible(timeout=10000)
    op.wait_for_timeout(1100)  # the same code twice within 1 s is treated as one (double read)
    scan(op, "receipt-scan", "7610000000024")
    op.wait_for_timeout(1100)
    for _ in range(3):
        scan(op, "receipt-scan", "7620000000016")
        op.wait_for_timeout(1100)
    op.locator("[data-testid='receipt-lot-SOAP-100'] input").fill("L2402")
    op.locator("[data-testid='receipt-expiry-SOAP-100'] input").fill("2028-03-31")
    scan(op, "receipt-bin-SOAP-100", "BIN-A01")
    op.wait_for_timeout(300)
    soap_qty = op.locator("[data-testid='receipt-qty-SOAP-100'] input").input_value()
    oil_qty = op.locator("[data-testid='receipt-qty-OIL-1L'] input").input_value()
    assert soap_qty == "2" and oil_qty == "3", (soap_qty, oil_qty)
    shot(op, "m05-receipt-lines")
    op.locator("[data-testid=receipt-save]").click()
    expect(op.get_by_text("saved as draft")).to_be_visible(timeout=10000)
    op.wait_for_timeout(500)
    shot(op, "m06-receipt-saved")
    assert op.locator("[data-testid=receipt-post]").count() == 0  # operators cannot post

    # Manager posts it from their phone.
    mgr, mgr_errors = phone(MANAGER)
    mgr.get_by_text("Receive goods").click()
    mgr.get_by_text("GR-MAIN-2026-000002").click()
    mgr.locator("[data-testid=receipt-post]").click()
    mgr.locator("ion-alert button", has_text="Post").click()
    expect(mgr.get_by_text("posted. Stock updated")).to_be_visible(timeout=10000)
    shot(mgr, "m07-receipt-posted")

    # ---- Picking: operator, guided by bin + barcode
    op.goto(f"{APP}/dispatches")
    expect(op.get_by_text(d["number"])).to_be_visible(timeout=10000)
    shot(op, "m08-dispatch-list")
    op.get_by_text(d["number"]).click()
    expect(op.locator("[data-testid=next-task]")).to_be_visible(timeout=10000)
    shot(op, "m09-pick-start")
    # Wrong product first: oil while standing at the soap bin.
    scan(op, "pick-bin", "BIN-A01")
    scan(op, "pick-product", "7620000000016")
    expect(op.get_by_text("is allocated to bin A-02")).to_be_visible(timeout=10000)
    op.locator("ion-button", has_text="Cancel").first.click()
    # Soap: carton barcode -> 2 cartons proposed (24 EA left).
    scan(op, "pick-bin", "BIN-A01")
    scan(op, "pick-product", "7610000000024")
    expect(op.locator("[data-testid=pick-confirm]")).to_contain_text("Pick 2 CTN (24 EA)", timeout=10000)
    shot(op, "m10-pick-confirm")
    op.locator("[data-testid=pick-confirm]").click()
    expect(op.get_by_text("SOAP-100: 24 picked from A-01")).to_be_visible(timeout=10000)
    # Oil: only 6 of 8 found.
    scan(op, "pick-bin", "BIN-A02")
    scan(op, "pick-product", "7620000000016")
    expect(op.locator("[data-testid=pick-confirm]")).to_contain_text("Pick 8 EA", timeout=10000)
    op.locator("[data-testid=pick-qty] input").fill("6")
    op.locator("[data-testid=pick-confirm]").click()
    expect(op.get_by_text("OIL-1L: 6 picked from A-02")).to_be_visible(timeout=10000)
    op.wait_for_timeout(800)
    shot(op, "m11-picked")
    op.locator("[data-testid=pick-done]").click()
    op.locator("ion-alert button", has_text="Finish partial").click()
    expect(op.get_by_text("Picked and ready")).to_be_visible(timeout=10000)
    shot(op, "m12-ready")

    # ---- Ship: manager, backorder for the 2 missing oil
    mgr.goto(f"{APP}/dispatches/{d['id']}")
    expect(mgr.locator("[data-testid=ship-confirm]")).to_be_visible(timeout=10000)
    mgr.locator("[data-testid=ship-carrier] input").fill("Own van")
    mgr.locator("[data-testid=ship-backorder]").click()
    shot(mgr, "m13-ship")
    mgr.locator("[data-testid=ship-confirm]").click()
    mgr.locator("ion-alert button", has_text="Ship").click()
    expect(mgr.get_by_text("Shipped. Stock deducted")).to_be_visible(timeout=15000)

    # ---- Deliver: operator with photo + signature
    op.goto(f"{APP}/dispatches/{d['id']}")
    expect(op.locator("[data-testid=pod-receiver]")).to_be_visible(timeout=10000)
    op.locator("[data-testid=pod-receiver] input").fill("John Receiver")
    op.set_input_files("[data-testid=pod-photos]", os.path.join(HERE, "photo.png"))
    expect(op.locator("img[alt='Delivery photo 1']")).to_be_visible(timeout=10000)
    canvas = op.locator("wh-signature-pad canvas")
    canvas.scroll_into_view_if_needed()
    box = canvas.bounding_box()
    op.mouse.move(box["x"] + 30, box["y"] + box["height"] * 0.7)
    op.mouse.down()
    for i in range(1, 25):
        op.mouse.move(box["x"] + 30 + i * 12, box["y"] + box["height"] * (0.7 - 0.4 * ((i % 8) / 8)))
    op.mouse.up()
    op.wait_for_timeout(300)
    shot(op, "m14-deliver")
    op.locator("[data-testid=pod-save]").click()
    expect(op.get_by_text("Delivery recorded")).to_be_visible(timeout=20000)
    op.wait_for_timeout(1000)
    shot(op, "m15-delivered")

    # ---- Recent activity
    op.goto(f"{APP}/activity")
    expect(op.get_by_text("Dispatched").first).to_be_visible(timeout=10000)
    shot(op, "m16-activity")

    # Stock check through the API: soap 60 + 24 received - 24 shipped = 60; oil 8 + 3 - 6 = 5.
    stock = {s["sku"]: s for s in req.get(f"{API}/inventory/summary?warehouseId={wh['id']}", headers=H).json()["items"]}
    assert stock["SOAP-100"]["onHand"] == 60, stock["SOAP-100"]
    assert stock["OIL-1L"]["onHand"] == 5, stock["OIL-1L"]
    print("stock ok:", {k: v["onHand"] for k, v in stock.items()})
    print("operator console errors:", [e for e in op_errors if "favicon" not in e][:10])
    print("manager console errors:", [e for e in mgr_errors if "favicon" not in e][:10])
    browser.close()
