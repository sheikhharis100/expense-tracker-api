// Service worker for the installed app: it opens from cache at once, even while the free server is waking up.
// Data never comes from here: the API, the live hub, Swagger and the health check always go to the network.

// Bump to drop every cached file (activate deletes caches with other names).
const VERSION = 'v1';
const CACHE = `expense-tracker-${VERSION}`;
const SHELL = '/index.html';
const NAV_TIMEOUT_MS = 4000;
// Hashed bundles pile up across deploys; keep the newest ones only.
const MAX_ASSETS = 60;

const PRECACHE = [SHELL, '/manifest.webmanifest', '/icon-180.png', '/icon-192.png', '/icon-512.png'];
const BYPASS = /^\/(?:api|hubs)\/|^\/swagger|^\/healthz/;
const STATIC = /^\/assets\/|\.(?:png|svg|ico|webmanifest|woff2?|ttf|otf)$/;

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE);
      const add = (urls) => Promise.all(urls.map((url) => cache.add(new Request(url, { cache: 'reload' })).catch(() => {})));
      await add(PRECACHE);
      // The page that registered us loaded its bundles before we existed; cache them (and the fonts their
      // stylesheets name) now so the next open works offline. Missing pieces are filled in at runtime.
      const bundles = await referenced(cache, SHELL, /(?:src|href)="(\/assets\/[^"]+)"/g);
      await add(bundles);
      const styles = bundles.filter((u) => u.endsWith('.css'));
      await add((await Promise.all(styles.map((css) => referenced(cache, css, /url\(["']?(\/assets\/[^"')]+)["']?\)/g)))).flat());
      await self.skipWaiting();
    })(),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const names = await caches.keys();
      await Promise.all(names.filter((n) => n.startsWith('expense-tracker-') && n !== CACHE).map((n) => caches.delete(n)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('fetch', (event) => {
  const { request } = event;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || BYPASS.test(url.pathname)) return;

  if (request.mode === 'navigate') {
    const network = fetch(request);
    // Keep the cached shell fresh even when the cached copy answered first.
    event.waitUntil(network.then((res) => saveShell(res)).catch(() => {}));
    event.respondWith(navigate(network));
    return;
  }

  if (STATIC.test(url.pathname)) event.respondWith(cacheFirst(event));
});

// Network first, but never wait more than a few seconds for a sleeping server when a cached shell exists.
async function navigate(network) {
  try {
    const res = await Promise.race([network, new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), NAV_TIMEOUT_MS))]);
    if (res.status < 500) return res;
    return (await cachedShell()) || res;
  } catch {
    return (await cachedShell()) || network;
  }
}

async function saveShell(res) {
  // Only a real HTML page replaces the cached shell. Clone before the first await, before the page reads the body.
  if (!res.ok || res.redirected || !(res.headers.get('content-type') || '').includes('text/html')) return;
  const copy = res.clone();
  const cache = await caches.open(CACHE);
  await cache.put(SHELL, copy);
}

async function cachedShell() {
  const cache = await caches.open(CACHE);
  return cache.match(SHELL);
}

async function cacheFirst(event) {
  const cache = await caches.open(CACHE);
  const hit = await cache.match(event.request);
  if (hit) return hit;
  const res = await fetch(event.request);
  if (res.ok && res.type === 'basic') {
    event.waitUntil(cache.put(event.request, res.clone()).then(() => trim(cache)));
  }
  return res;
}

async function trim(cache) {
  const assets = (await cache.keys()).filter((r) => new URL(r.url).pathname.startsWith('/assets/'));
  // Keys come back oldest first.
  await Promise.all(assets.slice(0, Math.max(0, assets.length - MAX_ASSETS)).map((r) => cache.delete(r)));
}

// Same-origin /assets/ paths named inside a file that is already cached.
async function referenced(cache, url, pattern) {
  const res = await cache.match(url);
  if (!res) return [];
  return [...new Set([...(await res.text()).matchAll(pattern)].map((m) => m[1]))];
}
