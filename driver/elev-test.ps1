# Marker test for elevation
$marker = "C:\Users\dir.minister\.asterxa-ai\desktop-workspace\MagicMouseEnhancer\driver\elev-test.txt"
try {
    "ran as admin at $(Get-Date -Format 'HH:mm:ss')" | Out-File -FilePath $marker -Encoding ascii
} catch {
    "FAILED: $($_.Exception.Message)" | Out-File -FilePath $marker -Encoding ascii
}
