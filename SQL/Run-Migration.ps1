# =====================================================================================
# PowerShell Script: Automatsko izvršavanje SQL migracije
# =====================================================================================
# Ovaj skript automatski izvršava 001_Extend_Staging_Tables.sql na Oracle bazi
#
# Preduslovi:
# - Oracle.ManagedDataAccess.Core NuGet paket instaliran u projektu
# - Ili SQL*Plus instaliran i dostupan u PATH-u
# =====================================================================================

param(
    [Parameter(Mandatory=$false)]
    [string]$ConnectionString = "User Id=APPUSER;Password=appPass;Data Source=localhost:1521/FREEPDB1;",

    [Parameter(Mandatory=$false)]
    [string]$SqlScriptPath = "$PSScriptRoot\001_Extend_Staging_Tables.sql",

    [Parameter(Mandatory=$false)]
    [ValidateSet("SqlPlus", "DotNet")]
    [string]$Method = "SqlPlus"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SQL Migration Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Provera da li fajl postoji
if (-not (Test-Path $SqlScriptPath)) {
    Write-Host "GREŠKA: SQL skripta ne postoji na putanji: $SqlScriptPath" -ForegroundColor Red
    exit 1
}

Write-Host "SQL skripta: $SqlScriptPath" -ForegroundColor Green
Write-Host "Metoda: $Method" -ForegroundColor Green
Write-Host ""

# =====================================================================================
# Metoda 1: SQL*Plus (preporučeno)
# =====================================================================================
if ($Method -eq "SqlPlus") {
    Write-Host "Izvršavanje putem SQL*Plus..." -ForegroundColor Yellow
    Write-Host ""

    # Proveri da li je SQL*Plus instaliran
    $sqlPlusPath = Get-Command sqlplus -ErrorAction SilentlyContinue

    if (-not $sqlPlusPath) {
        Write-Host "GREŠKA: SQL*Plus nije pronađen u PATH-u." -ForegroundColor Red
        Write-Host "Instalirajte Oracle Instant Client ili koristite -Method DotNet" -ForegroundColor Red
        exit 1
    }

    # Ekstrakt username, password, i connection string
    if ($ConnectionString -match "User Id=([^;]+);Password=([^;]+);Data Source=([^;]+)") {
        $username = $matches[1]
        $password = $matches[2]
        $dataSource = $matches[3]

        Write-Host "Povezivanje kao $username@$dataSource..." -ForegroundColor Cyan

        # Izvršavanje SQL skripte
        $sqlPlusCommand = "@`"$SqlScriptPath`""

        # Kreiranje privremenog SQL fajla sa komandama
        $tempSqlFile = [System.IO.Path]::GetTempFileName() + ".sql"
        @"
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET ECHO ON
SET FEEDBACK ON
SET SERVEROUTPUT ON
SPOOL C:\temp\migration_output.log

$sqlPlusCommand

SPOOL OFF
EXIT;
"@ | Out-File -FilePath $tempSqlFile -Encoding ASCII

        try {
            # Izvršavanje SQL*Plus
            $process = Start-Process -FilePath "sqlplus" `
                -ArgumentList "$username/$password@$dataSource", "@`"$tempSqlFile`"" `
                -NoNewWindow -Wait -PassThru

            if ($process.ExitCode -eq 0) {
                Write-Host ""
                Write-Host "========================================" -ForegroundColor Green
                Write-Host "✓ SQL migracija uspešno izvršena!" -ForegroundColor Green
                Write-Host "========================================" -ForegroundColor Green
                Write-Host ""
                Write-Host "Log fajl: C:\temp\migration_output.log" -ForegroundColor Cyan

                if (Test-Path "C:\temp\migration_output.log") {
                    Write-Host ""
                    Write-Host "Poslednje linije iz log-a:" -ForegroundColor Cyan
                    Get-Content "C:\temp\migration_output.log" | Select-Object -Last 20 | ForEach-Object {
                        Write-Host $_
                    }
                }
            } else {
                Write-Host ""
                Write-Host "========================================" -ForegroundColor Red
                Write-Host "✗ SQL migracija neuspešna!" -ForegroundColor Red
                Write-Host "========================================" -ForegroundColor Red
                Write-Host "Exit Code: $($process.ExitCode)" -ForegroundColor Red
                Write-Host ""

                if (Test-Path "C:\temp\migration_output.log") {
                    Write-Host "Greška iz log-a:" -ForegroundColor Red
                    Get-Content "C:\temp\migration_output.log" | Select-Object -Last 30 | ForEach-Object {
                        Write-Host $_ -ForegroundColor Red
                    }
                }

                exit $process.ExitCode
            }
        }
        finally {
            # Obriši privremeni fajl
            if (Test-Path $tempSqlFile) {
                Remove-Item $tempSqlFile -Force
            }
        }
    } else {
        Write-Host "GREŠKA: Neispravan format connection string-a" -ForegroundColor Red
        exit 1
    }
}

# =====================================================================================
# Metoda 2: .NET Oracle Client (alternativa)
# =====================================================================================
elseif ($Method -eq "DotNet") {
    Write-Host "Izvršavanje putem .NET Oracle.ManagedDataAccess..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "NAPOMENA: Ova metoda zahteva Oracle.ManagedDataAccess.Core NuGet paket" -ForegroundColor Yellow
    Write-Host ""

    # Proveri da li je Oracle.ManagedDataAccess dostupan
    $projectPath = (Get-Item $PSScriptRoot).Parent.FullName
    $dllPath = Get-ChildItem -Path $projectPath -Recurse -Filter "Oracle.ManagedDataAccess.dll" -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $dllPath) {
        Write-Host "GREŠKA: Oracle.ManagedDataAccess.dll nije pronađen." -ForegroundColor Red
        Write-Host "Instalirajte NuGet paket: Install-Package Oracle.ManagedDataAccess.Core" -ForegroundColor Red
        exit 1
    }

    Write-Host "Učitavanje Oracle.ManagedDataAccess iz: $($dllPath.FullName)" -ForegroundColor Cyan
    Add-Type -Path $dllPath.FullName

    # Učitaj SQL skriptu
    $sqlScript = Get-Content -Path $SqlScriptPath -Raw

    # Split na statement-e (pojednostavljeno - ne rešava sve edge case-ove)
    $statements = $sqlScript -split ";" | Where-Object { $_.Trim() -ne "" }

    Write-Host "Broj SQL statement-a: $($statements.Count)" -ForegroundColor Cyan
    Write-Host "Povezivanje na bazu..." -ForegroundColor Cyan

    try {
        $connection = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($ConnectionString)
        $connection.Open()

        Write-Host "✓ Povezivanje uspešno!" -ForegroundColor Green
        Write-Host ""

        $successCount = 0
        $errorCount = 0

        foreach ($statement in $statements) {
            $trimmed = $statement.Trim()

            # Preskoči komentare i PROMPT naredbe
            if ($trimmed.StartsWith("--") -or
                $trimmed.StartsWith("/*") -or
                $trimmed.StartsWith("PROMPT") -or
                $trimmed.Length -eq 0) {
                continue
            }

            try {
                $command = $connection.CreateCommand()
                $command.CommandText = $trimmed
                $command.CommandTimeout = 300

                Write-Host "Izvršavanje: $($trimmed.Substring(0, [Math]::Min(60, $trimmed.Length)))..." -ForegroundColor Gray

                $result = $command.ExecuteNonQuery()
                $successCount++

                Write-Host "  ✓ Uspešno" -ForegroundColor Green
            }
            catch {
                $errorCount++
                Write-Host "  ✗ Greška: $($_.Exception.Message)" -ForegroundColor Red
            }
            finally {
                if ($command) { $command.Dispose() }
            }
        }

        Write-Host ""
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "Rezultati:" -ForegroundColor Cyan
        Write-Host "  Uspešno: $successCount" -ForegroundColor Green
        Write-Host "  Greške: $errorCount" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
        Write-Host "========================================" -ForegroundColor Cyan

        if ($errorCount -eq 0) {
            Write-Host ""
            Write-Host "✓ SQL migracija uspešno završena!" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "✗ SQL migracija završena sa greškama!" -ForegroundColor Red
            exit 1
        }
    }
    catch {
        Write-Host ""
        Write-Host "GREŠKA prilikom povezivanja na bazu: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    finally {
        if ($connection) {
            $connection.Close()
            $connection.Dispose()
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Sledeći koraci:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "1. Proverite da su kolone dodate:" -ForegroundColor White
Write-Host "   SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'DOC_STAGING';" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Proverite indekse:" -ForegroundColor White
Write-Host "   SELECT INDEX_NAME FROM USER_INDEXES WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING');" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Pogledajte INTEGRATION_INSTRUCTIONS.md za sledeće korake" -ForegroundColor White
Write-Host ""

# Pitaj korisnika da li želi da verifikuje izmene
$verify = Read-Host "Da li želite da verifikujete izmene? (y/n)"
if ($verify -eq "y" -or $verify -eq "Y") {
    Write-Host ""
    Write-Host "Izvršavanje verifikacionih upita..." -ForegroundColor Yellow

    if ($Method -eq "SqlPlus" -and $ConnectionString -match "User Id=([^;]+);Password=([^;]+);Data Source=([^;]+)") {
        $username = $matches[1]
        $password = $matches[2]
        $dataSource = $matches[3]

        $verifyScript = @"
SET PAGESIZE 100
SET LINESIZE 200

PROMPT
PROMPT Nove kolone u DOC_STAGING:
SELECT COUNT(*) AS DOC_STAGING_NEW_COLUMNS
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'DOC_STAGING'
  AND COLUMN_NAME IN (
      'DOCUMENT_TYPE', 'DOCUMENT_TYPE_MIGRATION', 'SOURCE', 'IS_ACTIVE',
      'CATEGORY_CODE', 'CATEGORY_NAME', 'ORIGINAL_CREATED_AT',
      'CONTRACT_NUMBER', 'CORE_ID', 'VERSION', 'ACCOUNT_NUMBERS',
      'REQUIRES_TYPE_TRANSFORMATION', 'FINAL_DOCUMENT_TYPE',
      'IS_SIGNED', 'DUT_OFFER_ID', 'PRODUCT_TYPE'
  );

PROMPT
PROMPT Nove kolone u FOLDER_STAGING:
SELECT COUNT(*) AS FOLDER_STAGING_NEW_COLUMNS
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'FOLDER_STAGING'
  AND COLUMN_NAME IN (
      'CLIENT_TYPE', 'CORE_ID', 'CLIENT_NAME', 'MBR_JMBG',
      'PRODUCT_TYPE', 'CONTRACT_NUMBER', 'BATCH', 'SOURCE',
      'UNIQUE_IDENTIFIER', 'PROCESS_DATE', 'RESIDENCY', 'SEGMENT',
      'CLIENT_SUBTYPE', 'STAFF', 'OPU_USER', 'OPU_REALIZATION',
      'BARCLEX', 'COLLABORATOR', 'CREATOR', 'ARCHIVED_AT'
  );

PROMPT
PROMPT Indeksi:
SELECT INDEX_NAME, TABLE_NAME
FROM USER_INDEXES
WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING')
  AND INDEX_NAME LIKE 'IDX_%'
ORDER BY TABLE_NAME, INDEX_NAME;

EXIT;
"@

        $tempVerifyFile = [System.IO.Path]::GetTempFileName() + ".sql"
        $verifyScript | Out-File -FilePath $tempVerifyFile -Encoding ASCII

        try {
            $process = Start-Process -FilePath "sqlplus" `
                -ArgumentList "$username/$password@$dataSource", "@`"$tempVerifyFile`"" `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$PSScriptRoot\verify_output.txt"

            if (Test-Path "$PSScriptRoot\verify_output.txt") {
                Get-Content "$PSScriptRoot\verify_output.txt" | ForEach-Object {
                    Write-Host $_
                }
                Remove-Item "$PSScriptRoot\verify_output.txt" -Force
            }
        }
        finally {
            if (Test-Path $tempVerifyFile) {
                Remove-Item $tempVerifyFile -Force
            }
        }
    }
}

Write-Host ""
Write-Host "Gotovo!" -ForegroundColor Green
