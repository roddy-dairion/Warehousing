import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/** "Harbour" preset: Aura with a deep blue-teal primary used for actions. */
export const HarbourPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#eef7fa',
      100: '#d5ebf2',
      200: '#aed7e5',
      300: '#7cbcd3',
      400: '#4a9bb9',
      500: '#2a7f9e',
      600: '#1a6a88',
      700: '#0f5c7a',
      800: '#114c64',
      900: '#133f53',
      950: '#0a2836',
    },
    colorScheme: {
      light: {
        primary: {
          color: '{primary.700}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.800}',
          activeColor: '{primary.900}',
        },
      },
    },
  },
});
