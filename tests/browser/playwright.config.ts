import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: 'queue.spec.ts',
  fullyParallel: false,
  workers: 1,
  timeout: 45000,
  use: { baseURL: process.env.QUEUELESS_URL || 'http://localhost:5180', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: process.env.QUEUELESS_URL ? undefined : {
    command: 'dotnet run --project ../../src/QueueLess.Web --launch-profile http',
    url: 'http://localhost:5180/health',
    reuseExistingServer: !process.env.CI,
    timeout: 120000
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'mobile', use: { ...devices['iPhone 13'], defaultBrowserType: 'webkit' } }
  ],
  reporter: [['list'], ['html', { open: 'never' }]]
});
