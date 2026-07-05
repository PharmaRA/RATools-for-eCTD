type PackageReviewPanelStateInput = {
  jobId: string | null
  loading: boolean
  report: unknown | null
  reportError: unknown | null
  artifacts: readonly unknown[]
  artifactsLoaded: boolean
  artifactsError: unknown | null
}

export const buildPackageReviewPanelState = ({
  jobId,
  loading,
  report,
  reportError,
  artifacts,
  artifactsLoaded,
  artifactsError,
}: PackageReviewPanelStateInput) => {
  const reportLoaded = !reportError && !!report
  const reviewLoading = loading || (!!jobId && !report && !reportError && artifacts.length === 0 && !artifactsError)
  const reviewExportAvailable = reportLoaded || artifactsLoaded

  return {
    reportLoaded,
    reviewLoading,
    reviewExportAvailable,
  }
}
