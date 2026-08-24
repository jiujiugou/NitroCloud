import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    // ADR-009：沿用网关惯例，后端 NitroCloud.Api（组合根）绑定 IPv4；
    // 显式 127.0.0.1 强制走 IPv4（Node>=17 会把 localhost 优先解析为 ::1 → 502）。
    // 后端端口待 M1 骨架确认，先沿用网关 5100。
    proxy: {
      '/api': 'http://127.0.0.1:5100',
      '/hubs': { target: 'http://127.0.0.1:5100', ws: true }
    }
  },
  optimizeDeps: {
    include: ['element-plus', '@element-plus/icons-vue', 'axios', 'echarts', 'vue-router', '@microsoft/signalr']
  }
})
