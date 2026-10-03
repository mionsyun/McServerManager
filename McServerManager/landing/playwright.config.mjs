import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/browser',
  workers: 1,
  reporter: [['list'], ['json', { outputFile: 'test-results/browser-results.json' }]],
  use: {
    browserName: 'chromium',
    headless: true,
    launchOptions: {
      ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
        ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {}),
      args: ['--no-sandbox'],
    },
    reducedMotion: 'reduce',
  },
});
