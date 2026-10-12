"""
Browser walkthrough of purchasing on top of the HELLO Cherie demo data:
web (manager): dashboard -> reorder suggestions -> draft order -> place -> receive against the demo order -> post ->
order received -> suppliers -> product reorder settings -> stock "on order"; phone (operator): expected deliveries ->
start a receipt from the open order -> counted lines pre-filled -> save.

Needs an EMPTY database, then `python tools/seed/hello_cherie_demo.py`, the API on :5080, `ng serve web` on :4200 and
`ng serve mobile` on :4201.
    CHROMIUM=/path/to/chromium SMOKE_OUT=./purchasing-shots python tools/e2e/purchasing_smoke.py
"""
import os
import re
from playwright.sync_api import sync_playwright, expect

WEB = os.environ.get("WEB_URL", "http://localhost:4200")
MOBILE = os.environ.get("MOBILE_URL", "http://localhost:4201")
API = os.environ.get("API_URL", "http://localhost:5080/api/v1")
OUT = os.environ.get("SMOKE_OUT", "purchasing-shots")
os.makedirs(OUT, exist_ok=True)
MANAGER = ("manager.demo@hellocherie.test", "Demo-Password1")
OPERATOR = ("operator.demo@hellocherie.test", "Demo-Password1")


def shot(page, name, full=True):
    page.screenshot(path=f"{OUT}/{name}.png", full_page=full)
    print("shot", name, flush=True)


with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=os.environ.get("CHROMIUM") or None, args=["--no-sandbox"])
    errors = []

    # ---------------------------------------------------------------- web, manager
    page = browser.new_context(viewport={"width": 1400, "height": 900}).new_page()
    page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    page.goto(f"{WEB}/login")
    page.fill("input[type=email], #email", MANAGER[0])
    page.fill("input[type=password]", MANAGER[1])
    page.keyboard.press("Enter")
    page.wait_for_url(f"{WEB}/", timeout=15000)
    expect(page.get_by_role("heading", name="Purchasing")).to_be_visible(timeout=15000)
    expect(page.get_by_text("products to reorder")).to_be_visible()
    shot(page, "p01-dashboard")

    # Reorder: the 60-roll pack is below its minimum; 4 pallets are on order, 2 more are suggested.
    page.get_by_role("link", name="Reorder").first.click()
    row = page.locator("tr", has_text="HC-TR60-JAS")
    expect(row).to_be_visible(timeout=15000)
    expect(row).to_contain_text("PO-MAIN-")
    expect(row.locator("input").last).to_have_value("2")
    shot(page, "p02-reorder")
    page.get_by_test_id("create-orders").click()
    page.wait_for_url(re.compile(r"/purchasing/orders/[0-9a-f-]{36}$"), timeout=15000)
    expect(page.get_by_role("heading", name=re.compile(r"PO-MAIN-\d{4}-000002"))).to_be_visible(timeout=15000)
    shot(page, "p03-draft-order")
    page.get_by_test_id("po-place").click()
    page.locator(".p-confirmdialog").get_by_role("button", name="Place order").click()
    expect(page.locator("h1 p-tag, h1 .p-tag").first).to_have_text("Ordered", timeout=15000)
    shot(page, "p04-placed")

    # Purchase order list, open only.
    page.goto(f"{WEB}/purchasing/orders?open=1")
    expect(page.locator("tbody tr")).to_have_count(2, timeout=15000)
    shot(page, "p05-order-list")

    # Receive the demo order: a draft receipt pre-filled with what is outstanding (4 PAL); enter the lot and post.
    page.locator("tbody tr", has_text="000001").click()
    page.get_by_test_id("po-receive").click()
    page.wait_for_url(re.compile(r"/receipts/[0-9a-f-]{36}$"), timeout=15000)
    expect(page.get_by_text("For purchase order")).to_be_visible(timeout=15000)
    page.locator("#l0").fill("L-PO-0001")
    shot(page, "p06-receipt-from-order")
    page.get_by_role("button", name="Post").click()
    page.locator(".p-confirmdialog").get_by_role("button", name="Post").click()
    expect(page.locator("h1").get_by_text("Posted")).to_be_visible(timeout=15000)
    page.get_by_role("link", name=re.compile(r"PO-MAIN-\d{4}-000001")).click()
    expect(page.locator("h1").get_by_text("Received")).to_be_visible(timeout=15000)
    shot(page, "p07-order-received")

    # Suppliers: create one in the dialog.
    page.goto(f"{WEB}/purchasing/suppliers")
    expect(page.get_by_text("HELLO Cherie production")).to_be_visible(timeout=15000)
    page.get_by_test_id("new-supplier").click()
    page.get_by_test_id("supplier-code").fill("PACK-CO")
    page.get_by_test_id("supplier-name").fill("Demo Packaging Ltd")
    page.locator("#leadTimeDays").fill("7")
    shot(page, "p08-supplier-dialog", full=False)
    page.get_by_test_id("supplier-save").click()
    expect(page.locator("tbody").get_by_text("Demo Packaging Ltd")).to_be_visible(timeout=15000)

    # Product reorder settings and the stock overview's "on order".
    page.goto(f"{WEB}/products")
    page.get_by_text("HC-TR60-JAS").click()
    expect(page.locator("#maxStock")).to_have_value(re.compile(r"^120"), timeout=15000)
    page.get_by_text("Usual supplier").scroll_into_view_if_needed()
    shot(page, "p09-product-reorder", full=False)
    page.goto(f"{WEB}/inventory")
    expect(page.locator("tr", has_text="HC-TR60-JAS")).to_contain_text("24", timeout=15000)
    shot(page, "p10-stock-on-order")

    # ---------------------------------------------------------------- phone, operator
    phone = browser.new_context(viewport={"width": 390, "height": 844}, is_mobile=True, has_touch=True, device_scale_factor=2).new_page()
    phone.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
    phone.goto(f"{MOBILE}/login")
    phone.locator("ion-input[type=email] input").fill(OPERATOR[0])
    phone.locator("ion-input[type=password] input").fill(OPERATOR[1])
    phone.keyboard.press("Enter")
    expect(phone.get_by_text("Signed in as")).to_be_visible(timeout=15000)
    phone.get_by_text("Receive goods").click()
    expect(phone.get_by_text("Expected deliveries")).to_be_visible(timeout=15000)
    order = phone.locator("[data-testid^='po-PO-MAIN-']")
    expect(order).to_have_count(1, timeout=15000)
    shot(phone, "p11-phone-expected", full=False)
    order.click()
    phone.locator("ion-alert button", has_text="Start").click()
    expect(phone.get_by_test_id("receipt-po")).to_be_visible(timeout=15000)
    expect(phone.get_by_text("Expected 2 PAL")).to_be_visible()
    phone.get_by_test_id("receipt-lot-HC-TR60-JAS").locator("input").fill("L-PO-0002")
    phone.get_by_test_id("receipt-save").click()
    phone.wait_for_timeout(1500)
    shot(phone, "p12-phone-receipt", full=False)

    browser.close()
    print("console errors:", errors)
