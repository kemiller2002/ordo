// Declared conformance inventory for the Ordo website against
// visual-engineering/UI-LAYOUT-INTERACTION@1.0.0 (pinned at visual-engineering
// 8da8fbc). This file is data. Routes and headings are read from the same
// sources the generator renders (site/content front matter and
// site/data/*.json), so a page that the generator stops producing, or a
// heading that drifts from its source, is observable as a failure.
//
// Action classes:
// - chrome: the skip link, brand, primary navigation and footer actions that
//   src/Ordo.Site/Render.fs renders on every route. Declared individually.
// - calls to action: every `.button` link authored in site/content. Declared
//   individually with the destination heading they must reach.
// - content links: prose, citation and card links in <main>. Each is
//   enumerated at runtime and its destination verified (internal page or
//   fragment resolves; external link is an https URL). Their availability on
//   third-party hosts is unknown scope, not a pass.

import { readFileSync, readdirSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

export const STANDARD = Object.freeze({ id: "visual-engineering/UI-LAYOUT-INTERACTION", version: "1.0.0", source: "kemiller2002/visual-engineering@8da8fbc" });

const SITE = fileURLToPath(new URL("../../site/", import.meta.url));
const read = path => readFileSync(join(SITE, path), "utf8");
const readJson = path => JSON.parse(read(path));

const ENTITIES = Object.freeze({ "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": "\"", "&#39;": "'", "&rsquo;": "’", "&mdash;": "—", "&nbsp;": " " });
const plain = html => html.replace(/<[^>]+>/g, "").replace(/&[a-z#0-9]+;/g, entity => ENTITIES[entity] ?? entity).replace(/\s+/g, " ").trim();

const walk = dir => readdirSync(dir, { withFileTypes: true }).flatMap(entry =>
  entry.isDirectory() ? walk(join(dir, entry.name)) : entry.name.endsWith(".html") ? [join(dir, entry.name)] : []);

const frontMatter = source => Object.fromEntries((/^---\n([\s\S]*?)\n---/.exec(source)?.[1] ?? "")
  .split("\n").map(line => /^([a-zA-Z]+):\s*(.*)$/.exec(line)).filter(Boolean).map(([, key, value]) => [key, value.trim()]));

const contentPages = walk(join(SITE, "content")).map(file => {
  const source = readFileSync(file, "utf8");
  const meta = frontMatter(source);
  return {
    id: relative(join(SITE, "content"), file).replace(/\.html$/, "").replace(/\//g, "-").replace(/-index$/, "") || "home",
    path: meta.path,
    heading: plain(/<h1[^>]*>([\s\S]*?)<\/h1>/.exec(source)?.[1] ?? ""),
    source
  };
});

const SITE_DATA = readJson("data/site.json");
const EVIDENCE = readJson("data/evidence.json");

const experimentPages = EVIDENCE.experiments.map(experiment => ({
  id: `evidence-${experiment.slug}`, path: `/evidence/${experiment.slug}/`, heading: experiment.title, source: ""
}));

export const ROUTES = Object.freeze([...contentPages, ...experimentPages]
  .map(({ id, path, heading }) => ({ id: id === "index" ? "home" : id, path, heading, current: SITE_DATA.nav.find(item => item.path === path)?.label ?? null }))
  .sort((a, b) => a.path.localeCompare(b.path)));

export const routeFor = path => ROUTES.find(route => route.path === path);
const headingOf = path => routeFor(path)?.heading ?? null;

const link = name => ({ role: "link", name });

const navigate = (id, name, destination, from) =>
  ({ id, control: link(name), kind: "navigate", destination, heading: headingOf(destination), from, intent: `Navigate to ${destination}`, expectedOutcome: `URL becomes ${destination} and the destination's heading "${headingOf(destination)}" is shown` });

const NEUTRAL = "/glossary/";

export const CHROME = Object.freeze([
  { id: "skip-link", control: link("Skip to main content"), kind: "skip", destination: "#main-content", from: NEUTRAL, intent: "Move keyboard focus past the header to the page content", expectedOutcome: "The link appears on keyboard focus; activating it changes the hash to #main-content and the main region is in view below the sticky header" },
  navigate("brand-home", SITE_DATA.name, "/", NEUTRAL),
  ...SITE_DATA.nav.map(item => navigate(`nav-${item.path.replace(/\//g, "") || "home"}`, item.label, item.path, NEUTRAL)),
  { id: "footer-source", control: link("Read the source repository"), kind: "external", destination: SITE_DATA.sourceUrl, intent: `Open the source repository ${SITE_DATA.sourceUrl}`, expectedOutcome: `href is ${SITE_DATA.sourceUrl} (an https destination; the external host's availability is not claimed)` },
  { id: "footer-llms", control: link("llms.txt"), kind: "resource", destination: "/llms.txt", intent: "Open the machine-readable site summary", expectedOutcome: "/llms.txt is served as text/plain and names the site" }
]);

// Every `.button` link authored in a content page is a call to action with a
// declared destination.
const BUTTON = /<a\s+class="button[^"]*"\s+href="([^"]+)"[^>]*>([\s\S]*?)<\/a>/g;
const slug = text => text.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
// Content pages write internal links as {{link:/path/}} tokens (site/README.md).
const destinationOf = href => /^\{\{link:(.+)\}\}$/.exec(href)?.[1] ?? href;
const callsToAction = page => [...page.source.matchAll(BUTTON)].map(([, href, label]) =>
  navigate(`cta-${slug(plain(label))}`, plain(label), destinationOf(href), page.path));

// Alignment groups. `widths` names where the composition declares the
// relation (src/Ordo.Site/Render.fs markup, site/assets/css/ordo.css).
const actionsRow = (id, names, widths, rationale) =>
  ({ id, ruleId: "ULI-L10", relation: "centerline", edge: "center", widths, peers: names.map(link), rationale });

const ctaGroups = page => {
  const groups = [...page.source.matchAll(/<div class="actions">([\s\S]*?)<\/div>/g)].map(([, inner]) => [...inner.matchAll(BUTTON)].map(([, , label]) => plain(label)));
  return groups.filter(names => names.length > 1).flatMap((names, index) => [
    actionsRow(`cta-row-${index + 1}`, names, [1280], "Calls to action in one .actions row share a centerline where they fit on one line"),
    { id: `cta-row-${index + 1}-height`, ruleId: "ULI-L02", relation: "bottom-edge", edge: "bottom", widths: [1280], peers: names.map(link), rationale: "Peer calls to action in one row have one intrinsic height (min-height 3rem token)" }
  ]);
};

export const SURFACES = Object.freeze([
  {
    id: "chrome", path: "/* (shared header and footer on every route)", kind: "marketing",
    actions: CHROME,
    groups: [
      { ...actionsRow("primary-nav", SITE_DATA.nav.map(item => item.label), [768, 1280], "Primary navigation destinations share one row and centerline above the 680px recomposition"), scope: "navigation" },
      { ...actionsRow("footer-actions", ["Read the source repository", "llms.txt"], [390, 768, 1280], "Footer actions share one centerline in the footer action row"), scope: "contentinfo" }
    ]
  },
  ...ROUTES.map(route => {
    const page = contentPages.find(item => item.path === route.path);
    return {
      id: route.id, path: route.path, kind: "marketing",
      actions: page ? callsToAction(page) : [],
      groups: page ? ctaGroups(page) : []
    };
  })
]);

export const surfaceForRoute = route => SURFACES.find(surface => surface.path === route.path);

export const declaredActionsFor = route => [
  ...CHROME.map(action => ({ ...action, surfaceId: "chrome" })),
  ...(surfaceForRoute(route)?.actions ?? []).map(action => ({ ...action, surfaceId: surfaceForRoute(route).id }))
];
