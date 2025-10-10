# Simple debug script to understand the parser behavior
$bundleJson = @"
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "fullUrl": "urn:uuid:test",
      "resource": {
        "resourceType": "Patient",
        "id": "patient-1"
      },
      "request": {
        "method": "PUT",
        "url": "Patient/patient-1"
      }
    }
  ]
}
"@

Write-Host "JSON to parse:"
Write-Host $bundleJson
Write-Host "`nJSON length: $($bundleJson.Length) bytes"
Write-Host "Contains 'entry': $($bundleJson.Contains('entry'))"
