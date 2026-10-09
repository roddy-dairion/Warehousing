import type { CapacitorConfig } from '@capacitor/cli';

/**
 * Capacitor config for the warehouse mobile app (projects/mobile).
 * appId is permanent once published to the stores — confirm it before the first release (open question Q22).
 *
 * CAP_DEV=1 (set by `npm run android:sync`) allows plain-HTTP calls to the local API.
 * Release builds must be synced without it, so only HTTPS is allowed.
 */
const dev = process.env['CAP_DEV'] === '1';

const config: CapacitorConfig = {
  appId: 'com.warehousing.mobile',
  appName: 'HelloCherie Warehousing',
  webDir: 'dist/mobile/browser',
  android: {
    path: 'projects/mobile/android',
    allowMixedContent: dev,
  },
  ios: { path: 'projects/mobile/ios' },
  server: {
    androidScheme: 'https',
    cleartext: dev,
  },
};

export default config;
