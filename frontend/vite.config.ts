import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import type { UserConfig } from 'vite'
import type { InlineConfig } from 'vitest/node'

const config = {
  plugins: [react()],
  test: {
    environment: 'jsdom'
  },
  server: {
    port: 3000, // 我们把前端端口固定在 3000
    proxy: {
      // 当我们在前端请求 /api/xxx 时，Vite 会自动帮我们转发给本地 5000 端口的后端
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      },
      // 健康检查接口也一并代理过去
      '/health': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  }
} satisfies UserConfig & { test: InlineConfig }

export default defineConfig(config)
