import react from '@vitejs/plugin-react'
import { env } from 'node:process'
import { defineConfig } from 'vite'

// When the backend's SpaProxy starts Vite it passes on the launch profile's ASPNETCORE_URLS.
// Otherwise (npm run dev on its own) assume the backend's "http" or "api" launch profile.
const apiTarget =
  env.ASPNETCORE_URLS?.split(';')
    .find((url) => url.startsWith('http://'))
    ?.replace(/\/\/(\+|\*|0\.0\.0\.0|\[::\])(?=:)/, '//localhost') ?? 'http://localhost:5015'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Must match SpaProxyServerUrl in src/ReceiptSplit/ReceiptSplit.csproj.
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
    },
  },
})
