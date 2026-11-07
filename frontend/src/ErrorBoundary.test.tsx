import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { expect, it, vi } from 'vitest'
import { ErrorBoundary } from './ErrorBoundary'

it('shows the render error and retries the child without losing recovery controls', async () => {
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  const errorLog = vi.spyOn(console, 'error').mockImplementation(() => {})
  let shouldFail = true
  const Child = () => {
    if (shouldFail) throw new Error('测试渲染失败')
    return <p>已恢复</p>
  }

  try {
    await act(async () => root.render(<ErrorBoundary><Child /></ErrorBoundary>))
    expect(container.textContent).toContain('页面出现异常')
    expect(container.textContent).toContain('测试渲染失败')
    const buttons = [...container.querySelectorAll('button')]
    expect(buttons.map(button => button.textContent?.replace(/\s/g, ''))).toEqual(['重试', '返回首页'])
    shouldFail = false
    await act(async () => buttons[0].click())
    expect(container.textContent).toBe('已恢复')
  } finally {
    await act(async () => root.unmount())
    container.remove()
    errorLog.mockRestore()
  }
})
