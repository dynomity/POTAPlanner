$ErrorActionPreference = 'Stop'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php, then run this script again.'
}

& $compiler (Join-Path $scriptDirectory 'POTAPlanner.iss')

