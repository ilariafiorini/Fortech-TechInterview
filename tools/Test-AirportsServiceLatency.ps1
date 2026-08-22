<#
.SYNOPSIS
    Misura il tempo di risposta di AirportsService.GetAirports (REST) al variare del
    limit, per capire se conviene affettare in fette da 100, 75, 50 (o altro) quando si
    scaricano tutti i 300 aeroporti.

.DESCRIPTION
    Stesso spirito e stessa metodologia di tools/Test-FlightsServiceLatency.ps1, ma qui
    non serve un tool esterno come grpcurl: AirportsService e' REST, quindi le chiamate
    vengono fatte direttamente con System.Net.Http.HttpClient (un'unica istanza
    condivisa e riusata per tutte le chiamate, come si farebbe nel vero SearchAsync).
    Niente processi esterni, niente escaping di riga di comando: molto piu' semplice
    dello script per FlightsService.

    Per ciascun valore di -Limits, calcola quante chiamate GetAirports servono per
    coprire -TotalAirports aeroporti (default 300, coerente col mock di
    AirportsService), le lancia in parallelo di default (oppure una alla volta con
    -Sequential) e misura:
      - il tempo di ciascuna singola chiamata (media/min/max)
      - il tempo totale dello sweep (dall'avvio della prima chiamata al termine
        dell'ultima) - e' questo il numero che conta per il seeding di "" nella cache
        Airports, perche' e' quanto si aspetta prima di avere il superset completo.

    A differenza di FlightsService, AirportsRepository e' singleton: i 300 aeroporti
    sono generati una sola volta all'avvio del container, non ad ogni chiamata. Quindi
    non ci si aspetta la stessa instabilita' a limit molto basso vista nel benchmark di
    FlightsService (li' causata dalla rigenerazione concorrente di 1000 voli per
    chiamata) - questo script serve proprio a confermarlo (o smentirlo) con dati veri
    sul tuo ambiente, invece che per deduzione.

    Ogni chiamata viene anche validata nel contenuto (non solo nel codice di stato
    HTTP): si controlla che la risposta contenga il numero di elementi atteso e che
    totalCount corrisponda a -TotalAirports, cosi' un problema non passa inosservato
    come "veloce ma silenziosamente fallito".

.PARAMETER BaseUrl
    Indirizzo:porta di AirportsService (default: la porta host di default del
    docker-compose, 8081).

.PARAMETER TotalAirports
    Numero totale di aeroporti nel mock (default 300, vedi AirportsRepository.cs).

.PARAMETER Limits
    Elenco dei valori di limit da confrontare (default 100, 75, 50, 25, 10). Ricorda
    che AirportsService li limita comunque a un massimo di 100 lato server.

.PARAMETER Sequential
    Se presente, le chiamate di ogni sweep vengono eseguite una alla volta invece che
    in parallelo - utile per vedere quanto guadagni davvero parallelizzando.

.EXAMPLE
    .\Test-AirportsServiceLatency.ps1

.EXAMPLE
    .\Test-AirportsServiceLatency.ps1 -Limits 100,50,25,10 -Sequential
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8081",
    [int]$TotalAirports = 300,
    [int[]]$Limits = @(100, 75, 50, 25, 10),
    [switch]$Sequential
)

Add-Type -AssemblyName System.Net.Http

$httpClient = New-Object System.Net.Http.HttpClient
$httpClient.Timeout = [TimeSpan]::FromSeconds(30)

function Invoke-AirportsSweep {
    param(
        [int]$Limit,
        [int]$TotalAirports,
        [string]$BaseUrl,
        [System.Net.Http.HttpClient]$Client,
        [bool]$Sequential
    )

    $callCount = [math]::Ceiling($TotalAirports / $Limit)
    $offsets = 0..($callCount - 1) | ForEach-Object { $_ * $Limit }

    $calls = @()
    $swTotal = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($offset in $offsets) {
        $expected = [math]::Min($Limit, $TotalAirports - $offset)
        $url = "$BaseUrl/api/airports?offset=$offset&limit=$Limit"

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $task = $Client.GetAsync($url)

        if ($Sequential) {
            $null = $task.GetAwaiter().GetResult()
            $sw.Stop()
        }

        $calls += [PSCustomObject]@{
            Offset   = $offset
            Expected = $expected
            Task     = $task
            Stopwatch = $sw
        }
    }

    # In modalita' parallela le chiamate sono gia' tutte partite: qui aspettiamo che
    # finiscano tutte, una alla volta - GetAwaiter().GetResult() su un task gia'
    # completato ritorna subito, quindi il cronometro individuale resta accurato anche
    # scorrendole in sequenza.
    $results = foreach ($call in $calls) {
        $errorMessage = $null
        $statusCode = $null
        $actualCount = $null
        $totalCount = $null

        try {
            if (-not $Sequential) {
                $response = $call.Task.GetAwaiter().GetResult()
                $call.Stopwatch.Stop()
            } else {
                $response = $call.Task.GetAwaiter().GetResult()
            }

            $statusCode = [int]$response.StatusCode

            if ($response.IsSuccessStatusCode) {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $json = $body | ConvertFrom-Json
                $actualCount = @($json.items).Count
                $totalCount = $json.totalCount

                if ($actualCount -ne $call.Expected) {
                    $errorMessage = "attesi $($call.Expected) elementi, ricevuti $actualCount"
                } elseif ($totalCount -ne $TotalAirports) {
                    $errorMessage = "totalCount=$totalCount, atteso $TotalAirports"
                }
            } else {
                $errorMessage = "HTTP $statusCode"
            }
        } catch {
            if (-not $Sequential) { $call.Stopwatch.Stop() }
            $errorMessage = $_.Exception.Message
        }

        [PSCustomObject]@{
            Offset = $call.Offset
            Ms     = $call.Stopwatch.Elapsed.TotalMilliseconds
            Failed = [bool]$errorMessage
            Error  = $errorMessage
        }
    }

    $swTotal.Stop()

    $avg = ($results | Measure-Object Ms -Average).Average
    $min = ($results | Measure-Object Ms -Minimum).Minimum
    $max = ($results | Measure-Object Ms -Maximum).Maximum
    $failed = $results | Where-Object { $_.Failed }

    if ($failed.Count -gt 0) {
        $firstError = ($failed | Select-Object -First 1).Error
        Write-Host "  -> $($failed.Count)/$($results.Count) chiamate fallite (limit=$Limit). Primo errore:" -ForegroundColor Yellow
        Write-Host "     $firstError" -ForegroundColor Yellow
    }

    [PSCustomObject]@{
        Limit         = $Limit
        Chiamate      = $callCount
        MediaMs       = [math]::Round($avg, 1)
        MinMs         = [math]::Round($min, 1)
        MaxMs         = [math]::Round($max, 1)
        TempoTotaleMs = [math]::Round($swTotal.Elapsed.TotalMilliseconds, 1)
        Fallite       = $failed.Count
    }
}

$modo = if ($Sequential) { "sequenziale" } else { "parallela" }
Write-Host "Target: $BaseUrl - aeroporti totali: $TotalAirports - modalita': $modo`n" -ForegroundColor Cyan

$summary = foreach ($limit in $Limits) {
    Write-Host "Sweep con limit=$limit..." -ForegroundColor Cyan
    Invoke-AirportsSweep -Limit $limit -TotalAirports $TotalAirports -BaseUrl $BaseUrl -Client $httpClient -Sequential:$Sequential.IsPresent
}

Write-Host ""
$summary | Format-Table -AutoSize

if (($summary | Measure-Object Fallite -Sum).Sum -gt 0) {
    Write-Host "ATTENZIONE: una o piu' chiamate sono fallite o hanno dati inattesi. I tempi sopra NON sono attendibili come misura di GetAirports finche' 'Fallite' non e' 0 su ogni riga - guarda i messaggi di errore stampati durante l'esecuzione." -ForegroundColor Red
}

Write-Host "MediaMs/MinMs/MaxMs = durata della singola chiamata GetAirports." -ForegroundColor DarkGray
Write-Host "TempoTotaleMs = quanto impiega l'intero sweep (dall'avvio della prima chiamata alla fine dell'ultima) - e' il numero che conta per il seeding della cache Airports." -ForegroundColor DarkGray

$httpClient.Dispose()
