import { Component, type ErrorInfo, type ReactNode } from 'react'
import { Button, Result } from 'antd'

type ErrorBoundaryProps = {
  children: ReactNode
}

type ErrorBoundaryState = {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // 保留控制台记录以便定位；生产环境可在此接入监控上报。
    console.error('页面渲染出错：', error, info.componentStack)
  }

  private handleReset = () => {
    this.setState({ hasError: false, error: null })
  }

  private handleReload = () => {
    window.location.assign('/')
  }

  render() {
    if (!this.state.hasError) {
      return this.props.children
    }

    return (
      <Result
        status="error"
        title="页面出现异常"
        subTitle={this.state.error?.message || '发生了未预期的错误，请重试或返回首页。'}
        extra={[
          <Button type="primary" key="retry" onClick={this.handleReset}>
            重试
          </Button>,
          <Button key="home" onClick={this.handleReload}>
            返回首页
          </Button>,
        ]}
      />
    )
  }
}
