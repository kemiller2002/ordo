import { defineConfig } from "@playwright/test";

// `npm run ui:conformance` (from tests/ui-conformance/):
// visual-engineering/UI-LAYOUT-INTERACTION@1.0.0 evidence for the Ordo site.
// Chromium only: text scaling uses the Chromium DevTools protocol. The
// generated site (dist/) is served the way GitHub Pages resolves it. The
// evidence reporter writes test-results/evidence.json.
const port = Number(process.env.UI_CONFORMANCE_PORT ?? 4327);
const executablePath = process.env.UI_CONFORMANCE_CHROMIUM || undefined;

export default defineConfig({
  testDir: ".",
  testMatch: "*.spec.mjs",
  outputDir: "test-results/artifacts",
  fullyParallel: true,
  // Machines and CI runners are shared: keep concurrency low.
  workers: Number(process.env.UI_CONFORMANCE_WORKERS ?? 2),
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [[process.env.CI ? "line" : "list"], ["./evidence-reporter.mjs"]],
  timeout: 60_000,
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    launchOptions: executablePath ? { executablePath } : {}
  },
  projects: [{ name: "chromium", use: { browserName: "chromium" } }],
  webServer: {
    command: "node serve.mjs",
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: false,
    env: { UI_CONFORMANCE_PORT: String(port), ...(process.env.UI_CONFORMANCE_SITE_DIR ? { UI_CONFORMANCE_SITE_DIR: process.env.UI_CONFORMANCE_SITE_DIR } : {}) }
  }
});
