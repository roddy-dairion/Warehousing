import type { CapacitorConfig } from '@capacitor/cli';

/**
 * Capacitor config for the warehouse mobile app (projects/mobile).
 * appId is permanent once published to the stores — confirm it before the first release (open question Q22).
 */
const config: CapacitorConfig = {
  appId: 'com.warehousing.mobile',
  appName: 'HelloCherie Warehousing',
  webDir: 'dist/mobile/browser',
  android: { path: 'projects/mobile/android' },
  ios: { path: 'projects/mobile/ios' },
  server: { androidScheme: 'https' },
};

export default config;
