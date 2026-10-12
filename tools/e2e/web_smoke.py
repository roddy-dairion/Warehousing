"""
Browser smoke test of the web app (Phase 6): receipt -> stock -> dispatch -> approve (shortage, fix) -> pick with
barcodes -> short ship with backorder -> delivery with photo and drawn signature -> admin pages -> phone width.

Needs an EMPTY database (it creates users, products and bins), the API on :5080 and `ng serve web` on :4200.
    pip install playwright && python -m playwright install chromium   (or set CHROMIUM=/path/to/chromium)
    SMOKE_OUT=./smoke-shots python tools/e2e/web_smoke.py
Screenshots are written to SMOKE_OUT; the script fails on the first broken step.
"""
import os
from playwright.sync_api import sync_playwright, expect

WEB = "http://localhost:4200"
API = "http://localhost:5080/api/v1"
OUT = os.environ.get("SMOKE_OUT", "smoke-shots")
os.makedirs(OUT, exist_ok=True)
ADMIN = ("admin@hellocherie.test", "Admin-Password1")
MANAGER = ("manager@hellocherie.test", "Manager-Password1")

def shot(page, name):
    page.screenshot(path=f"{OUT}/{name}.png", full_page=True)
    print("shot", name, flush=True)

with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.environ.get("CHROMIUM") or None, args=["--no-sandbox"])
    req = p.request.new_context()
    tok = req.post(f"{API}/auth/login", data={"email": ADMIN[0], "password": ADMIN[1], "client": "mobile"}).json()["accessToken"]
    H = {"Authorization": f"Bearer {tok}"}
    wh = req.get(f"{API}/warehouses", headers=H).json()[0]
    units = {u["code"]: u["id"] for u in req.get(f"{API}/units", headers=H).json()}
    r = req.post(f"{API}/users", headers=H, data={"email": MANAGER[0], "displayName": "Maria Manager", "password": MANAGER[1], "roles": ["WH_MANAGER"], "allWarehouses": False, "warehouseIds": [wh["id"]]})
    assert r.ok, r.text()
    r = req.post(f"{API}/products", headers=H, data={"sku": "SOAP-100", "name": "Soap bar 100 g", "baseUnitId": units["EA"], "isLotTracked": True, "hasExpiry": True, "minStockQty": 20,
        "units": [{"unitId": units["CTN"], "factorToBase": 12}], "barcodes": [{"value": "7610000000017"}, {"value": "7610000000024", "unitId": units["CTN"]}]})
    assert r.ok, r.text()
    req.post(f"{API}/products", headers=H, data={"sku": "OIL-1L", "name": "Sunflower oil 1 L", "baseUnitId": units["EA"], "isLotTracked": False, "hasExpiry": False, "minStockQty": 10})
    r = req.post(f"{API}/warehouses/{wh['id']}/locations", headers=H, data={"code": "A-01", "type": "storage", "barcode": "BIN-A01"})
    assert r.ok, r.text()
    req.post(f"{API}/warehouses/{wh['id']}/locations", headers=H, data={"code": "A-02", "type": "storage"})
    r = req.post(f"{API}/customers", headers=H, data={"reference": "SHOP-01", "companyName": "Le Petit Shop", "contactName": "Jane Buyer", "email": "orders@petitshop.test",
        "addresses": [{"label": "Shop", "line1": "12 Royal Road", "city": "Curepipe", "country": "Mauritius", "isDefault": True}]})
    assert r.ok, r.text()

    def login(ctx, who):
        page = ctx.new_page()
        page.set_viewport_size({"width": 1400, "height": 900})
        page.goto(f"{WEB}/login")
        page.fill("input[type=email], #email", who[0])
        page.fill("input[type=password]", who[1])
        page.keyboard.press("Enter")
        page.wait_for_url(f"{WEB}/", timeout=15000)
        return page

    admin = login(browser.new_context(), ADMIN)
    errors = []
    admin.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    admin.wait_for_timeout(1500)
    shot(admin, "01-dashboard-empty")

    # ---- Goods receipt via the UI
    admin.goto(f"{WEB}/receipts/new")
    admin.fill("#supplierName", "Savonnerie Ltd")
    admin.fill("#p0", "SOAP")
    admin.get_by_role("option", name="SOAP-100 · Soap bar 100 g").click()
    admin.fill("#q0", "5")
    admin.locator("#u0").click(); admin.get_by_role("option", name="CTN (×12)").click()
    admin.locator("#b0").click(); admin.get_by_role("option", name="A-01", exact=True).click()
    admin.fill("#l0", "L2401")
    admin.locator("#x0").click(); admin.locator("#x0").press_sequentially("31.12.2027", delay=30); admin.keyboard.press("Enter"); admin.wait_for_timeout(200)
    admin.get_by_role("button", name="Add line").click()
    admin.fill("#p1", "OIL")
    admin.get_by_role("option", name="OIL-1L · Sunflower oil 1 L").click()
    admin.fill("#q1", "8")
    admin.locator("#b1").click(); admin.get_by_role("option", name="A-02", exact=True).click()
    admin.keyboard.press("Escape")
    admin.wait_for_timeout(300)
    shot(admin, "02-receipt-draft")
    admin.get_by_role("button", name="Post").click()
    admin.wait_for_timeout(500)
    shot(admin, "02b-confirm")
    admin.locator(".p-confirmdialog").get_by_role("button", name="Post").click()
    try:
        expect(admin.get_by_text("Posted").first).to_be_visible(timeout=15000)
    except Exception:
        shot(admin, "02c-fail"); print("console:", errors); raise
    admin.wait_for_timeout(800)
    shot(admin, "03-receipt-posted")

    admin.goto(f"{WEB}/inventory")
    expect(admin.get_by_text("SOAP-100").first).to_be_visible(timeout=10000)
    admin.wait_for_timeout(500)
    shot(admin, "04-stock")
    admin.get_by_text("SOAP-100").first.click()
    admin.wait_for_timeout(1200)
    shot(admin, "05-stock-drawer")
    admin.keyboard.press("Escape")

    # ---- Dispatch via the UI (admin creates and submits)
    admin.goto(f"{WEB}/dispatches/new")
    admin.fill("#customer", "Petit")
    admin.get_by_role("option", name="Le Petit Shop (SHOP-01)").click()
    admin.fill("#ref", "PO-4567")
    admin.fill("#p0", "SOAP")
    admin.get_by_role("option", name="SOAP-100 · Soap bar 100 g").click()
    admin.fill("#q0", "24")
    admin.get_by_role("button", name="Add line").click()
    admin.fill("#p1", "OIL")
    admin.get_by_role("option", name="OIL-1L · Sunflower oil 1 L").click()
    admin.fill("#q1", "10")
    admin.wait_for_timeout(800)
    shot(admin, "06-dispatch-new")
    admin.get_by_role("button", name="Save and submit").click()
    admin.wait_for_url("**/dispatches/*", timeout=15000)
    expect(admin.get_by_text("Awaiting approval").first).to_be_visible(timeout=10000)
    dispatch_url = admin.url
    shot(admin, "07-dispatch-submitted")

    # ---- Manager approves (shortage on oil: only 8), returns, etc.
    mgr = login(browser.new_context(), MANAGER)
    mgr_errors = []
    mgr.on("console", lambda m: mgr_errors.append(m.text) if m.type == "error" else None)
    mgr.goto(dispatch_url)
    mgr.get_by_role("button", name="Approve").click()
    mgr.get_by_role("button", name="Approve and reserve").click()
    expect(mgr.get_by_text("Not enough available stock").first).to_be_visible(timeout=10000)
    shot(mgr, "08-approve-shortage")
    mgr.get_by_role("button", name="Back").click()

    # Admin fixes the dispatch: return to draft is done by the manager, admin edits quantity to 8.
    mgr.get_by_role("button", name="Return to draft").click()
    mgr.fill("#reason", "Only 8 oil in stock")
    mgr.locator(".p-dialog").get_by_role("button", name="Return to draft").click()
    expect(mgr.get_by_text("Draft").first).to_be_visible(timeout=10000)
    admin.goto(dispatch_url + "/edit")
    admin.fill("#q1", "8")
    admin.get_by_role("button", name="Save and submit").click()
    expect(admin.get_by_text("Awaiting approval").first).to_be_visible(timeout=10000)

    mgr.goto(dispatch_url)
    mgr.get_by_role("button", name="Approve").click()
    mgr.get_by_role("button", name="Approve and reserve").click()
    expect(mgr.locator("#scanCode")).to_be_visible(timeout=10000)
    mgr.wait_for_timeout(800)
    shot(mgr, "09-approved-picklist")

    # Pick soap with the scanner box (carton barcode = 12, twice) and oil via the task dialog (only 6 of 8 → short).
    mgr.fill("#scanBin", "BIN-A01")
    mgr.fill("#scanCode", "7610000000024")
    mgr.fill("#scanLot", "L2401")
    mgr.locator("form.scan").get_by_role("button", name="Pick").click()
    mgr.wait_for_timeout(1200)
    mgr.fill("#scanCode", "7610000000024")
    mgr.locator("form.scan").get_by_role("button", name="Pick").click()
    mgr.wait_for_timeout(1200)
    oil_row = mgr.locator("tr", has_text="OIL-1L").last
    oil_row.get_by_role("button", name="Pick").click()
    mgr.fill("#pickQty", "6")
    mgr.get_by_role("button", name="Picked").click()
    mgr.wait_for_timeout(1200)
    shot(mgr, "10-picking")
    mgr.get_by_role("button", name="Picking done").click()
    mgr.locator(".p-confirmdialog").get_by_role("button", name="Confirm partial").click()
    expect(mgr.get_by_role("button", name="Ship")).to_be_visible(timeout=10000)

    mgr.get_by_role("button", name="Ship").click()
    mgr.fill("#carrier", "Own van")
    mgr.fill("#tracking", "VAN-2")
    mgr.locator("label[for=backorder]").click()
    shot(mgr, "11-ship-dialog")
    mgr.locator(".p-dialog").get_by_role("button", name="Ship").click()
    expect(mgr.get_by_text("Dispatched").first).to_be_visible(timeout=15000)
    mgr.wait_for_timeout(2500)
    mgr.reload()
    mgr.wait_for_timeout(1500)
    shot(mgr, "12-dispatched")

    # Record delivery: name, photo, signature drawn with the mouse.
    mgr.get_by_role("button", name="Record delivery").click()
    mgr.fill("#receiver", "John Receiver")
    mgr.set_input_files("#photos", os.path.join(os.path.dirname(os.path.abspath(__file__)), "photo.png"))
    mgr.wait_for_timeout(500)
    box = mgr.locator("wh-signature-pad canvas").bounding_box()
    mgr.mouse.move(box["x"] + 20, box["y"] + box["height"] * 0.7)
    mgr.mouse.down()
    for i in range(1, 30):
        mgr.mouse.move(box["x"] + 20 + i * 12, box["y"] + box["height"] * (0.7 - 0.4 * ((i % 10) / 10)))
    mgr.mouse.up()
    shot(mgr, "13-deliver-dialog")
    mgr.get_by_role("button", name="Save delivery").click()
    expect(mgr.get_by_text("Proof of delivery")).to_be_visible(timeout=20000)
    mgr.wait_for_timeout(3000)
    mgr.reload()
    mgr.wait_for_timeout(2500)
    shot(mgr, "14-delivered")

    # ---- Admin pages
    admin.goto(f"{WEB}/")
    admin.wait_for_timeout(1500)
    shot(admin, "15-dashboard")
    admin.goto(f"{WEB}/settings/templates")
    admin.wait_for_timeout(6000)
    shot(admin, "16-templates")
    admin.goto(f"{WEB}/settings/company")
    admin.wait_for_timeout(1500)
    shot(admin, "17-company")
    admin.goto(f"{WEB}/reports")
    admin.locator("p-select").first.click()
    admin.get_by_role("option", name="Short shipments and picking discrepancies").click()
    admin.get_by_role("button", name="Show").click()
    admin.wait_for_timeout(1500)
    shot(admin, "18-reports")
    admin.goto(f"{WEB}/audit")
    admin.wait_for_timeout(1500)
    shot(admin, "19-audit")
    admin.goto(f"{WEB}/transfers")
    admin.get_by_role("button", name="New move").click()
    admin.wait_for_timeout(500)
    shot(admin, "20-move-dialog")

    # Mobile-width check of the dispatch page.
    mgr.set_viewport_size({"width": 390, "height": 844})
    mgr.goto(dispatch_url)
    mgr.wait_for_timeout(2000)
    shot(mgr, "21-dispatch-mobile")

    print("admin console errors:", [e for e in errors if "favicon" not in e][:10])
    print("manager console errors:", [e for e in mgr_errors if "favicon" not in e][:10])
    browser.close()
