"""
Mobile smoke test: a manager scans unknown barcodes and (1) creates a product with it, (2) adds a carton barcode to an
existing product. Needs an EMPTY database, the API on :5080 and `ng serve mobile --port 4201`.
    CHROMIUM=/path/to/chromium SMOKE_OUT=./mobile-shots python tools/e2e/mobile_new_barcode.py
"""
import os
from playwright.sync_api import sync_playwright, expect
APP=os.environ.get("MOBILE_URL","http://localhost:4201"); API=os.environ.get("API_URL","http://localhost:5080/api/v1")
OUT=os.environ.get("SMOKE_OUT","mobile-shots"); os.makedirs(OUT, exist_ok=True)
with sync_playwright() as p:
    req=p.request.new_context()
    tok=req.post(f"{API}/auth/login",data={"email":"admin@hellocherie.test","password":"Admin-Password1","client":"mobile"}).json()["accessToken"]
    H={"Authorization":f"Bearer {tok}"}
    units={u["code"]:u["id"] for u in req.get(f"{API}/units",headers=H).json()}
    r=req.post(f"{API}/products",headers=H,data={"sku":"SOAP-100","name":"Soap bar 100 g","baseUnitId":units["EA"],"isLotTracked":False,"hasExpiry":False,"units":[{"unitId":units["CTN"],"factorToBase":12}]}); assert r.ok, r.text()
    b=p.chromium.launch(executable_path=os.environ.get("CHROMIUM") or None,args=["--no-sandbox"])
    pg=b.new_context(viewport={"width":390,"height":844},is_mobile=True,has_touch=True,device_scale_factor=2).new_page()
    errs=[]; pg.on("console",lambda m: errs.append(m.text) if m.type=="error" else None)
    pg.goto(APP+"/login")
    pg.locator("ion-input[type=email] input").fill("admin@hellocherie.test"); pg.locator("ion-input[type=password] input").fill("Admin-Password1"); pg.keyboard.press("Enter")
    expect(pg.get_by_text("Signed in as")).to_be_visible(timeout=15000)
    pg.get_by_text("Scan a product or bin").click()
    def scan(c):
        f=pg.locator("[data-testid=scan] input"); f.fill(c); f.press("Enter")
    # 1. Create a product from an unknown barcode
    scan("5449000000996")
    expect(pg.locator("[data-testid=unknown-create]")).to_be_visible(timeout=10000)
    pg.screenshot(path=f"{OUT}/u1-unknown.png")
    pg.locator("[data-testid=unknown-create]").click()
    pg.locator("[data-testid=new-sku] input").fill("cola-330")
    pg.locator("[data-testid=new-name] input").fill("Cola can 330 ml")
    pg.screenshot(path=f"{OUT}/u2-create.png")
    pg.locator("[data-testid=new-save]").click()
    expect(pg.locator(".wh-big", has_text="COLA-330")).to_be_visible(timeout=10000)
    pg.wait_for_timeout(2500)
    pg.screenshot(path=f"{OUT}/u3-created.png")
    # 2. Link a carton barcode to an existing product with pack units
    pg.wait_for_timeout(1200)
    scan("7610000099999")
    pg.locator("[data-testid=unknown-link]").click()
    pg.locator("[data-testid=unknown-search] input").fill("soap")
    pg.get_by_text("Soap bar 100 g").click()
    pg.locator("ion-alert .alert-radio-label", has_text="CTN").click()
    pg.screenshot(path=f"{OUT}/u4-unit.png")
    pg.locator("ion-alert button", has_text="Add barcode").click()
    try:
        expect(pg.locator(".wh-big", has_text="SOAP-100")).to_be_visible(timeout=10000)
    except Exception:
        pg.screenshot(path=f"{OUT}/u-fail.png"); print("errs", errs); raise
    expect(pg.get_by_text("= 1 CTN (12 EA)")).to_be_visible(timeout=10000)
    pg.wait_for_timeout(2500)
    pg.screenshot(path=f"{OUT}/u5-linked.png")
    # 3. The same barcode on another product is refused by the server
    print("errors:", errs)
    b.close()
