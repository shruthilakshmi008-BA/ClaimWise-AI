/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// In development the UI runs on :5173 and the API on :5080; proxying /api avoids CORS and keeps URLs relative,
// so the same build also works when the API serves the UI. Override the target with VITE_API_TARGET.
export default defineConfig(({ mode }) => {
  const target = loadEnv(mode, '.', '').VITE_API_TARGET || 'http://localhost:5080';
  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': { target, changeOrigin: true },
        '/health': { target, changeOrigin: true },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      css: false,
    },
  };
});
