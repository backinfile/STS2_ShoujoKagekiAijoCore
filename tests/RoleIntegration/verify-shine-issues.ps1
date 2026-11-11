param([Parameter(Mandatory)][string]$BaseUrl, [ValidateSet('preview','archive','race','valid','replay','retarget','selfkill')][string]$Mode = 'preview', [string]$Output)
$ErrorActionPreference = 'Stop'
function Command([string]$text) { Invoke-RestMethod ($BaseUrl+'/singleplayer') -Method Post -ContentType application/json -Body (@{action='run_command';command=$text} | ConvertTo-Json -Compress) }
$start = Command ('shine_issues '+$Mode)
if ($start.status -ne 'ok') { throw ($start | ConvertTo-Json -Compress) }
$deadline=[DateTime]::UtcNow.AddSeconds(30)
do {
    Start-Sleep -Milliseconds 200
    $reply=Command 'shine_issues status'
    if ($reply.message -and $reply.message -ne 'running') {
        $report=$reply.message | ConvertFrom-Json
        if ($Output) { $reply.message | Set-Content -Encoding UTF8 -LiteralPath $Output }
        $reply.message
        if (!$report.passed) { throw ('Shine '+$Mode+' regression failed') }
        return
    }
} while ([DateTime]::UtcNow -lt $deadline)
throw 'Shine issue probe timed out'
