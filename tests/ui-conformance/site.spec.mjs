// UI conformance suite for the Ordo website against
// visual-engineering/UI-LAYOUT-INTERACTION@1.0.0. Every assertion resolves
// controls by role and accessible name, waits with web-first assertions and
// compares observed pre/post state. No coordinates, no fixed sleeps.
//
// The site is static generated HTML with no script and no form, so there is
// no application state to fail and recover: the failure path exercised here
// is navigation to a destination that does not exist (404), which a dead
// internal link would hit.
import { expect, test } from "@playwright/test";
import {
  CONTRACT_WIDTHS,
  expectActionOutcome,
  expectDestinationClearOfHeader,
  expectLabelIntact,
  expectNoOverflow,
  expectPeerAlignment,
  inventoryActions,
  recordTabSequence,
  scaleDefaultText,
  settleFontsAndFrames,
  wasFocusedDuringSequence
} from "./forma-conformance/index.mjs";
import { CHROME, ROUTES, SURFACES, declaredActionsFor } from "./surfaces.mjs";

const escape = value => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const NAV_NAMES = CHROME.filter(action => action.id.startsWith("nav-")).map(action => action.control.name);
const CHROME_PROBE = "/glossary/";

const annotate = (type, description) => test.info().annotations.push({ type, description });
const banner = page => page.getByRole("banner");
const primaryNav = page => page.getByRole("navigation", { name: "Primary" });
const pathPattern = destination => new RegExp(`^https?://[^/]+${escape(destination)}$`);
const heading = page => page.getByRole("heading", { level: 1 });

// --- ULI-L05/L06/L09: every route at every contract width -------------------

for (const width of CONTRACT_WIDTHS) {
  test.describe(`at ${width}px`, () => {
    test.use({ viewport: { width, height: 900 } });

    for (const route of ROUTES) {
      test(`${route.path} renders without overflow, with intact navigation labels`, async ({ page }) => {
        annotate("uli-surface", route.id);
        annotate("uli-surface", "chrome");
        await page.goto(route.path);
        await expect(heading(page)).toHaveText(route.heading);
        await expectNoOverflow(page);
        await expect(banner(page).getByRole("link", { name: "Ordo", exact: true })).toBeVisible();
        for (const name of NAV_NAMES) await expectLabelIntact(primaryNav(page).getByRole("link", { name, exact: true }));
        // Wayfinding: the current destination is announced in the primary navigation.
        await expect(primaryNav(page).locator("[aria-current=page]")).toHaveCount(route.current ? 1 : 0);
        if (route.current) await expect(primaryNav(page).getByRole("link", { name: route.current, exact: true })).toHaveAttribute("aria-current", "page");
      });
    }
  });
}

// --- ULI-L05: 200% default text size (browser text-scaling setting) ---------

for (const width of CONTRACT_WIDTHS) {
  test.describe(`at ${width}px with 200% text`, () => {
    test.use({ viewport: { width, height: 900 } });
    for (const route of ROUTES) {
      test(`${route.path} reflows without overflow`, async ({ page }) => {
        annotate("uli-surface", route.id);
        await page.goto(route.path);
        await scaleDefaultText(page, 2);
        await expectNoOverflow(page);
        for (const name of NAV_NAMES) await expectLabelIntact(primaryNav(page).getByRole("link", { name, exact: true }));
      });
    }
  });
}

// --- ULI-A01/A06/A10: only declared action classes render --------------------

for (const route of ROUTES) {
  test(`${route.path} renders only declared actions`, async ({ page }) => {
    annotate("uli-surface", route.id);
    await page.goto(route.path);
    await settleFontsAndFrames(page);
    const declared = declaredActionsFor(route);
    const outside = [
      ...(await inventoryActions(banner(page))),
      ...(await inventoryActions(page.getByRole("contentinfo")))
    ];
    const mainItems = await inventoryActions(page.locator("main"));
    const isChrome = item => declared.some(action => action.control.role === item.role && action.control.name === item.name);
    // Every action in the header and footer is a declared chrome action, and
    // nothing renders outside the skip link, header, main and footer.
    expect(outside.filter(item => !isChrome(item)).map(item => `${item.role} "${item.name}" -> ${item.href}`), "ULI-A10: chrome actions without a declared contract").toEqual([]);
    expect(await page.locator("body > :not(.skip-link, header, main, footer)").count(), "unexpected top-level regions").toBe(0);
    // Main content holds only links (calls to action and content links); a
    // button, disclosure or other control would need its own contract.
    expect(mainItems.filter(item => item.role !== "link").map(item => `${item.role} "${item.name}"`), "ULI-A10: non-link controls without a declared contract").toEqual([]);
    expect([...outside, ...mainItems].filter(item => !item.name).map(item => item.href), "ULI-A06: actions without an accessible name").toEqual([]);
  });
}

// --- ULI-A01: every content link in <main> has a meaningful destination ------

const pageHasFragment = async (request, path, fragment) => {
  const response = await request.get(path);
  const body = await response.text();
  return { status: response.status(), found: !fragment || new RegExp(`\\sid="${escape(fragment)}"`).test(body) };
};

for (const route of ROUTES) {
  test(`${route.path} content links resolve`, async ({ page, request, baseURL }) => {
    annotate("uli-content-links", route.id);
    await page.goto(route.path);
    const links = await page.locator("main a[href]").evaluateAll(anchors => anchors.map(anchor => ({
      text: anchor.textContent.replace(/\s+/g, " ").trim(), href: anchor.getAttribute("href"), resolved: anchor.href
    })));
    const problems = [];
    for (const link of links) {
      const url = new URL(link.resolved);
      if (link.href.startsWith("javascript:") || link.href === "#" || link.href === "") {
        problems.push(`"${link.text}" has no destination (${link.href})`);
      } else if (url.origin === new URL(baseURL).origin) {
        const { status, found } = await pageHasFragment(request, url.pathname, decodeURIComponent(url.hash.slice(1)));
        if (status !== 200) problems.push(`"${link.text}" -> ${link.href}: HTTP ${status}`);
        else if (!found) problems.push(`"${link.text}" -> ${link.href}: fragment target missing`);
      } else if (!["https:", "mailto:"].includes(url.protocol)) {
        problems.push(`"${link.text}" -> ${link.href}: not an https destination`);
      }
    }
    test.info().annotations.push({ type: "content-links", description: `${links.length} links checked` });
    expect(problems, `ULI-A01: ${links.length} content links on ${route.path}`).toEqual([]);
  });
}

// --- Declared actions: destination is meaningful (ULI-A01) -------------------

for (const surface of SURFACES) {
  for (const action of surface.actions) {
    test(`${surface.id}/${action.id} destination is valid`, async ({ page, request }) => {
      annotate("uli-destination", `${surface.id}/${action.id}`);
      await page.goto(surface.id === "chrome" ? CHROME_PROBE : surface.path);
      const control = page.getByRole("link", { name: action.control.name, exact: true });
      await expect(control).toHaveCount(1);
      if (action.kind === "navigate") {
        await expect(control).toHaveAttribute("href", action.destination);
        expect((await request.get(action.destination)).status(), action.destination).toBe(200);
      }
      if (action.kind === "skip") {
        await expect(control).toHaveAttribute("href", action.destination);
        await expect(page.locator(action.destination)).toHaveCount(1);
      }
      if (action.kind === "external") {
        await expect(control).toHaveAttribute("href", action.destination);
        expect(new URL(action.destination).protocol).toBe("https:");
      }
      if (action.kind === "resource") {
        await expect(control).toHaveAttribute("href", action.destination);
        const response = await request.get(action.destination);
        expect(response.status()).toBe(200);
        expect(await response.text()).toMatch(/Ordo/);
      }
    });
  }
}

// --- ULI-A02/A06: every navigation action changes the page ------------------

const navigationActions = SURFACES.flatMap(surface => surface.actions.filter(action => action.kind === "navigate").map(action => ({ surface, action })));

for (const width of [390, 1280]) {
  test.describe(`navigation outcomes at ${width}px`, () => {
    test.use({ viewport: { width, height: 900 } });
    for (const { surface, action } of navigationActions) {
      test(`${surface.id}: ${action.id} reaches ${action.destination}`, async ({ page }) => {
        annotate("uli-action", `${surface.id}/${action.id}`);
        await page.goto(action.from);
        await expectActionOutcome(page, {
          ruleId: "ULI-A02",
          action: action.control,
          outcome: { kind: "url", expected: pathPattern(action.destination) }
        });
        await expect(heading(page)).toHaveText(action.heading);
      });
    }

    test("skip link moves to the main content", async ({ page }) => {
      annotate("uli-action", "chrome/skip-link");
      await page.goto(CHROME_PROBE);
      const skip = page.getByRole("link", { name: "Skip to main content" });
      await expect(skip).not.toBeInViewport();
      await page.keyboard.press("Tab");
      await expect(skip).toBeFocused();
      await expect(skip).toBeInViewport();
      await expectActionOutcome(page, {
        ruleId: "ULI-A02",
        action: { role: "link", name: "Skip to main content" },
        outcome: { kind: "url", expected: pathPattern(`${CHROME_PROBE}#main-content`) }
      });
      await expect(page.getByRole("main")).toBeInViewport();
    });
  });
}

// External and resource actions: the destination is the observable outcome;
// the suite does not open third-party sites.
for (const action of CHROME.filter(item => item.kind === "external" || item.kind === "resource")) {
  test(`chrome: ${action.id} points at ${action.destination}`, async ({ page }) => {
    annotate("uli-action", `chrome/${action.id}`);
    await page.goto(CHROME_PROBE);
    const control = page.getByRole("contentinfo").getByRole("link", { name: action.control.name, exact: true });
    await expect(control).toBeVisible();
    await expect(control).toHaveAttribute("href", action.destination);
    if (action.kind === "resource") {
      await control.click();
      await expect(page).toHaveURL(pathPattern(action.destination));
      await expect(page.locator("body")).toContainText("Ordo");
    }
  });
}

// --- ULI-L05: in-page destinations land clear of the sticky header ----------

const fragmentRoutes = ROUTES.filter(route => route.path === "/research/");

for (const width of [320, 1280]) {
  for (const route of fragmentRoutes) {
    test(`${route.path} in-page links land below the sticky header at ${width}px`, async ({ page }) => {
      annotate("uli-surface", route.id);
      await page.setViewportSize({ width, height: 800 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.goto(route.path);
      const fragments = await page.locator('main a[href^="#"]').evaluateAll(anchors => anchors.map(anchor => ({ name: anchor.textContent.trim(), hash: anchor.getAttribute("href") })));
      expect(fragments.length).toBeGreaterThan(0);
      for (const fragment of fragments) {
        await page.goto(route.path);
        await expectActionOutcome(page, {
          ruleId: "ULI-A02",
          action: { role: "link", name: fragment.name, scope: page.getByRole("main") },
          outcome: { kind: "url", expected: pathPattern(`${route.path}${fragment.hash}`) }
        });
        await expectDestinationClearOfHeader(page, page.locator(fragment.hash), banner(page));
      }
    });
  }
}

// --- Failure path: an unknown destination is a 404, not a soft success ------

test("an unknown route is reported as not found", async ({ request }) => {
  annotate("uli-surface", "chrome");
  expect((await request.get("/no-such-page/")).status()).toBe(404);
});

// --- ULI-L01/L02/L10: declared alignment groups ----------------------------

for (const surface of SURFACES) {
  for (const group of surface.groups) {
    for (const width of group.widths) {
      test(`${surface.id}: ${group.id} aligns at ${width}px`, async ({ page }) => {
        annotate("uli-group", `${surface.id}/${group.id}@${width}`);
        await page.setViewportSize({ width, height: 900 });
        await page.goto(surface.id === "chrome" ? CHROME_PROBE : surface.path);
        await expectPeerAlignment(page, {
          ruleId: group.ruleId,
          id: group.id,
          edge: group.edge,
          peers: group.peers,
          scope: group.scope === "navigation" ? primaryNav(page) : group.scope ? page.getByRole(group.scope) : undefined
        });
      });
    }
  }
}

// --- ULI-A08: keyboard reachability; reduced motion; forced colors ----------

test("every chrome action is reachable by keyboard", async ({ page }) => {
  annotate("uli-surface", "chrome");
  await page.goto(CHROME_PROBE);
  await page.getByRole("main").evaluate(main => main.setAttribute("inert", ""));
  await recordTabSequence(page, CHROME.length + 2);
  for (const action of CHROME) {
    expect(await wasFocusedDuringSequence(page.getByRole("link", { name: action.control.name, exact: true })), action.id).toBe(true);
  }
});

test("reduced motion removes smooth scrolling and transitions", async ({ page }) => {
  annotate("uli-surface", "home");
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto("/");
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).scrollBehavior)).toBe("auto");
  await expect.poll(() => page.getByRole("link", { name: "See how Ordo works" }).evaluate(element => getComputedStyle(element).transitionDuration)).toBe("0s");
});

test("forced colors keep filled controls bounded and navigation visible", async ({ page }) => {
  annotate("uli-surface", "home");
  await page.emulateMedia({ forcedColors: "active" });
  await page.goto("/");
  await expect.poll(() => page.getByRole("link", { name: "See how Ordo works" }).evaluate(element => getComputedStyle(element).borderTopStyle)).toBe("solid");
  for (const name of NAV_NAMES) await expect(primaryNav(page).getByRole("link", { name, exact: true })).toBeVisible();
  await expectNoOverflow(page);
});
