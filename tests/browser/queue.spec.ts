import { test, expect, Page } from '@playwright/test';

async function register(page: Page) {
  await page.goto('/register');
  await page.getByLabel('Full name').fill('Browser Test Student');
  await page.getByLabel('Email address').fill(`browser-${Date.now()}-${Math.random().toString(16).slice(2)}@example.test`);
  await page.getByLabel('Password', { exact: true }).fill('BrowserTest2026!');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('heading', { name: 'Your day, without the delay.' })).toBeVisible();
}

test('visitor can join, inspect and cancel a live ticket', async ({ page }) => {
  await register(page);
  const service = page.locator('.service-card').filter({ has: page.getByRole('heading', { name: 'Student services', exact: true }) });
  await service.getByRole('button', { name: 'Join queue' }).click();
  await expect(page.getByRole('status')).toContainText("You're in!");
  await page.getByRole('link', { name: 'My tickets', exact: true }).click();
  await expect(page.locator('.big-ticket-code')).toHaveText(/S-\d+/);
  await page.getByRole('button', { name: 'Leave queue', exact: true }).click();
  await page.getByRole('button', { name: 'Yes, leave queue' }).click();
  await expect(page.getByRole('heading', { name: 'A fresh start. No active tickets.' })).toBeVisible();
});

test('visitor can reserve and release a slot', async ({ page }) => {
  await register(page);
  await page.getByRole('link', { name: 'Appointments', exact: true }).click();
  const tomorrow = new Date(Date.now() + 86400000 + 5 * 3600000).toISOString().slice(0, 10);
  await page.getByLabel('Choose a date').fill(tomorrow);
  await page.locator('.slot').first().click();
  await page.getByRole('button', { name: 'Confirm appointment' }).click();
  await expect(page.getByRole('status')).toContainText('Your visit is booked');
  await page.getByRole('button', { name: 'Cancel visit', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm cancellation' }).click();
  await expect(page.locator('.appointment-card .status-badge').first()).toHaveText('Cancelled');
});

test('queue update reaches a separate public display without reloading', async ({ page, browser }) => {
  const displayContext = await browser.newContext();
  const display = await displayContext.newPage();
  await display.goto((process.env.QUEUELESS_URL || 'http://localhost:5180') + '/display');
  await register(page);
  await page.locator('.service-card').filter({ hasText: 'Library support' }).getByRole('button', { name: 'Join queue' }).click();
  await expect(page.getByRole('status')).toContainText("You're in!");
  const count = display.locator('.display-card').filter({ hasText: 'Library support' }).locator('.display-footer').first();
  await expect(count).toContainText(/[1-9]\d* waiting/);
  await page.getByRole('link', { name: 'My tickets', exact: true }).click();
  await page.getByRole('button', { name: 'Leave queue', exact: true }).click();
  await page.getByRole('button', { name: 'Yes, leave queue' }).click();
  await displayContext.close();
});

test('service search, empty state and responsive layout', async ({ page }, testInfo) => {
  await page.goto('/');
  await page.getByLabel('Search services').fill('not-a-service');
  await expect(page.getByRole('heading', { name: 'No services found' })).toBeVisible();
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page.locator('.service-card')).toHaveCount(4);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
  await page.screenshot({ path: testInfo.outputPath('overview.png'), fullPage: true });
});
