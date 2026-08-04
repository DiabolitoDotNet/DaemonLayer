$ProgressPreference = 'SilentlyContinue'
$headers = @{ 'X-Infernal-Operator-Key' = 'daemonlayer-local-operator-key' }
$chatId = 5879484069

$agentJson = (Invoke-WebRequest -Uri 'http://localhost:5080/api/agents' -UseBasicParsing -Headers $headers -TimeoutSec 30).Content | ConvertFrom-Json
$target = ($agentJson | Where-Object { $_.name -eq 'Orobas' } | Select-Object -First 1)
if (-not $target) { $target = ($agentJson | Where-Object { $_.rank -eq 'Duke' } | Select-Object -First 1) }
if (-not $target) { throw 'No Duke agent found for suspend/resume test.' }

$cmds = @(
  '/help',
  '/usage',
  '/learning lucifer',
  '/models',
  '/status',
  '/memory facts',
  ('/suspend ' + $target.id),
  ('/resume ' + $target.id)
)

$runStart = Get-Date

function Get-QueueDepth {
  $mb = (Invoke-WebRequest -Uri 'http://localhost:5080/api/perf/message-bus' -UseBasicParsing -Headers $headers -TimeoutSec 30).Content | ConvertFrom-Json
  return [int]$mb.queue.totalDepth
}

$results = @()
foreach ($cmd in $cmds) {
  $start = Get-Date
  $body = (@{ command = $cmd; chatId = $chatId } | ConvertTo-Json -Compress)
  $status = 0

  try {
    $resp = Invoke-WebRequest -Uri 'http://localhost:5080/api/ops/telegram/simulate-command' -Method POST -UseBasicParsing -ContentType 'application/json' -Headers $headers -Body $body -TimeoutSec 120
    $status = [int]$resp.StatusCode
  }
  catch {
    if ($_.Exception.Response) {
      $status = [int]$_.Exception.Response.StatusCode
    }
    else {
      $status = -1
    }
  }

  $deadline = (Get-Date).AddMinutes(2)
  $depth = Get-QueueDepth
  while ($depth -gt 0 -and (Get-Date) -lt $deadline) {
    $depth = Get-QueueDepth
  }

  $since = $start.ToString('o')
  $lines = docker logs infernal-hierarchy --since $since 2>&1

  $escapedCmd = [regex]::Escape($cmd)
  $ingressPattern = "Telegram message from ${chatId}: $escapedCmd \| CorrelationId: (?<cid>[^\s]+)"
  $ingressMatch = ($lines | Select-String -Pattern $ingressPattern | Select-Object -First 1)
  $ingress = $ingressMatch.Line
  $corr = $null
  if ($ingressMatch -and $ingressMatch.Matches.Count -gt 0) {
    $corr = $ingressMatch.Matches[0].Groups['cid'].Value
  }

  if ($corr) {
    $corrPattern = [regex]::Escape($corr)
    $received = ($lines | Select-String -Pattern ("received Query:|received Command:") | Where-Object { $_.Line -match $corrPattern } | Select-Object -First 1).Line
    $processed = ($lines | Select-String -Pattern 'processing task:' | Where-Object { $_.Line -match $corrPattern } | Select-Object -First 1).Line
    $action = ($lines | Select-String -SimpleMatch 'Action: FINAL_ANSWER' | Where-Object { $_.Line -match $corrPattern } | Select-Object -Last 1).Line
    $forward = ($lines | Select-String -SimpleMatch 'Forwarded agent message' | Where-Object { $_.Line -match $corrPattern } | Select-Object -Last 1).Line
  }
  else {
    $received = $null
    $processed = $null
    $action = $null
    $forward = $null
  }

  $results += [pscustomobject]@{
    command = $cmd
    acceptedStatus = $status
    queueDepthAfter = $depth
    correlationId = $corr
    ingress = $ingress
    received = $received
    processed = $processed
    finalAnswer = $action
    forwarded = $forward
  }
}

$allLines = docker logs infernal-hierarchy --since $runStart.ToString('o') 2>&1
foreach ($result in $results) {
  if (-not $result.correlationId) {
    continue
  }

  $corrPattern = [regex]::Escape($result.correlationId)
  if (-not $result.received) {
    $result.received = ($allLines | Select-String -Pattern 'received Query:|received Command:' | Where-Object { $_.Line -match $corrPattern } | Select-Object -First 1).Line
  }

  if (-not $result.processed) {
    $result.processed = ($allLines | Select-String -Pattern 'processing task:' | Where-Object { $_.Line -match $corrPattern } | Select-Object -First 1).Line
  }

  if (-not $result.finalAnswer) {
    $result.finalAnswer = ($allLines | Select-String -SimpleMatch 'Action: FINAL_ANSWER' | Where-Object { $_.Line -match $corrPattern } | Select-Object -Last 1).Line
  }

  if (-not $result.forwarded) {
    $result.forwarded = ($allLines | Select-String -SimpleMatch 'Forwarded agent message' | Where-Object { $_.Line -match $corrPattern } | Select-Object -Last 1).Line
  }
}

$results | ConvertTo-Json -Depth 6
