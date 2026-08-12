import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import type { UserConfig } from 'vite'
import type { InlineConfig } from 'vitest/node'

const config = {
  plugins: [react()],
  build: {
    // Vite 只检查原始 chunk 大小；实际 gzip 预算由 npm run bundle:check 强制执行。
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        // 把体积最大的第三方库拆成独立 vendor chunk：antd 被三个页面共享，
        // 单独分包后浏览器可长期缓存，也消除了此前 >500kB 的单包构建告警。
        // Vite 8（rolldown）要求 manualChunks 为函数形式。
        manualChunks: (id: string) => {
          if (id.includes('node_modules/antd/') || id.includes('node_modules/rc-')) {
            return 'antd'
          }
          if (
            id.includes('node_modules/react/')
            || id.includes('node_modules/react-dom/')
            || id.includes('node_modules/react-router')
          ) {
            return 'react'
          }
          if (id.includes('node_modules/lucide-react/')) {
            return 'icons'
          }
          return undefined
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/testSetup.ts'
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
      },
      '/runtime-config': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  }
} satisfies UserConfig & { test: InlineConfig }

export default defineConfig(config)
