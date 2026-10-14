// Static server for the generated Ordo site (dist/) used by the UI conformance
// suite. It mirrors GitHub Pages resolution: "/x/" serves "/x/index.html", a
// directory path without its trailing slash serves the same page, and an
// unknown path is a 404 so a dead internal link is observable as a failure.
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(process.env.UI_CONFORMANCE_SITE_DIR ?? fileURLToPath(new URL("../../dist/", import.meta.url)));
const port = Number(process.env.UI_CONFORMANCE_PORT ?? 4327);

const TYPES = Object.freeze({
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".txt": "text/plain; charset=utf-8",
  ".xml": "application/xml; charset=utf-8",
  ".svg": "image/svg+xml"
});

const inside = path => (path === root || path.startsWith(`${root}/`) ? path : null);

const candidatePath = pathname => {
  const decoded = decodeURIComponent(pathname);
  return inside(normalize(join(root, decoded.endsWith("/") ? `${decoded}index.html` : decoded)));
};

const resolveFile = async path => {
  const info = await stat(path);
  return info.isDirectory() ? join(path, "index.html") : path;
};

const respond = (response, status, type, body) => {
  response.writeHead(status, { "content-type": type });
  response.end(body);
};

createServer(async (request, response) => {
  const path = candidatePath(new URL(request.url, "http://localhost").pathname);
  if (!path) return respond(response, 403, "text/plain", "Forbidden");
  try {
    const file = await resolveFile(path);
    return respond(response, 200, TYPES[extname(file)] ?? "application/octet-stream", await readFile(file));
  } catch {
    return respond(response, 404, "text/plain", "Not found");
  }
}).listen(port, "127.0.0.1", () => process.stdout.write(`serving ${root} on http://127.0.0.1:${port}\n`));
