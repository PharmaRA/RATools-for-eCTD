param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$ApiKey = "dev-api-key-do-not-use-in-production",
    [switch]$KeepSampleFile,
    [switch]$SkipAuditCheck,
    [switch]$CleanPublishOutput,
    [switch]$InjectWarnings,
    [switch]$CorruptReportAfterPublish
)

$ErrorActionPreference = "Stop"
$ApiHeaders = @{ "X-RA-Tools-Api-Key" = $ApiKey }

try {
    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
}
catch {
    throw "Failed to load System.Net.Http assembly. Please ensure .NET Framework/.NET runtime is installed. Details: $($_.Exception.Message)"
}

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-JsonPost {
    param(
        [string]$Url,
        [object]$Body
    )

    return Invoke-RestMethod -Method Post -Uri $Url -Headers $ApiHeaders -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 10)
}

function Invoke-JsonGet {
    param([string]$Url)
    return Invoke-RestMethod -Method Get -Uri $Url -Headers $ApiHeaders
}

function Invoke-TextGet {
    param([string]$Url)
    return Invoke-WebRequest -Method Get -Uri $Url -Headers $ApiHeaders -UseBasicParsing | Select-Object -ExpandProperty Content
}

function Invoke-RequestStatusCode {
    param([string]$Url)

    try {
        Invoke-WebRequest -Method Get -Uri $Url -Headers $ApiHeaders -UseBasicParsing | Out-Null
        return 200
    }
    catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            return [int]$_.Exception.Response.StatusCode
        }
        throw
    }
}

function Download-File {
    param(
        [string]$Url,
        [string]$DestinationPath
    )

    Invoke-WebRequest -Method Get -Uri $Url -Headers $ApiHeaders -OutFile $DestinationPath -UseBasicParsing | Out-Null
}

function Wait-ForPublishJob {
    param(
        [string]$BaseUrl,
        [string]$JobId,
        # 冷启动的 postgres:16 容器 + 首次发布（建索引/编译计划）可能超过 60 秒；
        # 之前固定 60 秒是间歇失败的一个来源。180 秒给慢 runner 留足余量。
        [int]$TimeoutSeconds = 180
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $job = Invoke-JsonGet -Url "$BaseUrl/api/publish-jobs/$JobId"
        if ($job.status -eq "Completed" -or $job.status -eq "Failed") {
            return $job
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Publish job $JobId did not reach a terminal status within $TimeoutSeconds seconds."
}

function Invoke-FileUpload {
    param(
        [string]$Url,
        [string]$FilePath,
        [string]$CtdSection
    )

    $httpClient = New-Object System.Net.Http.HttpClient
    try {
        $httpClient.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", $ApiKey)
        $multipart = New-Object System.Net.Http.MultipartFormDataContent
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $fileName = [System.IO.Path]::GetFileName($FilePath)

        $fileContent = New-Object System.Net.Http.ByteArrayContent -ArgumentList (, $fileBytes)
        $fileContent.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf")
        $multipart.Add($fileContent, "File", $fileName)

        $sectionContent = New-Object System.Net.Http.StringContent($CtdSection)
        $multipart.Add($sectionContent, "CtdSection")

        $response = $httpClient.PostAsync($Url, $multipart).GetAwaiter().GetResult()
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        if (-not $response.IsSuccessStatusCode) {
            throw "Upload failed with status $([int]$response.StatusCode): $responseBody"
        }

        return $responseBody | ConvertFrom-Json
    }
    finally {
        $httpClient.Dispose()
    }
}

# 生成最小但真实合规的 PDF：可解析版本头、可搜索文本、Type3 自包含字体（满足
# 字体嵌入检查）、书签。旧样本是纯文本伪 PDF，诚实化后的 PDF 检查器会以
# PDF_PARSE_FAILED 正确阻断发布。
function New-SmokePdf {
    param(
        [string]$Path,
        [string]$Text
    )

    $glyph = "750 0 0 0 750 750 d1`n0 0 750 750 re f"
    $content = "BT /F1 24 Tf 72 700 Td ($Text) Tj ET"
    # 覆盖 ASCII 32(空格)-90(Z)：空格也要在 Widths/Encoding 内，否则 PdfPig 解析报错。
    $charCodes = 32..90
    $charNames = ($charCodes | ForEach-Object { "/c$_" })
    $widths = (@("750") * $charCodes.Count) -join " "
    $charProcs = ($charNames | ForEach-Object { "$_ 10 0 R" }) -join " "
    $differences = $charNames -join " "

    $objects = @(
        "<< /Type /Catalog /Pages 2 0 R /Outlines 6 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
        "<< /Type /Font /Subtype /Type3 /Name /F1 /FontBBox [0 0 750 750] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs 8 0 R /Encoding 9 0 R /FirstChar 32 /LastChar 90 /Widths [$widths] >>",
        "<< /Length $($content.Length) >>`nstream`n$content`nendstream",
        "<< /Type /Outlines /First 7 0 R /Last 7 0 R /Count 1 >>",
        "<< /Title (Smoke Bookmark) /Parent 6 0 R /Dest [3 0 R /Fit] >>",
        "<< $charProcs >>",
        "<< /Type /Encoding /Differences [32 $differences] >>",
        "<< /Length $($glyph.Length) >>`nstream`n$glyph`nendstream"
    )

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append("%PDF-1.4`n")
    $offsets = @()
    for ($index = 0; $index -lt $objects.Count; $index += 1) {
        $offsets += $builder.Length
        [void]$builder.Append("$($index + 1) 0 obj`n$($objects[$index])`nendobj`n")
    }

    $xrefOffset = $builder.Length
    [void]$builder.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
    foreach ($offset in $offsets) {
        [void]$builder.Append(("{0:D10} 00000 n `n" -f $offset))
    }
    [void]$builder.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n")

    [System.IO.File]::WriteAllText($Path, $builder.ToString(), [System.Text.Encoding]::ASCII)
}

# 跨平台临时目录：Linux 的 pwsh 没有 $env:TEMP，回退到 GetTempPath()（/tmp）。
$tempRoot = if ($env:TEMP) { $env:TEMP } else { [System.IO.Path]::GetTempPath() }
$sampleFilePath = Join-Path $tempRoot "ratools-smoke-sample.pdf"
$duplicateSampleDirectory = Join-Path $tempRoot ("ratools-smoke-" + [guid]::NewGuid().ToString("N"))
$duplicateSampleFilePath = Join-Path $duplicateSampleDirectory "ratools-smoke-sample.pdf"
$applicationWorkspaceParentPath = Join-Path $tempRoot ("ratools-workspace-" + [guid]::NewGuid().ToString("N"))
$downloadedZipPath = $null

New-SmokePdf -Path $sampleFilePath -Text "SMOKE TEST DOCUMENT"
New-Item -ItemType Directory -Path $duplicateSampleDirectory -Force | Out-Null
New-SmokePdf -Path $duplicateSampleFilePath -Text "SMOKE TEST DUPLICATE"
New-Item -ItemType Directory -Path $applicationWorkspaceParentPath -Force | Out-Null

try {
    $suffix = Get-Date -Format "yyyyMMddHHmmss"

    Write-Step "Checking API health"
    $health = Invoke-JsonGet -Url "$BaseUrl/health"
    if ($health.status -ne "ok") {
        throw "Health check failed. Response: $($health | ConvertTo-Json -Depth 5)"
    }

    if ($CleanPublishOutput) {
        Write-Step "Cleaning publish output directory"
        $publishPath = Join-Path (Get-Location) "src\RATools.Api\App_Data\publish"
        if (Test-Path $publishPath) {
            Remove-Item -Path $publishPath -Recurse -Force
        }
    }

    Write-Step "Creating application"
    $application = Invoke-JsonPost -Url "$BaseUrl/api/applications" -Body @{
        applicationNumber = "IND-$suffix"
        ectdTemplateKey = "us-fda-ectd-3.2.2"
        sponsorName = "Smoke Test Sponsor"
        workingDirectoryParentPath = $applicationWorkspaceParentPath
    }

    $expectedApplicationWorkspacePath = Join-Path $applicationWorkspaceParentPath $application.applicationNumber
    if (-not (Test-Path $application.workingDirectoryPath)) {
        throw "Application working directory was not created: $($application.workingDirectoryPath)"
    }

    $applicationWorkspaceResolvedPath = (Get-Item $application.workingDirectoryPath).FullName
    $expectedApplicationWorkspaceResolvedPath = (Get-Item $expectedApplicationWorkspacePath).FullName
    if ($applicationWorkspaceResolvedPath -ne $expectedApplicationWorkspaceResolvedPath) {
        throw "Application workingDirectoryPath '$applicationWorkspaceResolvedPath' did not match expected '$expectedApplicationWorkspaceResolvedPath'."
    }

    Write-Step "Creating sequence 0000"
    $sequence = Invoke-JsonPost -Url "$BaseUrl/api/applications/$($application.id)/sequences" -Body @{
        sequenceNumber = "0000"
        submissionType = "original-application"
        description = "Smoke test submission"
    }

    $createdSequence = $sequence.sequences | Where-Object { $_.sequenceNumber -eq "0000" } | Select-Object -First 1
    $expectedSequenceWorkspacePath = Join-Path $application.workingDirectoryPath "0000"
    if (-not $createdSequence) {
        throw "Created sequence 0000 was not returned in application payload."
    }

    if (-not (Test-Path $createdSequence.workingDirectoryPath)) {
        throw "Sequence working directory was not created: $($createdSequence.workingDirectoryPath)"
    }

    $sequenceWorkspaceResolvedPath = (Get-Item $createdSequence.workingDirectoryPath).FullName
    $expectedSequenceWorkspaceResolvedPath = (Get-Item $expectedSequenceWorkspacePath).FullName
    if ($sequenceWorkspaceResolvedPath -ne $expectedSequenceWorkspaceResolvedPath) {
        throw "Sequence workingDirectoryPath '$sequenceWorkspaceResolvedPath' did not match expected '$expectedSequenceWorkspaceResolvedPath'."
    }

    $uploadSection = "m1.1"
    $expectedUploadDirectory = Join-Path $sequenceWorkspaceResolvedPath (Join-Path "m1" (Join-Path "us" "11-forms"))

    Write-Step "Uploading sample document"
    $document = Invoke-FileUpload -Url "$BaseUrl/api/applications/$($application.id)/sequences/0000/documents/upload" -FilePath $sampleFilePath -CtdSection $uploadSection

    Write-Step "Uploading duplicate-name document"
    $duplicateDocument = Invoke-FileUpload -Url "$BaseUrl/api/applications/$($application.id)/sequences/0000/documents/upload" -FilePath $duplicateSampleFilePath -CtdSection $uploadSection

    $documentStorageParent = (Get-Item (Split-Path $document.storagePath -Parent)).FullName
    if ($documentStorageParent -ne $expectedUploadDirectory) {
        throw "Uploaded document storagePath '$($document.storagePath)' is not inside the expected canonical folder '$expectedUploadDirectory'."
    }

    $duplicateDocumentStorageParent = (Get-Item (Split-Path $duplicateDocument.storagePath -Parent)).FullName
    if ($duplicateDocumentStorageParent -ne $expectedUploadDirectory) {
        throw "Uploaded duplicate document storagePath '$($duplicateDocument.storagePath)' is not inside the expected canonical folder '$expectedUploadDirectory'."
    }

    Write-Step "Creating document placement"
    $placementPayload = @{
        documentId = $document.id
        applicationId = $application.id
        sequenceNumber = "0000"
        ctdSection = if ($InjectWarnings) { "m3.p.s.1" } else { $uploadSection }
        operation = "new"
    }

    if (-not $InjectWarnings) {
        $placementPayload.title = "Smoke Test Study Report"
    }

    $placement = Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body $placementPayload

    Write-Step "Creating second placement for duplicate-name document"
    $duplicatePlacementPayload = @{
        documentId = $duplicateDocument.id
        applicationId = $application.id
        sequenceNumber = "0000"
        ctdSection = if ($InjectWarnings) { "m3.p.s.1" } else { $uploadSection }
        operation = "new"
    }

    if (-not $InjectWarnings) {
        $duplicatePlacementPayload.title = "Smoke Test Study Report Duplicate"
    }

    $duplicatePlacement = Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body $duplicatePlacementPayload

    if (-not $InjectWarnings) {
        $originalDocumentPath = $document.storagePath
        $reassignedSection = "m5.3.7"
        $expectedReassignedDirectory = Join-Path $sequenceWorkspaceResolvedPath (Join-Path "m5" (Join-Path "53-clinical-study-reports" "537-case-report-forms-and-individual-patient-listings"))

        Write-Step "Reassigning document placement to canonical clinical section"
        $placement = Invoke-RestMethod -Method Put -Uri "$BaseUrl/api/document-placements/$($placement.id)/section" -Headers $ApiHeaders -ContentType "application/json" -Body (@{ ctdSection = $reassignedSection } | ConvertTo-Json)
        $document = Invoke-JsonGet -Url "$BaseUrl/api/documents/$($document.id)"

        if ($placement.ctdSection -ne $reassignedSection) {
            throw "Updated placement section '$($placement.ctdSection)' did not match expected '$reassignedSection'."
        }

        $reassignedDocumentStorageParent = (Get-Item (Split-Path $document.storagePath -Parent)).FullName
        if ($reassignedDocumentStorageParent -ne $expectedReassignedDirectory) {
            throw "Reassigned document storagePath '$($document.storagePath)' is not inside the expected canonical folder '$expectedReassignedDirectory'."
        }

        if (Test-Path $originalDocumentPath) {
            throw "Reassigned document should no longer exist at original path '$originalDocumentPath'."
        }

        if (-not (Test-Path $document.storagePath)) {
            throw "Reassigned document file was not found at '$($document.storagePath)'."
        }
    }

    if ($InjectWarnings) {
        Write-Step "Injecting duplicate placement warning scenario"
        Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body @{
            documentId = $document.id
            applicationId = $application.id
            sequenceNumber = "0000"
            ctdSection = "m3.p.s.1"
            operation = "new"
        } | Out-Null
    }

    Write-Step "Running validation"
    $validation = Invoke-JsonPost -Url "$BaseUrl/api/validation/sequence" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    $matchedSection = $validation.sectionMatches | Where-Object { $_.sectionPath -eq "m5.3.7" -or $_.sectionPath -eq "m3.p.s.1" } | Select-Object -First 1
    if (-not $matchedSection) {
        throw "Validation report did not include sectionMatches for the current placement path."
    }

    if ($validation.lifecycleMatches.Count -ne 0) {
        throw "Default smoke test scenario should not produce lifecycle matches, but $($validation.lifecycleMatches.Count) were returned."
    }

    if (-not $InjectWarnings -and $matchedSection.matchedPrefix -ne "m5.3.7") {
        throw "Validation report matchedPrefix '$($matchedSection.matchedPrefix)' did not match expected 'm5.3.7'."
    }

    Write-Step "Populating US Regional publishing metadata"
    # US Regional backbone 生成要求联系人元数据（readiness/publish 会正确阻断缺失项）。
    Invoke-RestMethod -Method Put -Uri "$BaseUrl/api/applications/$($application.id)/sequences/0000/publishing-metadata" -Headers $ApiHeaders -ContentType "application/json" -Body (@{
        applicationType = "ind"
        submissionType = "original-application"
        submissionSubtype = "initial"
        sequenceDescription = "Smoke test submission"
        applicantName = "Smoke Test Sponsor"
        formType = "1571"
        applicantContactName = "Smoke Contact"
        applicantContactType = "regulatory"
        telephone = "301-555-0100"
        telephoneNumberType = "office"
        email = "smoke@example.test"
    } | ConvertTo-Json) | Out-Null

    Write-Step "Executing publish job"
    $acceptedJob = Invoke-JsonPost -Url "$BaseUrl/api/publish-jobs/execute" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    Write-Step "Waiting for background publish job to complete"
    $publishJob = Wait-ForPublishJob -BaseUrl $BaseUrl -JobId $acceptedJob.id

    # 失败的作业也会到达终态；不断言 Completed 会把失败静默吞掉继续读报告。
    if ($publishJob.status -ne "Completed") {
        throw "Publish job $($publishJob.id) ended as '$($publishJob.status)': $($publishJob.failureReason)"
    }

    if ($CorruptReportAfterPublish) {
        Write-Step "Corrupting persisted publish report"
        $reportForCorruption = Invoke-JsonGet -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/report"
        Set-Content -Path $reportForCorruption.reportPath -Value "{not-json}" -Encoding UTF8

        $corruptedReportStatus = Invoke-RequestStatusCode -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/report"
        if ($corruptedReportStatus -ne 422) {
            throw "Corrupted publish report detail should return 422, got $corruptedReportStatus."
        }
    }

    Write-Step "Reading persisted publish report"
    $persistedReport = $null
    if (-not $CorruptReportAfterPublish) {
        $persistedReport = Invoke-JsonGet -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/report"
    }

    Write-Step "Reading publish artifacts"
    $artifacts = Invoke-JsonGet -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/artifacts"

    Write-Step "Reading application publish history"
    $publishHistory = Invoke-JsonGet -Url "$BaseUrl/api/applications/$($application.id)/publish-history"

    Write-Step "Reading filtered and paged application publish history"
    $filteredPublishHistory = Invoke-JsonGet -Url "$BaseUrl/api/applications/$($application.id)/publish-history?sequenceNumber=0000&page=1&pageSize=1"
    $pagedPublishHistory = Invoke-JsonGet -Url "$BaseUrl/api/applications/$($application.id)/publish-history?sequenceNumber=0000&page=2&pageSize=1"

    $createdFromUtc = [uri]::EscapeDataString((Get-Date).ToUniversalTime().AddDays(-1).ToString("o"))
    $createdToUtc = [uri]::EscapeDataString((Get-Date).ToUniversalTime().AddDays(1).ToString("o"))
    Write-Step "Reading status and date filtered publish history"
    $statusDateFilteredHistory = Invoke-JsonGet -Url "$BaseUrl/api/applications/$($application.id)/publish-history?status=Completed&createdFromUtc=$createdFromUtc&createdToUtc=$createdToUtc&page=1&pageSize=20"

    Write-Step "Downloading persisted publish report"
    $downloadedReportContent = Invoke-TextGet -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/artifacts/PublishReport/download"

    $downloadedZipPath = Join-Path $tempRoot "ratools-smoke-$($publishJob.id)-package.zip"
    Write-Step "Downloading package zip"
    Download-File -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/artifacts/PackageZip/download" -DestinationPath $downloadedZipPath

    if ($publishJob.status -ne "Completed") {
        throw "Publish job did not complete successfully. Failure: $($publishJob.failureReason)"
    }

    if (-not $CorruptReportAfterPublish) {
        if (-not $persistedReport.integritySummary) {
            throw "Persisted publish report did not include integritySummary."
        }

        if (-not $persistedReport.integritySummary.isConsistent) {
            throw "Persisted publish report integritySummary reported inconsistent artifacts."
        }

        if (-not $persistedReport.reportVersion) {
            throw "Persisted publish report did not include a reportVersion."
        }

        if (-not $persistedReport.reportPath) {
            throw "Persisted publish report did not include a reportPath."
        }
    }
    else {
        $reportStatusCode = Invoke-RequestStatusCode -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/report"
        if ($reportStatusCode -ne 422) {
            throw "Corrupted publish report should return 422, actual status was '$reportStatusCode'."
        }
    }

    $requiredArtifacts = @("BackboneXml", "PublishReport", "PackageZip")
    foreach ($artifactName in $requiredArtifacts) {
        $artifact = $artifacts.artifacts | Where-Object { $_.name -eq $artifactName } | Select-Object -First 1
        if (-not $artifact) {
            throw "Artifacts endpoint did not return required artifact '$artifactName'."
        }

        if (-not $artifact.exists) {
            throw "Artifact '$artifactName' exists flag is false."
        }
    }

    $historyEntry = $publishHistory.entries | Where-Object { $_.publishJobId -eq $publishJob.id } | Select-Object -First 1
    if (-not $historyEntry) {
        throw "Publish history did not contain the current publish job '$($publishJob.id)'."
    }

    if ($historyEntry.sequenceNumber -ne "0000") {
        throw "Publish history entry sequence '$($historyEntry.sequenceNumber)' did not match expected '0000'."
    }

    if ($historyEntry.status -ne $publishJob.status) {
        throw "Publish history entry status '$($historyEntry.status)' did not match publish job status '$($publishJob.status)'."
    }

    if ($null -eq $historyEntry.lifecycleMatches) {
        throw "Publish history entry did not include lifecycleMatches."
    }

    if ($historyEntry.lifecycleMatches.Count -ne 0) {
        throw "Default smoke test scenario should not produce lifecycleMatches on the publish history entry."
    }

    if ($CorruptReportAfterPublish) {
        if (-not $historyEntry.reportAvailable -or -not $historyEntry.reportReadable) {
            throw "Publish history should retain its materialized report snapshot after detail evidence is corrupted."
        }
    }

    if ($filteredPublishHistory.page -ne 1 -or $filteredPublishHistory.pageSize -ne 1) {
        throw "Filtered publish history did not preserve requested paging values."
    }

    if ($filteredPublishHistory.totalCount -lt 1) {
        throw "Filtered publish history totalCount should be at least 1 for sequence 0000."
    }

    if ($filteredPublishHistory.entries.Count -ne 1) {
        throw "Filtered publish history page should contain exactly 1 entry."
    }

    if ($filteredPublishHistory.entries[0].sequenceNumber -ne "0000") {
        throw "Filtered publish history returned sequence '$($filteredPublishHistory.entries[0].sequenceNumber)' instead of '0000'."
    }

    if ($pagedPublishHistory.page -ne 2 -or $pagedPublishHistory.pageSize -ne 1) {
        throw "Paged publish history did not preserve requested second-page values."
    }

    if ($pagedPublishHistory.entries.Count -ne 0) {
        throw "Second page of filtered publish history should be empty in smoke test scenario."
    }

    $statusDateEntry = $statusDateFilteredHistory.entries | Where-Object { $_.publishJobId -eq $publishJob.id } | Select-Object -First 1
    if (-not $statusDateEntry) {
        throw "Status/date filtered publish history did not contain the current publish job '$($publishJob.id)'."
    }

    if ($statusDateEntry.status -ne "Completed") {
        throw "Status/date filtered publish history returned status '$($statusDateEntry.status)' instead of 'Completed'."
    }

    if (-not $publishHistory.statusSummary) {
        throw "Publish history did not return statusSummary."
    }

    if (-not $publishHistory.lifecycleSummary) {
        throw "Publish history did not return lifecycleSummary."
    }

    if ($publishHistory.lifecycleSummary.matchedCount -ne 0 -or
        $publishHistory.lifecycleSummary.replaceTargetNotFoundCount -ne 0 -or
        $publishHistory.lifecycleSummary.deleteTargetNotFoundCount -ne 0 -or
        $publishHistory.lifecycleSummary.appendTargetNotFoundCount -ne 0 -or
        $publishHistory.lifecycleSummary.ambiguousCount -ne 0 -or
        $publishHistory.lifecycleSummary.currentSequenceCount -ne 0) {
        throw "Default smoke test scenario should not produce lifecycle summary counts in publish history."
    }

    if ($publishHistory.statusSummary.completedCount -lt 1) {
        throw "Publish history statusSummary completedCount should be at least 1."
    }

    if (-not $filteredPublishHistory.statusSummary -or $filteredPublishHistory.statusSummary.completedCount -lt 1) {
        throw "Filtered publish history statusSummary should report at least one completed entry."
    }

    if (-not $statusDateFilteredHistory.statusSummary) {
        throw "Status/date filtered publish history did not return statusSummary."
    }

    if ($statusDateFilteredHistory.statusSummary.completedCount -ne 1) {
        throw "Status/date filtered publish history completedCount '$($statusDateFilteredHistory.statusSummary.completedCount)' did not match expected 1."
    }

    if ($statusDateFilteredHistory.statusSummary.failedCount -ne 0 -or $statusDateFilteredHistory.statusSummary.runningCount -ne 0) {
        throw "Status/date filtered publish history statusSummary should only contain completed entries in smoke test scenario."
    }

    if ([string]::IsNullOrWhiteSpace($downloadedReportContent)) {
        throw "Downloaded publish report content is empty."
    }

    if ($downloadedReportContent -notmatch '"reportVersion"') {
        throw "Downloaded publish report does not contain the expected reportVersion field."
    }

    if (-not (Test-Path $downloadedZipPath)) {
        throw "Downloaded package zip file was not created."
    }

    $packageArtifact = $artifacts.artifacts | Where-Object { $_.name -eq "PackageZip" } | Select-Object -First 1
    if (-not $packageArtifact) {
        throw "PackageZip artifact metadata was not returned."
    }

    $downloadedZipSize = (Get-Item $downloadedZipPath).Length
    if ($downloadedZipSize -ne [int64]$packageArtifact.sizeBytes) {
        throw "Downloaded package zip size '$downloadedZipSize' does not match artifact metadata '$($packageArtifact.sizeBytes)'."
    }

    Write-Step "Verifying generated artifacts"
    if ([string]::IsNullOrWhiteSpace($publishJob.outputPath) -or -not (Test-Path $publishJob.outputPath)) {
        throw "Output index.xml path does not exist: $($publishJob.outputPath)"
    }

    $indexXmlContent = Get-Content -Path $publishJob.outputPath -Raw
    # href 规则与 PublishOutputNaming 一致：storagePath 中序列号段之后的相对路径（'/' 分隔）。
    function Get-ExpectedHref {
        param([string]$StoragePath, [string]$SequenceNumber)
        $segments = $StoragePath -split '[\/]' | Where-Object { $_ -ne '' }
        $sequenceIndex = [Array]::LastIndexOf($segments, $SequenceNumber)
        if ($sequenceIndex -ge 0 -and $sequenceIndex -lt ($segments.Count - 1)) {
            return ($segments[($sequenceIndex + 1)..($segments.Count - 1)] -join '/')
        }
        return [System.IO.Path]::GetFileName($StoragePath)
    }

    $expectedDocumentHref = Get-ExpectedHref -StoragePath $document.storagePath -SequenceNumber "0000"
    if ($indexXmlContent -notmatch [regex]::Escape($expectedDocumentHref)) {
        throw "Generated index.xml does not contain the expected unique document href '$expectedDocumentHref'."
    }

    # 重复文档的 placement 在 m1（区域 backbone）：href 写入 us-regional.xml 而非 index.xml
    # （index.xml 只承载 ICH M2-M5 leaves）。区域 href 相对 us-regional.xml 所在目录。
    $regionalXmlPath = Join-Path (Split-Path $publishJob.outputPath -Parent) (Join-Path "m1" (Join-Path "us" "us-regional.xml"))
    if (-not (Test-Path $regionalXmlPath)) {
        throw "Generated us-regional.xml was not found at '$regionalXmlPath'."
    }
    $regionalXmlContent = Get-Content -Path $regionalXmlPath -Raw
    $duplicateStoredFileName = [System.IO.Path]::GetFileName($duplicateDocument.storagePath)
    if ($regionalXmlContent -notmatch [regex]::Escape($duplicateStoredFileName)) {
        throw "Generated us-regional.xml does not reference the duplicate document file '$duplicateStoredFileName'."
    }

    if ($indexXmlContent -notmatch 'dtd-version="3\.2"') {
        throw "Generated index.xml does not declare the expected dtd-version=3.2 metadata."
    }

    if ($indexXmlContent -notmatch 'xlink:type="simple"') {
        throw 'Generated index.xml does not contain xlink:type="simple" on leaf nodes.'
    }

    if ($indexXmlContent -notmatch 'checksum-type="md5"') {
        throw 'Generated index.xml does not contain checksum-type="md5" on leaf nodes.'
    }

    if (-not $CorruptReportAfterPublish) {
        if ([string]::IsNullOrWhiteSpace($persistedReport.reportPath) -or -not (Test-Path $persistedReport.reportPath)) {
            throw "Publish report path does not exist: $($persistedReport.reportPath)"
        }
    }

    if ([string]::IsNullOrWhiteSpace($publishJob.packagePath) -or -not (Test-Path $publishJob.packagePath)) {
        throw "Package zip path does not exist: $($publishJob.packagePath)"
    }

    if ($publishJob.packagePath -notmatch $publishJob.id.Replace('-', '')) {
        throw "Package zip path '$($publishJob.packagePath)' does not contain the publish job id, so history may still be overwritten."
    }

    if (-not $SkipAuditCheck) {
        Write-Step "Checking audit linkage"
        # 服务端过滤 + 分页（A3）：按 entityType/entityId 精确取，不再全表拉回客户端筛。
        $publishJobAudit = (Invoke-JsonGet -Url "$BaseUrl/api/audit-logs?entityType=PublishJob&entityId=$($publishJob.id)&pageSize=200").items

        $validationAudit = (Invoke-JsonGet -Url "$BaseUrl/api/audit-logs?entityType=SequenceValidation&entityId=$($application.id):0000&pageSize=200").items

        # PublishJobArtifact 的 entityId 是 "<jobId>:<role>" 前缀形态，无法精确匹配；
        # 按 entityType 取一页后在客户端筛前缀。
        $artifactAudit = (Invoke-JsonGet -Url "$BaseUrl/api/audit-logs?entityType=PublishJobArtifact&pageSize=200").items |
            Where-Object { $_.entityId -like "$($publishJob.id):*" }

        # pageSize clamp 守卫：请求 999 必须被压到 200 上限。
        $clampProbe = Invoke-JsonGet -Url "$BaseUrl/api/audit-logs?pageSize=999"
        if ($clampProbe.pageSize -ne 200) {
            throw "Audit log pageSize clamp failed: requested 999 but response reports pageSize $($clampProbe.pageSize) instead of 200."
        }

        if (-not $publishJobAudit -or $publishJobAudit.Count -eq 0) {
            throw "Audit linkage check failed: no PublishJob audit logs found for job $($publishJob.id)."
        }

        if (-not $validationAudit -or $validationAudit.Count -eq 0) {
            throw "Audit linkage check failed: no SequenceValidation audit logs found for application $($application.id), sequence 0000."
        }

        if (-not $artifactAudit -or $artifactAudit.Count -eq 0) {
            throw "Audit linkage check failed: no PublishJobArtifact audit logs found for job $($publishJob.id)."
        }

        Write-Host ""
        Write-Host "Audit details (PublishJob):" -ForegroundColor DarkCyan
        $publishJobAudit |
            Sort-Object createdUtc |
            ForEach-Object {
                Write-Host "- $($_.createdUtc) [$($_.action)] $($_.details)"
            }

        Write-Host ""
        Write-Host "Audit details (SequenceValidation):" -ForegroundColor DarkCyan
        $validationAudit |
            Sort-Object createdUtc |
            ForEach-Object {
                Write-Host "- $($_.createdUtc) [$($_.action)] $($_.details)"
            }

        $validationAuditEntry = $validationAudit | Sort-Object createdUtc | Select-Object -Last 1
        if ($validationAuditEntry.details -notmatch 'MatchedPrefixes=') {
            throw "SequenceValidation audit details do not include MatchedPrefixes summary."
        }

        if ($validationAuditEntry.details -notmatch 'LifecycleResults=none') {
            throw "SequenceValidation audit details do not include the expected LifecycleResults=none summary for the smoke test scenario."
        }

        Write-Host ""
        Write-Host "Audit details (PublishJobArtifact):" -ForegroundColor DarkCyan
        $artifactAudit |
            Sort-Object createdUtc |
            ForEach-Object {
                Write-Host "- $($_.createdUtc) [$($_.entityId)] [$($_.action)] $($_.details)"
            }
    }

    Write-Host ""
    Write-Host "Smoke test completed." -ForegroundColor Green
    Write-Host "Report Ver.    : $($persistedReport.reportVersion)"
    Write-Host "Application ID : $($application.id)"
    Write-Host "Document ID    : $($document.id)"
    Write-Host "Document ID 2  : $($duplicateDocument.id)"
    Write-Host "Placement ID   : $($placement.id)"
    Write-Host "Placement ID 2 : $($duplicatePlacement.id)"
    Write-Host "Valid          : $($validation.isValid)"
    Write-Host "Publish Valid  : $($persistedReport.validationReport.isValid)"
    Write-Host "Val Profile    : $($persistedReport.validationProfile)"
    Write-Host "Succeeded      : $($persistedReport.succeeded)"
    Write-Host "Message        : $($persistedReport.message)"
    Write-Host "Duration (ms)  : $($persistedReport.durationMs)"
    Write-Host "Integrity OK   : $($persistedReport.integritySummary.isConsistent)"
    Write-Host "Error Count    : $($persistedReport.errorCount)"
    Write-Host "Warning Count  : $($persistedReport.warningCount)"
    Write-Host "Warn Summary   : $($persistedReport.warningSummary)"
    Write-Host "Publish Job ID : $($publishJob.id)"
    Write-Host "Status         : $($publishJob.status)"
    Write-Host "Report Path    : $($persistedReport.reportPath)"
    Write-Host "Index Path     : $($publishJob.outputPath)"
    Write-Host "Package Path   : $($publishJob.packagePath)"

    if ($persistedReport.artifactSummary) {
        Write-Host "Artifact Files : $($persistedReport.artifactSummary.fileCount)"
        Write-Host "Artifact Bytes : $($persistedReport.artifactSummary.totalSizeBytes)"
        Write-Host "Package Bytes  : $($persistedReport.artifactSummary.packageSizeBytes)"
    }

    if ($persistedReport.auditSummary) {
        Write-Host "Audit(Publish) : $($persistedReport.auditSummary.publishJobEventCount)"
        Write-Host "Audit(Valid)   : $($persistedReport.auditSummary.validationEventCount)"
        Write-Host "Audit Last Act : $($persistedReport.auditSummary.latestPublishJobAction)"
    }

    Write-Host "Artifacts OK   : $($artifacts.artifacts.Count) item(s)"
    Write-Host "Download Check : Passed"

    if (-not $persistedReport.validationReport.isValid) {
        Write-Host ""
        Write-Host "Validation issues:" -ForegroundColor Yellow
        $persistedReport.validationReport.issues | ForEach-Object {
            Write-Host "- [$($_.severity)] $($_.code): $($_.message)"
        }
    }

    if ($InjectWarnings) {
        $nonStandardPatternWarning = $persistedReport.validationReport.issues | Where-Object { $_.code -eq "NON_STANDARD_SECTION_PATTERN" } | Select-Object -First 1
        if (-not $nonStandardPatternWarning) {
            throw "Expected NON_STANDARD_SECTION_PATTERN warning was not returned when InjectWarnings was enabled."
        }
    }

    if (-not $SkipAuditCheck) {
        Write-Host "Audit Check    : Passed"
    }
    else {
        Write-Host "Audit Check    : Skipped"
    }
}
finally {
    if (-not $KeepSampleFile -and (Test-Path $sampleFilePath)) {
        Remove-Item $sampleFilePath -Force
    }

    if (-not $KeepSampleFile -and (Test-Path $duplicateSampleDirectory)) {
        Remove-Item $duplicateSampleDirectory -Recurse -Force
    }

    if (Test-Path $applicationWorkspaceParentPath) {
        Remove-Item $applicationWorkspaceParentPath -Recurse -Force
    }

    if ($downloadedZipPath -and (Test-Path $downloadedZipPath)) {
        Remove-Item $downloadedZipPath -Force
    }
}
