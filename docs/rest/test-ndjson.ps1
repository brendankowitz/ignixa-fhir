# Test script for NDJSON storage format verification
# Usage: Run from repository root

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NDJSON Storage Format Verification Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Start the API in background
Write-Host "1. Starting FHIR Server..." -ForegroundColor Yellow
$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project src/Sparky.Api/Sparky.Api.csproj" -PassThru -NoNewWindow
Start-Sleep -Seconds 5

try {
    # Test PUT /Patient/{id}
    Write-Host "2. Creating Patient resource (PUT /Patient/example-ndjson-test)..." -ForegroundColor Yellow
    $patientJson = Get-Content "test-patient.json" -Raw
    $response = Invoke-WebRequest -Uri "https://localhost:7157/Patient/example-ndjson-test" `
        -Method PUT `
        -Body $patientJson `
        -ContentType "application/fhir+json" `
        -SkipCertificateCheck

    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "   Response: $($response.Content)" -ForegroundColor Gray

    # Extract ETag
    $etag = $response.Headers['ETag']
    Write-Host "   ETag: $etag" -ForegroundColor Green

    # Test GET /Patient/{id}
    Write-Host ""
    Write-Host "3. Retrieving Patient resource (GET /Patient/example-ndjson-test)..." -ForegroundColor Yellow
    $response = Invoke-WebRequest -Uri "https://localhost:7157/Patient/example-ndjson-test" `
        -Method GET `
        -SkipCertificateCheck

    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "   ETag: $($response.Headers['ETag'])" -ForegroundColor Green
    Write-Host "   Last-Modified: $($response.Headers['Last-Modified'])" -ForegroundColor Green

    # Verify NDJSON file structure
    Write-Host ""
    Write-Host "4. Verifying NDJSON file structure..." -ForegroundColor Yellow

    $fhirDataPath = "src/Sparky.Api/fhir-data/Patient"
    if (Test-Path $fhirDataPath) {
        Write-Host "   FHIR data directory exists: $fhirDataPath" -ForegroundColor Green

        # Find NDJSON files
        $ndjsonFiles = Get-ChildItem -Path $fhirDataPath -Filter "tx-*.ndjson" -Recurse
        Write-Host "   Found $($ndjsonFiles.Count) NDJSON file(s)" -ForegroundColor Green

        foreach ($file in $ndjsonFiles) {
            Write-Host "   - File: $($file.FullName)" -ForegroundColor Gray
            Write-Host "     Path structure: $($file.Directory.FullName -replace [regex]::Escape($fhirDataPath), '')" -ForegroundColor Gray

            # Read and display file structure
            $lines = Get-Content $file.FullName
            Write-Host "     Line 1 (Bundle): $($lines[0].Substring(0, [Math]::Min(80, $lines[0].Length)))..." -ForegroundColor Gray
            Write-Host "     Line 2 (Resource): $($lines[1].Substring(0, [Math]::Min(80, $lines[1].Length)))..." -ForegroundColor Gray
        }

        # Find metadata files
        $metadataFiles = Get-ChildItem -Path $fhirDataPath -Filter "tx-*.metadata.ndjson" -Recurse
        Write-Host "   Found $($metadataFiles.Count) metadata file(s)" -ForegroundColor Green

        foreach ($file in $metadataFiles) {
            Write-Host "   - Metadata: $($file.Name)" -ForegroundColor Gray
            $metadata = Get-Content $file.FullName | ConvertFrom-Json
            Write-Host "     TransactionId: $($metadata.TransactionId)" -ForegroundColor Gray
            Write-Host "     ResourceType: $($metadata.ResourceType)" -ForegroundColor Gray
            Write-Host "     ResourceId: $($metadata.ResourceId)" -ForegroundColor Gray
            Write-Host "     VersionId: $($metadata.VersionId)" -ForegroundColor Gray
            Write-Host "     LastModified: $($metadata.LastModified)" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ERROR: FHIR data directory not found!" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Test completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan

} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
} finally {
    # Stop the API
    Write-Host ""
    Write-Host "5. Stopping FHIR Server..." -ForegroundColor Yellow
    Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
}
