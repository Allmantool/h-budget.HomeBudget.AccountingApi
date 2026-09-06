[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AccountingBaseUrl,

    [Parameter(Mandatory)]
    [string]$GatewayBaseUrl,

    [string]$RunId = ("release-acceptance-" + [DateTime]::UtcNow.ToString("yyyyMMddHHmmss") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)),

    [switch]$SkipGatewayCertificateValidation,

    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$accountingUrl = $AccountingBaseUrl.TrimEnd('/')
$gatewayUrl = $GatewayBaseUrl.TrimEnd('/')

function Invoke-AccountingApi {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [hashtable]$Headers = @{}
    )

    $request = @{
        Uri = "$accountingUrl/$Path"
        Method = $Method
        Headers = $Headers
        ContentType = 'application/json'
    }

    if ($null -ne $Body) {
        $request.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }

    $response = Invoke-RestMethod @request
    if (-not $response.isSucceeded) {
        throw "${Method} /${Path} failed: $($response.statusMessage)"
    }

    return $response.payload
}

function Find-OrCreateHandbook {
    param(
        [Parameter(Mandatory)][string]$Endpoint,
        [Parameter(Mandatory)][scriptblock]$Predicate,
        [Parameter(Mandatory)][object]$CreateBody
    )

    $existing = Invoke-AccountingApi -Method 'GET' -Path $Endpoint
    $existingMatch = @($existing | Where-Object $Predicate | Select-Object -First 1)
    if ($existingMatch.Count -eq 1) {
        return $existingMatch[0].key
    }

    return Invoke-AccountingApi -Method 'POST' -Path $Endpoint -Body $CreateBody
}

function Find-OrCreateAccount {
    param(
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][decimal]$InitialBalance,
        [Parameter(Mandatory)][int]$AccountType
    )

    $existing = Invoke-AccountingApi -Method 'GET' -Path 'payment-accounts'
    $account = @($existing | Where-Object { $_.description -eq $Description } | Select-Object -First 1)
    if ($account.Count -eq 1) {
        return $account[0].key
    }

    return Invoke-AccountingApi -Method 'POST' -Path 'payment-accounts' -Body @{
        agent = 'Priorbank'
        initialBalance = $InitialBalance
        currency = 'USD'
        description = $Description
        accountType = $AccountType
    }
}

$accountA = Find-OrCreateAccount -Description "Acceptance Priorbank $RunId" -InitialBalance 45 -AccountType 3
$accountB = Find-OrCreateAccount -Description "Acceptance Secondary Account $RunId" -InitialBalance 0 -AccountType 0

$categoryId = Find-OrCreateHandbook -Endpoint 'categories' -Predicate {
    $_.categoryType -eq 1 -and (@($_.nameNodes) -join '|') -eq 'category|two|expense'
} -CreateBody @{
    categoryType = 1
    nameNodes = @('category', 'two', 'expense')
}

$contractorId = Find-OrCreateHandbook -Endpoint 'contractors' -Predicate {
    (@($_.nameNodes) -join '|') -eq 'Parties|one'
} -CreateBody @{
    nameNodes = @('Parties', 'one')
}

$idempotencyKey = "release-acceptance-payment-$RunId"
$payment = Invoke-AccountingApi -Method 'POST' -Path "payment-operations/$accountA" -Headers @{
    'Idempotency-Key' = $idempotencyKey
} -Body @{
    amount = 23
    comment = 'XXX'
    categoryId = "$categoryId"
    contractorId = "$contractorId"
    operationDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
}

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$latestStatus = $null
do {
    $latestStatus = Invoke-AccountingApi -Method 'GET' -Path "payment-operations/$accountA/commands/$($payment.commandId)"
    if ($latestStatus.status -eq 'Projected') {
        break
    }

    Start-Sleep -Milliseconds 250
} while ([DateTime]::UtcNow -lt $deadline)

if ($latestStatus.status -ne 'Projected') {
    throw "Payment command projection timed out. commandId=$($payment.commandId); status=$($latestStatus.status); operationId=$($payment.paymentOperationId); accountId=$accountA"
}

$gatewayRequest = @{
    Uri = "$gatewayUrl/accounting/payments-history/$accountA"
    Method = 'GET'
}
if ($SkipGatewayCertificateValidation) {
    $gatewayRequest.SkipCertificateCheck = $true
}

$historyResponse = Invoke-RestMethod @gatewayRequest
if (-not $historyResponse.isSucceeded) {
    throw "Gateway history read failed: $($historyResponse.statusMessage)"
}

$historyRecord = @($historyResponse.payload | Where-Object { "$($_.record.key)" -eq "$($payment.paymentOperationId)" } | Select-Object -First 1)
if ($historyRecord.Count -ne 1) {
    throw "Projected payment '$($payment.paymentOperationId)' was not returned by gateway history for account '$accountA'."
}

$record = $historyRecord[0]
if ($record.record.amount -ne 23 -or
    "$($record.record.categoryId)" -ne "$categoryId" -or
    "$($record.record.contractorId)" -ne "$contractorId" -or
    $record.record.comment -ne 'XXX' -or
    $record.balance -ne 22) {
    throw "Gateway history does not match the acceptance invariant: $($record | ConvertTo-Json -Depth 8 -Compress)"
}

[pscustomobject]@{
    runId = $RunId
    accountingBaseUrl = $accountingUrl
    gatewayBaseUrl = $gatewayUrl
    accountAId = "$accountA"
    accountBId = "$accountB"
    categoryId = "$categoryId"
    contractorId = "$contractorId"
    paymentOperationId = "$($payment.paymentOperationId)"
    commandId = "$($payment.commandId)"
    idempotencyKey = $idempotencyKey
    commandStatus = "$($latestStatus.status)"
    initialBalance = 45
    paymentMagnitude = 23
    finalBalance = $record.balance
    historyRecord = $record
} | ConvertTo-Json -Depth 8
