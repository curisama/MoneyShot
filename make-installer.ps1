# Money Shot 설치 파일 만들기
#
# 앱을 먼저 빌드하고, 그 exe를 리소스로 품은 설치 프로그램을 만든다.
# 흔한 설치파일 제작기들의 자가추출 패커 구조를 피하려고 여기서도 csc.exe만 쓴다.
# 그 구조 자체가 백신 휴리스틱에 걸리기 때문이다(실제 악성이 아니라 전형적인 오탐).
#
# 사용: powershell -ExecutionPolicy Bypass -File make-installer.ps1

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = Join-Path $dir 'Money Shot.exe'
$ico = Join-Path $dir 'money-shot.ico'
$src = Join-Path $dir 'installer'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe 없음: $csc" }

# 1) 앱부터 새로 빌드
'앱 빌드…'
& powershell -ExecutionPolicy Bypass -File (Join-Path $dir 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw '앱 빌드 실패' }
if (-not (Test-Path -LiteralPath $app)) { throw "앱 exe가 없다: $app" }

$ver = (Get-Item -LiteralPath $app).VersionInfo.FileVersion
if (-not $ver) { $ver = '1.0.0.0' }
$short = ($ver -split '\.')[0..1] -join '.'
"앱 버전: $ver"

# 2) 버전을 설치 프로그램 안에 박아 넣는다
$genPath = Join-Path $src 'Info.cs'
$gen = @"
// 이 파일은 make-installer.ps1이 만든다. 직접 고치지 말 것.
namespace MoneyShotSetup
{
    public static class Info
    {
        public const string Version = "$short";
        public const string FullVersion = "$ver";
    }
}
"@
[System.IO.File]::WriteAllText($genPath, $gen, (New-Object System.Text.UTF8Encoding $true))

# 3) 설치 프로그램 빌드 (앱 exe를 리소스로 품는다)
$out = Join-Path $dir "Money Shot $short 설치.exe"
Get-Process -Name 'Money Shot*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $out) { [System.IO.File]::Delete($out) }

$WB = [System.Reflection.Assembly]::LoadWithPartialName('WindowsBase').Location
$PC = [System.Reflection.Assembly]::LoadWithPartialName('PresentationCore').Location
$PF = [System.Reflection.Assembly]::LoadWithPartialName('PresentationFramework').Location
$XA = [System.Reflection.Assembly]::LoadWithPartialName('System.Xaml').Location
# Microsoft.CSharp(바로가기 COM 늦은 바인딩)은 csc 기본 응답파일에 이미 들어 있다

$sources = Get-ChildItem -Path $src -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName }

$cargs = [System.Collections.Generic.List[string]]::new()
'/nologo', '/target:winexe', '/platform:x64', '/optimize+',
"/win32icon:$ico", "/win32manifest:$src\installer.manifest", "/out:$out" | ForEach-Object { $cargs.Add($_) }
$WB, $PC, $PF, $XA | ForEach-Object { $cargs.Add("/reference:$_") }
$cargs.Add("/resource:$app,payload")
$cargs.Add("/resource:$dir\THIRD_PARTY_NOTICES.md,notices")
$sources | ForEach-Object { $cargs.Add($_) }

"설치 프로그램 빌드: $($sources.Count)개 파일"
& $csc @cargs
if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 빌드 실패 (exit $LASTEXITCODE)" }

$kb = [math]::Round((Get-Item -LiteralPath $out).Length / 1KB, 0)
''
"만들었다: $out"
"크기: $kb KB"
''
'배포할 땐 이 파일 하나만 주면 된다.'
'받는 쪽은 관리자 권한 없이 설치할 수 있고, 설정 > 앱 목록에서 제거할 수 있다.'
