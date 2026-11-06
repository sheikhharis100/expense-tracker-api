import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the API runs on :5216 (dotnet run); Vite proxies to it so the app and API share an origin.
const api = process.env.API_URL ?? 'http://localhost:5216';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/hubs': { target: api, changeOrigin: true, ws: true },
    },
  },
  // One bundle on purpose: the service worker caches it whole, so every screen opens offline (~150 kB gzipped).
  build: { outDir: 'dist', sourcemap: false, chunkSizeWarningLimit: 600 },
});
