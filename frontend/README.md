# Frontend workspace

One Angular CLI workspace with three projects:

| Project | What it is | Path |
|---|---|---|
| `shared` | Library used by both apps: auth service, HTTP interceptors, API error model, permissions, warehouse context, guards. Imported as `@wh/shared` (compiled from source, no separate build). | `projects/shared` |
| `web` | Admin and warehouse-management web app (Angular + PrimeNG). | `projects/web` |
| `mobile` | Warehouse staff app (Ionic + Angular + Capacitor). | `projects/mobile` |

## Requirements

* Node.js 22 LTS (≥ 22.12) or 24 LTS
* npm 10+

## Commands

```bash
npm ci                 # install exact locked versions
npm start              # web app on http://localhost:4200 (proxies /api to http://localhost:5080)
npm run start:mobile   # mobile app in the browser on http://localhost:4201
npm test               # unit tests for shared, web and mobile (Vitest)
npm run lint:types     # strict TypeScript check of both apps
npm run build          # production builds into dist/web and dist/mobile
```

## Testing the mobile app

**1. In the browser (quickest, no camera):** start the API, then `npm run start:mobile` and open http://localhost:4201. In Chrome DevTools, toggle the device toolbar (Ctrl+Shift+M) to get a phone-sized screen. There is no camera scanning in the browser: type the code and press Enter (a USB scanner gun works too, it types the code and presses Enter). Camera scanning needs a real device.

**2. Android emulator:** needs Android Studio (with an emulator image) and JDK 21 (bundled with Android Studio).
```powershell
npm run android:sync    # builds the "device" config (API at http://10.0.2.2:5080) and copies it into the Android project
npm run android:open    # opens projects/mobile/android in Android Studio, then press Run
```
Start the API normally (http profile) first.

**3. Android phone (USB or Wi-Fi):**
1. Set your PC's IP in `projects/mobile/src/environments/environment.device.ts` (`ipconfig` → IPv4), e.g. `http://192.168.1.20:5080/api/v1`.
2. Start the API with the **lan** profile (listens on all interfaces).
3. Allow port 5080 through Windows Firewall once (admin PowerShell):
   `New-NetFirewallRule -DisplayName "HelloCherie API dev" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private`
4. Enable USB debugging on the phone, plug it in, then `npm run android:sync` and Run from Android Studio (or `npm run android:run`).

The phone must be on the same network as the PC, and that network must be set to *Private* in Windows.

**4. iPhone / iPad:** needs a Mac with Xcode 16+ and CocoaPods (`sudo gem install cocoapods` or `brew install cocoapods`). The Xcode project is in `projects/mobile/ios` (CocoaPods, because ML Kit does not support Swift Package Manager; minimum iOS 15.5).
```bash
npm ci
npm run ios:sync        # builds the "device" config and runs pod install
npm run ios:open        # opens the workspace in Xcode: pick your team under Signing, choose the phone, Run
```
For a phone on Wi-Fi, set the PC's IP in `environment.device.ts` as for Android. `Info.plist` allows plain HTTP on the local network (`NSAllowsLocalNetworking`) for development; production uses HTTPS.

See [docs/10-mobile-app.md](../docs/10-mobile-app.md) for the screens and the device test checklist.

Debug builds allow plain HTTP to the local API (`CAP_DEV=1` in `android:sync`). Release builds must be synced with `npm run build:mobile`, which allows HTTPS only.

## Key decisions

* **Angular 21 + PrimeNG 21.1.10 (MIT).** PrimeNG 22 moved to a commercial "PrimeUI" licence that needs a licence key, so we stay on the last MIT release. Do not upgrade PrimeNG past 21.x without a licence decision.
* **Auth:** access token in memory only. Web uses an HttpOnly refresh cookie; mobile keeps the refresh token in the iOS Keychain / Android Keystore (`@aparajita/capacitor-secure-storage`).
* **Errors:** every HTTP failure becomes an `ApiError` (ProblemDetails). A network failure says that nothing was saved.
* **Idempotency:** stock-changing calls pass `context: idempotent(key)` so retries are recognised by the API.
* **Permissions in the UI are for navigation only.** The API enforces every rule.
* The web app is zoneless. The mobile app keeps zone.js because Ionic relies on NgZone.
