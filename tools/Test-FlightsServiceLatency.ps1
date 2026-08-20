<#
.SYNOPSIS
    Misura il tempo di risposta di FlightsService.GetFlights (via grpcurl) al variare
    del limit, per capire se conviene affettare in fette da 100, 75, 50 (o altro).

.DESCRIPTION
    Per ciascun valore di -Limits, calcola quante chiamate GetFlights servono per
    coprire -TotalFlights voli (default 1000, coerente col mock di FlightsService),
    le lancia come processi grpcurl indipendenti - in parallelo di default, oppure
    una alla volta con -Sequential - e misura:
      - il tempo di ciascuna singola chiamata (media/min/max)
      - il tempo totale dello sweep (dall'avvio della prima chiamata al termine
        dell'ultima) - e' questo il numero che conta per la tua GetSearchAsync reale,
        perche' e' quanto aspetta l'utente prima di avere tutti i dati.

    Se una o piu' chiamate falliscono, lo script mostra il messaggio di errore della
    prima chiamata fallita per ciascun limit, cosi' non devi rilanciarla a mano per
    capire cosa e' successo.

.PARAMETER Target
    Indirizzo:porta di FlightsService (default: la porta host di default del
    docker-compose, 8082).

.PARAMETER TotalFlights
    Numero totale di voli nel mock (default 1000, vedi FlightsServiceImpl.cs).

.PARAMETER Limits
    Elenco dei valori di limit da confrontare (default 100, 75, 50). Ricorda che
    FlightsService li limita comunque a un massimo di 100 lato server.

.PARAMETER Sequential
    Se presente, le chiamate di ogni sweep vengono eseguite una alla volta invece che
    in parallelo - utile per vedere quanto guadagni davvero parallelizzando.

.EXAMPLE
    .\Test-FlightsServiceLatency.ps1

.EXAMPLE
    .\Test-FlightsServiceLatency.ps1 -Limits 100,50,25,10 -Sequential
#>

[CmdletBinding()]
param(
    [string]$Target = "localhost:8082",
    [int]$TotalFlights = 1000,
    [int[]]$Limits = @(100, 75, 50),
    [string]$GrpcurlPath = "grpcurl",
    [switch]$Sequential
)

# Verifica che grpcurl sia raggiungibile prima di partire.
if (-not (Get-Command $GrpcurlPath -ErrorAction SilentlyContinue)) {
    Write-Error "grpcurl non trovato (cercato: '$GrpcurlPath'). Installalo con: go install github.com/fullstorydev/grpcurl/cmd/grpcurl@latest, oppure passa -GrpcurlPath con il percorso completo."
    return
}

function Invoke-FlightsSweep {
    param(
        [int]$Limit,
        [int]$TotalFlights,
        [string]$Target,
        [string]$GrpcurlPath,
        [bool]$Sequential
    )

    $callCount = [math]::Ceiling($TotalFlights / $Limit)
    $offsets = 0..($callCount - 1) | ForEach-Object { $_ * $Limit }

    $calls = @()
    $swTotal = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($offset in $offsets) {
        # Argomenti come singola stringa (proprieta' "Arguments"), NON ArgumentList:
        # su Windows PowerShell 5.1, ProcessStartInfo.ArgumentList risulta null invece
        # di una collezione vuota, e ogni .Add() fallisce silenziosamente - il processo
        # parte senza argomenti. "Arguments" invece esiste da sempre e funziona sia su
        # PowerShell 5.1 che 7. Il corpo JSON va tra virgolette, con le virgolette
        # interne "escapate" con backslash secondo le regole standard della riga di
        # comando di Windows.
        $jsonBody = "{\`"offset\`": $offset, \`"limit\`": $Limit}"

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $GrpcurlPath
        $psi.Arguments = "-plaintext -d `"$jsonBody`" $Target flights.Flights/GetFlights"
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $proc = [System.Diagnostics.Process]::Start($psi)

        # Lettura asincrona di stdout/stderr avviata subito: evita che un processo si
        # blocchi scrivendo su un buffer pieno mentre nessuno lo legge (rischio di
        # deadlock con risposte grandi), e ci permette di recuperare il messaggio di
        # errore quando una chiamata fallisce.
        $stdOutTask = $proc.StandardOutput.ReadToEndAsync()
        $stdErrTask = $proc.StandardError.ReadToEndAsync()

        if ($Sequential) {
            $proc.WaitForExit()
            $sw.Stop()
        }

        $calls += [PSCustomObject]@{
            Offset      = $offset
            Process     = $proc
            Stopwatch   = $sw
            StdOutTask  = $stdOutTask
            StdErrTask  = $stdErrTask
        }
    }

    # In modalita' parallela le chiamate sono gia' tutte partite: qui aspettiamo che
    # finiscano tutte e fermiamo il rispettivo cronometro individuale.
    foreach ($call in $calls) {
        if (-not $Sequential) {
            $call.Process.WaitForExit()
            $call.Stopwatch.Stop()
        }
    }

    $results = $calls | ForEach-Object {
        [PSCustomObject]@{
            Offset   = $_.Offset
            Ms       = $_.Stopwatch.Elapsed.TotalMilliseconds
            ExitCode = $_.Process.ExitCode
            StdErr   = $_.StdErrTask.Result
        }
    }

    $swTotal.Stop()

    $avg = ($results | Measure-Object Ms -Average).Average
    $min = ($results | Measure-Object Ms -Minimum).Minimum
    $max = ($results | Measure-Object Ms -Maximum).Maximum
    $failed = $results | Where-Object { $_.ExitCode -ne 0 }

    if ($failed.Count -gt 0) {
        $firstError = ($failed | Select-Object -First 1).StdErr.Trim()
        Write-Host "  -> $($failed.Count)/$($results.Count) chiamate fallite (limit=$Limit). Primo errore:" -ForegroundColor Yellow
        Write-Host "     $firstError" -ForegroundColor Yellow
    }

    [PSCustomObject]@{
        Limit          = $Limit
        Chiamate       = $callCount
        MediaMs        = [math]::Round($avg, 1)
        MinMs          = [math]::Round($min, 1)
        MaxMs          = [math]::Round($max, 1)
        TempoTotaleMs  = [math]::Round($swTotal.Elapsed.TotalMilliseconds, 1)
        Fallite        = $failed.Count
    }
}

$modo = if ($Sequential) { "sequenziale" } else { "parallela" }
Write-Host "Target: $Target - voli totali: $TotalFlights - modalita': $modo`n" -ForegroundColor Cyan

$summary = foreach ($limit in $Limits) {
    Write-Host "Sweep con limit=$limit..." -ForegroundColor Cyan
    Invoke-FlightsSweep -Limit $limit -TotalFlights $TotalFlights -Target $Target -GrpcurlPath $GrpcurlPath -Sequential:$Sequential.IsPresent
}

Write-Host ""
$summary | Format-Table -AutoSize

if (($summary | Measure-Object Fallite -Sum).Sum -gt 0) {
    Write-Host "ATTENZIONE: una o piu' chiamate sono fallite. I tempi sopra NON sono attendibili come misura di GetFlights finche' 'Fallite' non e' 0 su ogni riga - guarda i messaggi di errore stampati durante l'esecuzione." -ForegroundColor Red
}

Write-Host "MediaMs/MinMs/MaxMs = durata della singola chiamata GetFlights." -ForegroundColor DarkGray
Write-Host "TempoTotaleMs = quanto impiega l'intero sweep (dall'avvio della prima chiamata alla fine dell'ultima) - e' il numero che conta per il caso reale." -ForegroundColor DarkGray
