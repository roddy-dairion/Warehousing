# 11 — Building the mobile app with Codemagic

Codemagic builds the Android and iOS apps in the cloud, on Macs, so no Mac or Android Studio is needed for builds. The configuration is `codemagic.yaml` at the repository root. All four workflows start **manually**: in Codemagic choose *Start new build*, then the branch and the workflow.

| Workflow | Produces | Needs | Cost |
|---|---|---|---|
| `android-test` | Debug **.apk**, installed directly on Android phones or rugged scanners | Nothing | Free (Codemagic build minutes) |
| `ios-test-unsigned` | Unsigned **.ipa** for *your own* iPhone, signed on your PC with a free Apple ID (Sideloadly) | A free Apple ID | Free; the app must be reinstalled every 7 days |
| `android-release` | Signed **.aab** for Google Play | Upload keystore; Google Play account to publish | US$25 once (Google Play) |
| `ios-release` | Signed **.ipa** sent to **TestFlight** | Apple Developer Program | US$99 per year |

Every workflow runs the unit tests first, so a broken commit never becomes an app.

## Settings already in the project

* **App ID** `ch.hellocherie.warehouse` (Q22, decided 2026-10-12). It is set in Android, iOS and `capacitor.config.ts`. It can still change until the first upload to Google Play or App Store Connect.
* **App name** "HelloCherie Warehousing" (it can be changed at any time).
* **Version:** `APP_VERSION` in `codemagic.yaml` (default `1.0.0`) is the version users see. The build number is Codemagic's automatic `BUILD_NUMBER`, so every build is higher than the last, as the stores require.
* **API address:** never stored in the app's source. The build writes it from the `API_BASE_URL` variable (`frontend/scripts/mobile-ci.mjs env`). Release builds refuse anything but `https://`. Test builds (`CAP_DEV=1`) may use `http://` to a PC on the local network.
* **Signing keys** stay in Codemagic, never in the repository. `android/app/build.gradle` reads Codemagic's keystore variables (`CM_KEYSTORE_*`).

## One-time setup

1. **Put the repository on GitHub** (or GitLab / Bitbucket). Codemagic builds from a Git host, not from your PC. A private repository is fine.
2. Sign up at codemagic.io with that Git account and **add the application** (choose the repository). Codemagic finds `codemagic.yaml` by itself.
3. In the app's **Environment variables**, create the group **`mobile_test`** with `API_BASE_URL`, e.g. `http://192.168.1.20:5080/api/v1`. Use your PC's IPv4 address from `ipconfig`.

## Test on an Android phone (free)

1. On the PC, start the API with the **lan** profile and allow port 5080 through the Windows firewall (see [frontend/README.md](../frontend/README.md#testing-the-mobile-app)). Keep the phone on the same Wi-Fi.
2. In Codemagic, run **`android-test`**. When it finishes, download the `.apk` from the build page (or open the build page on the phone).
3. On the phone, open the `.apk`. Android asks you to allow installing from this source (the browser or Files app); allow it once.
4. Sign in and work through the device checklist in [doc 10](10-mobile-app.md#still-to-do-on-real-devices-the-phase-1-verification-gate).

Rugged Zebra/Honeywell devices install the same `.apk`. For their built-in scanner, set the wedge profile to "keystroke output" with **Enter** after each scan (Zebra: DataWedge; Honeywell: Scan Wedge).

## Test on your iPhone (free, 7 days per install)

1. Same API setup as for Android. Run **`ios-test-unsigned`** and download the `.ipa`.
2. On the Windows PC, install **Sideloadly** (third-party, sideloadly.io) and the iTunes / Apple Devices support it asks for.
3. Create a **separate Apple ID just for testing**. You type its password into Sideloadly, so do not use your personal Apple ID.
4. Connect the iPhone by USB, choose the `.ipa` in Sideloadly, enter the test Apple ID and start.
5. On the iPhone:
   * trust the developer: *Settings → General → VPN & Device Management*;
   * on iOS 16 or later, switch on *Settings → Privacy & Security → Developer Mode* (the phone restarts).
6. The app stops opening after 7 days. Reinstall it the same way. At most 3 such apps per Apple ID.

This route is for testing on your own phone only, not for staff.

## Releases (later, Phase 9)

**Android / Google Play**

1. Create the upload key once, and **back it up safely**: losing it blocks updates until Google resets it.
   `keytool -genkey -v -keystore hellocherie-upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000`
2. Codemagic → *Team settings → Code signing identities → Android keystores*: upload it with the reference name **`hellocherie_upload`**.
3. Create the group **`mobile_release`** with the HTTPS `API_BASE_URL` of the production API.
4. Run **`android-release`** to get the `.aab`. Upload the **first** one by hand in the Google Play Console (internal testing, or a private app via Managed Google Play). After that, Codemagic can publish automatically: add a Google Play service account as `google_play` / `GOOGLE_PLAY_SERVICE_ACCOUNT_CREDENTIALS` and uncomment `publishing:` in the workflow.

**iOS / TestFlight**

1. Join the Apple Developer Program (US$99 per year; a company needs a D-U-N-S number).
2. In App Store Connect, create the app with bundle ID `ch.hellocherie.warehouse`, and an **App Store Connect API key** (Users and Access → Integrations).
3. Codemagic → *Team settings → Integrations → Developer Portal*: add the key with the name **`HelloCherie App Store Connect`** (the name used in `codemagic.yaml`). Codemagic then creates the certificate and provisioning profile itself.
4. Run **`ios-release`**: the build goes to TestFlight. Testers install it with Apple's TestFlight app.

## What was checked here

* `codemagic.yaml` parses as valid YAML.
* `scripts/mobile-ci.mjs`:
  * `env` writes the API address, and refuses `http://` unless `CAP_DEV=1`;
  * `ios-version` sets the version and build number in the Xcode project.
* The steps up to `cap sync` were run locally with a test API address: the production web build contains the address, and the Android and iOS projects are updated.
* **Not run here:** the Gradle and Xcode steps (no Android SDK or Mac in this environment) and a real Codemagic build. Run `android-test` first; if a step fails, the build log in Codemagic shows which one.
