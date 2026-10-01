# Money Shot 빌드
# 윈도우에 기본 내장된 C# 컴파일러(csc.exe)만 쓴다. Visual Studio도 SDK도 필요 없다.
# IPtendo Switch에서 ps2exe가 알약에 오탐으로 잡혔던 전례가 있어, 패커를 거치지 않고
# 평범한 관리형 어셈블리를 바로 만든다.
#
# 사용: powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $dir 'Money Shot.exe'
$ico = Join-Path $dir 'money-shot.ico'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe 없음: $csc (.NET Framework 4.x 필요)" }

if (-not (Test-Path $ico)) {
    "아이콘이 없어 먼저 만든다…"
    & powershell -ExecutionPolicy Bypass -File (Join-Path $dir 'make-icon.ps1')
}

# 실행 중이면 내린다
Get-Process -Name 'Money Shot' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
if (Test-Path -LiteralPath $out) { [System.IO.File]::Delete($out) }

# WPF 어셈블리는 GAC에 있고 경로가 환경마다 달라 런타임에 해석한다.
$WB = [System.Reflection.Assembly]::LoadWithPartialName('WindowsBase').Location
$PC = [System.Reflection.Assembly]::LoadWithPartialName('PresentationCore').Location
$PF = [System.Reflection.Assembly]::LoadWithPartialName('PresentationFramework').Location
$XA = [System.Reflection.Assembly]::LoadWithPartialName('System.Xaml').Location
# 누끼 런타임을 누겟 패키지(zip)에서 꺼내는 데 쓴다
$ZC = [System.Reflection.Assembly]::LoadWithPartialName('System.IO.Compression').Location
$ZF = [System.Reflection.Assembly]::LoadWithPartialName('System.IO.Compression.FileSystem').Location
# System / System.Core / System.Drawing / System.Windows.Forms / System.Web.Extensions 는
# csc 기본 응답파일에 이미 들어 있다. 명시하면 CS1703(중복 참조)이 난다.

$sources = Get-ChildItem -Path $dir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw '컴파일할 .cs 파일이 없다' }

$cargs = [System.Collections.Generic.List[string]]::new()
'/nologo', '/target:winexe', '/platform:x64', '/unsafe+', '/optimize+',
"/win32icon:$ico", "/win32manifest:$dir\app.manifest", "/out:$out" | ForEach-Object { $cargs.Add($_) }
$WB, $PC, $PF, $XA, $ZC, $ZF | ForEach-Object { $cargs.Add("/reference:$_") }
# 오픈소스 고지를 실행 파일 안에 넣는다 — 설정 › 오픈소스 고지가 이걸 보여준다(MIT는 배포본마다 고지가 있어야 한다)
$cargs.Add("/resource:$dir\THIRD_PARTY_NOTICES.md,notices")
$sources | ForEach-Object { $cargs.Add($_) }

"컴파일: $($sources.Count)개 파일"
& $csc @cargs
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }

if (Test-Path -LiteralPath $out) {
    "빌드 성공: $out  ($([math]::Round((Get-Item $out).Length/1KB,0)) KB)"
} else {
    throw '빌드 실패: 출력 파일 없음'
}
