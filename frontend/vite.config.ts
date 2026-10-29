import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// host: true 让同一局域网内的手机也能打开管理端页面（可选）
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
  },
});
