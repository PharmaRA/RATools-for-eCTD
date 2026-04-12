/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}", // 告诉 Tailwind 去哪些文件里寻找 CSS 类名
  ],
  theme: {
    extend: {},
  },
  plugins: [],
  corePlugins: {
    // ⚠️ 极其重要：关闭 Tailwind 的默认样式重置，防止它把 Ant Design 的按钮和组件样式搞乱
    preflight: false, 
  }
}