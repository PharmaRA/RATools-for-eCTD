param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$KeepSampleFile,
    [switch]$SkipAuditCheck,
    [switch]$CleanPublishOutput,
    [switch]$InjectWarnings,
    [switch]$CorruptReportAfterPublish
)

$ErrorActionPreference = "Stop"

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

    return Invoke-RestMethod -Method Post -Uri $Url -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 10)
}

function Invoke-JsonGet {
    param([string]$Url)
    return Invoke-RestMethod -Method Get -Uri $Url
}

function Invoke-TextGet {
    param([string]$Url)
    return Invoke-WebRequest -Method Get -Uri $Url -UseBasicParsing | Select-Object -ExpandProperty Content
}

function Invoke-RequestStatusCode {
    param([string]$Url)

    try {
        Invoke-WebRequest -Method Get -Uri $Url -UseBasicParsing | Out-Null
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

    Invoke-WebRequest -Method Get -Uri $Url -OutFile $DestinationPath -UseBasicParsing | Out-Null
}

function Invoke-FileUpload {
    param(
        [string]$Url,
        [string]$FilePath
    )

    $httpClient = New-Object System.Net.Http.HttpClient
    try {
        $multipart = New-Object System.Net.Http.MultipartFormDataContent
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $fileName = [System.IO.Path]::GetFileName($FilePath)

        $fileContent = New-Object System.Net.Http.ByteArrayContent -ArgumentList (, $fileBytes)
        $fileContent.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("text/plain")
        $multipart.Add($fileContent, "File", $fileName)

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

$sampleFilePath = Join-Path $env:TEMP "ratools-smoke-sample.txt"
$duplicateSampleDirectory = Join-Path $env:TEMP ("ratools-smoke-" + [guid]::NewGuid().ToString("N"))
$duplicateSampleFilePath = Join-Path $duplicateSampleDirectory "ratools-smoke-sample.txt"
$downloadedZipPath = $null
$sampleContent = @(
    "RATools smoke test file"
    "Generated: $(Get-Date -Format o)"
    "This file is used to verify upload, placement, validation, and publish flow."
) -join [Environment]::NewLine

Set-Content -Path $sampleFilePath -Value $sampleContent -Encoding UTF8
New-Item -ItemType Directory -Path $duplicateSampleDirectory -Force | Out-Null
Set-Content -Path $duplicateSampleFilePath -Value ($sampleContent + [Environment]::NewLine + "duplicate") -Encoding UTF8

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
        region = "US"
        sponsorName = "Smoke Test Sponsor"
    }

    Write-Step "Creating sequence 0000"
    $sequence = Invoke-JsonPost -Url "$BaseUrl/api/applications/$($application.id)/sequences" -Body @{
        sequenceNumber = "0000"
        submissionType = "original-application"
        description = "Smoke test submission"
    }

    Write-Step "Uploading sample document"
    $document = Invoke-FileUpload -Url "$BaseUrl/api/documents/upload" -FilePath $sampleFilePath

    Write-Step "Uploading duplicate-name document"
    $duplicateDocument = Invoke-FileUpload -Url "$BaseUrl/api/documents/upload" -FilePath $duplicateSampleFilePath

    Write-Step "Creating document placement"
    $placementPayload = @{
        documentId = $document.id
        applicationId = $application.id
        sequenceNumber = "0000"
        ctdSection = if ($InjectWarnings) { "module5" } else { "m5.3.5.1" }
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
        ctdSection = if ($InjectWarnings) { "module5" } else { "m5.3.5.1" }
        operation = "new"
    }

    if (-not $InjectWarnings) {
        $duplicatePlacementPayload.title = "Smoke Test Study Report Duplicate"
    }

    $duplicatePlacement = Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body $duplicatePlacementPayload

    if ($InjectWarnings) {
        Write-Step "Injecting duplicate placement warning scenario"
        Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body @{
            documentId = $document.id
            applicationId = $application.id
            sequenceNumber = "0000"
            ctdSection = "module5"
            operation = "new"
        } | Out-Null
    }

    Write-Step "Running validation"
    $validation = Invoke-JsonPost -Url "$BaseUrl/api/validation/sequence" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    Write-Step "Executing publish job"
    $publishReport = Invoke-JsonPost -Url "$BaseUrl/api/publish-jobs/execute" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    $publishJob = $publishReport.publishJob

    if ($CorruptReportAfterPublish) {
        Write-Step "Corrupting persisted publish report"
        Set-Content -Path $publishReport.reportPath -Value "{not-json}" -Encoding UTF8
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

    $downloadedZipPath = Join-Path $env:TEMP "ratools-smoke-$($publishJob.id)-package.zip"
    Write-Step "Downloading package zip"
    Download-File -Url "$BaseUrl/api/publish-jobs/$($publishJob.id)/artifacts/PackageZip/download" -DestinationPath $downloadedZipPath

    if ($publishJob.status -ne "Completed") {
        throw "Publish job did not complete successfully. Failure: $($publishJob.failureReason)"
    }

    if (-not $CorruptReportAfterPublish) {
        if ($persistedReport.reportVersion -ne $publishReport.reportVersion) {
            throw "Persisted report version '$($persistedReport.reportVersion)' does not match execute response '$($publishReport.reportVersion)'."
        }

        if ($persistedReport.reportPath -ne $publishReport.reportPath) {
            throw "Persisted report path '$($persistedReport.reportPath)' does not match execute response '$($publishReport.reportPath)'."
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

    if ($CorruptReportAfterPublish) {
        if (-not $historyEntry.reportAvailable -or $historyEntry.reportReadable) {
            throw "Corrupted report should remain available but unreadable in publish history."
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
    $expectedDocumentHref = "documents/$($document.id.Replace('-', ''))_$($document.fileName)"
    if ($indexXmlContent -notmatch [regex]::Escape($expectedDocumentHref)) {
        throw "Generated index.xml does not contain the expected unique document href '$expectedDocumentHref'."
    }

    $expectedDuplicateDocumentHref = "documents/$($duplicateDocument.id.Replace('-', ''))_$($duplicateDocument.fileName)"
    if ($indexXmlContent -notmatch [regex]::Escape($expectedDuplicateDocumentHref)) {
        throw "Generated index.xml does not contain the expected unique duplicate document href '$expectedDuplicateDocumentHref'."
    }

    if ([string]::IsNullOrWhiteSpace($publishReport.reportPath) -or -not (Test-Path $publishReport.reportPath)) {
        throw "Publish report path does not exist: $($publishReport.reportPath)"
    }

    if ([string]::IsNullOrWhiteSpace($publishJob.packagePath) -or -not (Test-Path $publishJob.packagePath)) {
        throw "Package zip path does not exist: $($publishJob.packagePath)"
    }

    if ($publishJob.packagePath -notmatch $publishJob.id.Replace('-', '')) {
        throw "Package zip path '$($publishJob.packagePath)' does not contain the publish job id, so history may still be overwritten."
    }

    if (-not $SkipAuditCheck) {
        Write-Step "Checking audit linkage"
        $auditLogs = Invoke-JsonGet -Url "$BaseUrl/api/audit-logs"
        $publishJobAudit = $auditLogs | Where-Object {
            $_.entityType -eq "PublishJob" -and $_.entityId -eq $publishJob.id
        }

        $validationAudit = $auditLogs | Where-Object {
            $_.entityType -eq "SequenceValidation" -and $_.entityId -eq "$($application.id):0000"
        }

        $artifactAudit = $auditLogs | Where-Object {
            $_.entityType -eq "PublishJobArtifact" -and $_.entityId -like "$($publishJob.id):*"
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
    Write-Host "Report Ver.    : $($publishReport.reportVersion)"
    Write-Host "Application ID : $($application.id)"
    Write-Host "Document ID    : $($document.id)"
    Write-Host "Document ID 2  : $($duplicateDocument.id)"
    Write-Host "Placement ID   : $($placement.id)"
    Write-Host "Placement ID 2 : $($duplicatePlacement.id)"
    Write-Host "Valid          : $($validation.isValid)"
    Write-Host "Publish Valid  : $($publishReport.validationReport.isValid)"
    Write-Host "Val Profile    : $($publishReport.validationProfile)"
    Write-Host "Succeeded      : $($publishReport.succeeded)"
    Write-Host "Message        : $($publishReport.message)"
    Write-Host "Duration (ms)  : $($publishReport.durationMs)"
    Write-Host "Error Count    : $($publishReport.errorCount)"
    Write-Host "Warning Count  : $($publishReport.warningCount)"
    Write-Host "Warn Summary   : $($publishReport.warningSummary)"
    Write-Host "Publish Job ID : $($publishJob.id)"
    Write-Host "Status         : $($publishJob.status)"
    Write-Host "Report Path    : $($publishReport.reportPath)"
    Write-Host "Index Path     : $($publishJob.outputPath)"
    Write-Host "Package Path   : $($publishJob.packagePath)"

    if ($publishReport.artifactSummary) {
        Write-Host "Artifact Files : $($publishReport.artifactSummary.fileCount)"
        Write-Host "Artifact Bytes : $($publishReport.artifactSummary.totalSizeBytes)"
        Write-Host "Package Bytes  : $($publishReport.artifactSummary.packageSizeBytes)"
    }

    if ($publishReport.auditSummary) {
        Write-Host "Audit(Publish) : $($publishReport.auditSummary.publishJobEventCount)"
        Write-Host "Audit(Valid)   : $($publishReport.auditSummary.validationEventCount)"
        Write-Host "Audit Last Act : $($publishReport.auditSummary.latestPublishJobAction)"
    }

    Write-Host "Artifacts OK   : $($artifacts.artifacts.Count) item(s)"
    Write-Host "Download Check : Passed"

    if (-not $publishReport.validationReport.isValid) {
        Write-Host ""
        Write-Host "Validation issues:" -ForegroundColor Yellow
        $publishReport.validationReport.issues | ForEach-Object {
            Write-Host "- [$($_.severity)] $($_.code): $($_.message)"
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

    if (Test-Path $downloadedZipPath) {
        Remove-Item $downloadedZipPath -Force
    }
}
