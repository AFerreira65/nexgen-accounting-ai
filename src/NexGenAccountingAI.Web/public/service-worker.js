// Cache da PWA publicada. Mudar o nome deita fora a cache das versões antigas.
const cacheName = 'accounting-ai-2026-10-05'

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(cacheName).then((cache) => cache.addAll(['/'])).then(() => self.skipWaiting()))
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== cacheName).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  )
})

const remember = (request, response) => {
  if (response.ok) {
    const copy = response.clone()
    void caches.open(cacheName).then((cache) => cache.put(request, copy))
  }
  return response
}

self.addEventListener('fetch', (event) => {
  const request = event.request
  const url = new URL(request.url)

  if (request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api')) {
    return
  }

  // A página (index.html) vem primeiro da rede, para uma versão nova aparecer logo;
  // a cache só serve sem ligação. Os ficheiros de /assets têm o hash no nome e
  // nunca mudam, por isso servem-se da cache.
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).then((response) => remember(request, response)).catch(() => caches.match(request).then((cached) => cached ?? caches.match('/'))))
    return
  }

  event.respondWith(caches.match(request).then((cached) => cached ?? fetch(request).then((response) => remember(request, response))))
})
