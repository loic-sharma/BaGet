import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Built for BaGet's Frontend:SpaBasePath=/app. Change `base` (and the router basename in
// src/main.tsx follows it) if you host under another path.
export default defineConfig({
  base: '/app/',
  plugins: [react()],
  server: {
    port: 5173,
    // Dev convenience for "same origin" mode (VITE_BAGET_URL empty): /api goes to a local BaGet.
    // The browser's Origin stays http://localhost:5173, so list it in Cors:AllowedOrigins.
    proxy: { '/api': { target: process.env.BAGET_DEV_PROXY ?? 'http://localhost:6060', changeOrigin: true } },
  },
});
