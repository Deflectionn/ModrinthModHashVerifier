$ErrorActionPreference = 'Stop'
$base = 'https://github.com/Deflectionn/ModrinthModHashVerifier/releases/latest/download'
$dir = Join-Path $env:TEMP ('modhash-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $exe = Join-Path $dir 'modhash.exe'
    Invoke-WebRequest "$base/modhash.exe" -OutFile $exe -UseBasicParsing

    $raw = (Invoke-WebRequest "$base/modhash.exe.sha256" -UseBasicParsing).Content
    if ($raw -is [byte[]]) { $raw = [Text.Encoding]::ASCII.GetString($raw) }
    $expected = (($raw.ToString().Trim()) -split '\s+')[0].ToLower()
    $actual = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()

    if ($expected -ne $actual) {
        Write-Host 'Hash mismatch. Aborting.' -ForegroundColor Red
        return
    }

    Write-Host "modhash.exe verified, SHA-256 $actual"

    $default = Join-Path $env:APPDATA '.minecraft\mods'
    $folder = Read-Host 'Paste the mods folder path'
    if ([string]::IsNullOrWhiteSpace($folder)) { $folder = $default }
    $folder = $folder.Trim().Trim('"').TrimEnd('\')

    & $exe $folder --no-report
}
finally {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}
