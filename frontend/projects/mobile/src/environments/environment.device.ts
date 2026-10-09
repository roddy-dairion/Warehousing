/**
 * Debug build for the Android emulator or a phone (`npm run android:sync`).
 *
 * - Android emulator: 10.0.2.2 is your PC as seen from the emulator. Keep the default.
 * - Physical phone on the same Wi-Fi: replace with your PC's IP (run `ipconfig`, IPv4 address),
 *   e.g. 'http://192.168.1.20:5080/api/v1', and start the API with the "lan" profile.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://10.0.2.2:5080/api/v1',
};
