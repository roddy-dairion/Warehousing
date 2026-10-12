# 10 — Warehouse mobile app (Phase 7)

Status: **Built and tested in a phone-sized browser on 2026-10-12. Not yet tested on real phones.** That test needs the warehouse's phones (see the checklist below). The Android and iOS projects are ready; the app has not been compiled into an APK or IPA yet. This environment cannot download the Android SDK, and iOS builds need a Mac.

Ionic 9 + Angular 21 + Capacitor 8, in `frontend/projects/mobile`. It uses the same `@wh/shared` models, API clients, auth and helpers as the web app. Online only: every stock action waits for the server's answer, and nothing is queued offline.

## Screens

| Screen | Who (permission) | What it does |
|---|---|---|
| Login, choose warehouse | everyone | Refresh token in the iOS Keychain / Android Keystore. The warehouse choice only appears for users with several warehouses. |
| Home | everyone | The actions the user's role allows. Counts come from the dashboard (dispatches waiting, draft receipts). Pull down to refresh. |
| **Scan** | `products.read` | Scan anything. A **product or carton barcode** shows the product, what the barcode stands for (e.g. 1 CTN = 12 EA), lot/expiry flags, and stock in this warehouse (on hand / reserved / available, by bin and lot). A **bin label** shows what is in the bin. **Other text** searches SKU and name. Unknown and inactive items get a clear warning. |
| Product stock | `inventory.read` | Stock by bin and lot and the latest 25 movements. |
| **Dispatches** | `dispatches.read` + pick / ship / deliver | Tabs **To pick** (approved, picking; "only assigned to me" filter), **To ship** (ready), **To deliver** (dispatched). Each tab only appears if the user holds that permission. |
| **Picking** | `dispatches.pick` | Guided steps. The next task is shown large (bin, product, lot, quantity left). The picker scans the **bin label**, then the **product or carton**, then checks the lot and confirms the quantity. Whole cartons are proposed (24 EA left → "Pick 2 CTN (24 EA)"). Other buttons: take this job, choose another bin (reallocate), report a problem (missing / damaged / unexpected / wrong lot / other), undo recent picks, picking done (asks before finishing short). |
| **Ship** | `dispatches.ship` | Picked vs ordered per line, carrier, tracking. When short: backorder or close short. Asks for confirmation; deducts the stock once. |
| **Delivery** | `dispatches.deliver` | Receiver's name, photos (camera; scaled to 1600 px JPEG before upload), signature drawn on the screen, notes. All three are required (Q13). The signed delivery note is emailed by the server. |
| **Receive goods** | `inventory.receive` (count, save draft), `inventory.receive.post` (post) | Draft receipts, or a new one. Each scan adds the product or carton, or adds one more to its line. Per line: quantity (− / +), bin (scan the label; empty = RECEIVING), lot and expiry. An operator saves the draft; a manager posts it (from the phone or the web). |
| Recent activity | `inventory.read` | Latest stock movements in the warehouse, endless scroll. |

Permissions only shape what the app shows. The API checks every action again.

## Scanning

* **Camera:** `@capacitor-mlkit/barcode-scanning` 8.2.1 in custom-overlay mode (`startScan`). The camera preview is drawn behind the web view; while scanning, the app is hidden (`body.wh-scanning`) and only `wh-scanner-overlay` shows: the prompt ("Scan the bin label"), a frame, a **Light** (torch) button where the phone has one, and **Cancel**. The Android back button also cancels. One code per scan; all 13 ML Kit formats are accepted (EAN-13/8, UPC, Code 128/39/93, ITF, QR, Data Matrix, GS1 in Code 128…).
* **Camera permission:** asked the first time. If refused, the app offers "Open settings" and keeps working with typed or scanner-gun input.
* **Scanner gun / typed (keyboard wedge):** every scan field accepts typed input followed by Enter. USB or Bluetooth scanners that type the code then press Enter work without setup. In a desktop browser this is the only way (there is no camera there).
* **Double reads:** the same code twice within 1 second counts once. On the receipt screen, scans are handled one after another, so two cartons scanned quickly both count.
* A scan only *identifies* a product, bin or lot. The server decides whether the pick or receipt is allowed and how much.

## No double counting

| Action | Protection |
|---|---|
| Pick | Each scan has a `scanId`. After a network failure the same `scanId` is kept for the retry, so the server replays instead of picking twice. The message says "tap again: it will not be counted twice". |
| Undo pick | Its own `scanId`. |
| Ship, post receipt | `Idempotency-Key` kept until success. |
| Delivery | If the upload's answer is lost, the app reloads the dispatch; if it is already delivered, it reports success instead of an error. |
| Buttons | Disabled while a request runs. |

## Native projects

* **Android** (`projects/mobile/android`): `CAMERA` permission (scanning and delivery photos), ML Kit metadata. minSdk 24, target 36.
* **iOS** (`projects/mobile/ios`, new): CocoaPods, because ML Kit does not support Swift Package Manager. Deployment target 15.5 (ML Kit minimum). `NSCameraUsageDescription` and `NSPhotoLibraryUsageDescription` texts. `NSAllowsLocalNetworking` lets a debug build reach the API on the office PC over HTTP. `pod install` runs on the Mac (`npm run ios:sync`).
* Placeholder app id `com.warehousing.mobile` (Q22: confirm it before publishing to the stores, it cannot change afterwards).

Build and run: see [frontend/README.md](../frontend/README.md#testing-the-mobile-app).

## Tests run (2026-10-12)

* `ng test`: **50 passed** (shared 21, web 16, mobile 13). The new mobile tests cover:
  * the error messages (retry-safe network wording);
  * the scan field (trim, clear or keep the value, double reads ignored);
  * the quantity stepper;
  * the pick logic: bin before product; whole-carton proposal; product not on the dispatch; product allocated to another bin; scan id kept after a network failure but renewed after a rejected pick.
* `ng build mobile` and `ng build web` succeed; `lint:types` passes.
* Browser smoke test `tools/e2e/mobile_smoke.py` (Playwright, 390×844 touch phone, operator and manager on separate phones) on an empty database: **passed**. Steps:
  1. Scan a carton barcode, a bin label and an unknown code.
  2. Receive goods: 2 cartons with lot and expiry in bin A-01, 3 oil to RECEIVING; save the draft (an operator cannot post); the manager posts it.
  3. Pick: a wrong-bin warning, then 2 cartons by scan, then oil short (6 of 8); finish as partial.
  4. The manager ships with a backorder.
  5. The operator records delivery with a photo and a drawn signature.
  6. Recent activity; final stock checked through the API (soap 60, oil 5).

  The only browser errors are the expected 404s from looking up codes that are not product barcodes (a bin label and an unknown code).

## Still to do on real devices (the Phase 1 verification gate)

Install a debug build on the phones the warehouse will use (Android and iPhone) and check:

1. **Camera scan** of real product labels (EAN-13), carton labels, and bin labels, under the warehouse lighting, including the torch.
2. Behaviour after the camera permission is refused, then granted in settings.
3. **Scanner gun** (if used, Q10): it types into the focused field and sends Enter.
4. Delivery **photos** from the camera, and a **signature** drawn with a finger.
5. Wi-Fi dead spots: turn Wi-Fi off during a pick, then retry. The message should appear and the retry must not count twice.
6. Font size and button size with gloves; screen rotation during a signature.

If ML Kit struggles with your labels, plan B is `@capacitor/barcode-scanner` (doc 01). Only `ScannerService` would change.

## Not in this release

* Offline work (by design), push notifications, adjustments and bin moves from the phone (web only for now), printing labels.
