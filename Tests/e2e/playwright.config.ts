import { defineConfig, devices } from '@playwright/test';

const browserName = process.env.PW_BROWSER || 'chromium';
const channel = process.env.PW_CHANNEL || undefined;

export default defineConfig({
  testDir: './tests',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL || 'http://127.0.0.1:5072',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    locale: 'en-US',
    viewport: { width: 1280, height: 900 },
    launchOptions: {
      args: ['--autoplay-policy=no-user-gesture-required']
    },
    channel
  },
  projects: [
    {
      name: channel ?? browserName,
      use: { ...devices['Desktop Chrome'], browserName: browserName as 'chromium' | 'firefox' | 'webkit' }
    }
  ],
  outputDir: 'test-results'
});
