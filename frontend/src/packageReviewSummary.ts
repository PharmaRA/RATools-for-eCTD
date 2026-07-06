export type PackageReviewArtifactLike = {
  name: string
  exists?: boolean
  sizeBytes?: number
  contentType?: string
}

export type RequiredArtifactSummary = {
  presentCount: number
  existsByName: Record<string, boolean>
  rows: PackageReviewArtifactLike[]
}

export type PackageReviewArtifactIndex<TArtifact extends PackageReviewArtifactLike> = {
  firstArtifactByName: Map<string, TArtifact>
  existingNames: Set<string>
}

export const buildPackageReviewArtifactIndex = <TArtifact extends PackageReviewArtifactLike>(
  artifacts: TArtifact[],
): PackageReviewArtifactIndex<TArtifact> => {
  const firstArtifactByName = new Map<string, TArtifact>()
  const existingNames = new Set<string>()

  for (const artifact of artifacts) {
    if (!firstArtifactByName.has(artifact.name)) {
      firstArtifactByName.set(artifact.name, artifact)
    }

    if (artifact.exists === true) {
      existingNames.add(artifact.name)
    }
  }

  return {
    firstArtifactByName,
    existingNames,
  }
}

export const summarizeRequiredArtifacts = <TArtifact extends PackageReviewArtifactLike>(
  artifacts: TArtifact[],
  requiredNames: string[],
): RequiredArtifactSummary => {
  const { firstArtifactByName, existingNames } = buildPackageReviewArtifactIndex(artifacts)
  const existsByName: Record<string, boolean> = {}
  let presentCount = 0
  const rows = requiredNames.map((name) => {
    const exists = existingNames.has(name)
    existsByName[name] = exists
    if (exists) {
      presentCount += 1
    }
    return firstArtifactByName.get(name) ?? { name, exists: false }
  })

  return {
    presentCount,
    existsByName,
    rows,
  }
}
