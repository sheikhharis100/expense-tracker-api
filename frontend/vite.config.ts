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
  build: { outDir: 'dist', sourcemap: false },
});
