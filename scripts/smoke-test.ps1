param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$KeepSampleFile,
    [switch]$SkipAuditCheck,
    [switch]$CleanPublishOutput
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
$sampleContent = @(
    "RATools smoke test file"
    "Generated: $(Get-Date -Format o)"
    "This file is used to verify upload, placement, validation, and publish flow."
) -join [Environment]::NewLine

Set-Content -Path $sampleFilePath -Value $sampleContent -Encoding UTF8

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

    Write-Step "Creating document placement"
    $placement = Invoke-JsonPost -Url "$BaseUrl/api/document-placements" -Body @{
        documentId = $document.id
        applicationId = $application.id
        sequenceNumber = "0000"
        ctdSection = "m5.3.5.1"
        operation = "new"
        title = "Smoke Test Study Report"
    }

    Write-Step "Running validation"
    $validation = Invoke-JsonPost -Url "$BaseUrl/api/validation/sequence" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    Write-Step "Creating publish job"
    $publishJob = Invoke-JsonPost -Url "$BaseUrl/api/publish-jobs" -Body @{
        applicationId = $application.id
        sequenceNumber = "0000"
    }

    if ($publishJob.status -ne "Completed") {
        throw "Publish job did not complete successfully. Failure: $($publishJob.failureReason)"
    }

    Write-Step "Verifying generated artifacts"
    if ([string]::IsNullOrWhiteSpace($publishJob.outputPath) -or -not (Test-Path $publishJob.outputPath)) {
        throw "Output index.xml path does not exist: $($publishJob.outputPath)"
    }

    if ([string]::IsNullOrWhiteSpace($publishJob.packagePath) -or -not (Test-Path $publishJob.packagePath)) {
        throw "Package zip path does not exist: $($publishJob.packagePath)"
    }

    if (-not $SkipAuditCheck) {
        Write-Step "Checking audit linkage"
        $auditLogs = Invoke-JsonGet -Url "$BaseUrl/api/audit-logs"
        $publishJobAudit = $auditLogs | Where-Object {
            $_.entityType -eq "PublishJob" -and $_.entityId -eq $publishJob.id
        }

        if (-not $publishJobAudit -or $publishJobAudit.Count -eq 0) {
            throw "Audit linkage check failed: no PublishJob audit logs found for job $($publishJob.id)."
        }
    }

    Write-Host ""
    Write-Host "Smoke test completed." -ForegroundColor Green
    Write-Host "Application ID : $($application.id)"
    Write-Host "Document ID    : $($document.id)"
    Write-Host "Placement ID   : $($placement.id)"
    Write-Host "Valid          : $($validation.isValid)"
    Write-Host "Publish Job ID : $($publishJob.id)"
    Write-Host "Status         : $($publishJob.status)"
    Write-Host "Index Path     : $($publishJob.outputPath)"
    Write-Host "Package Path   : $($publishJob.packagePath)"

    if (-not $validation.isValid) {
        Write-Host ""
        Write-Host "Validation issues:" -ForegroundColor Yellow
        $validation.issues | ForEach-Object {
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
}
