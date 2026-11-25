<#
.SYNOPSIS
    MoeQuick NetScan 构建与红线校验脚本。

.DESCRIPTION
    按 doc/project-structure.md §11 实现，包含：
      1. 参数校验
      2. 禁用 API 扫描（async / await / Task / HttpClient / BlockingCollection）
      3. MSBuild 编译（强制 net40 引用程序集，双轨解析）
      4. 产物校验（输出目录只允许规定文件）
      5. 引用程序集白名单校验
      6. Release 不得含 .pdb
      7. 图标校验（只校验有无，不比对内容）
      8. 可选：复制文档

    双轨 net40 引用程序集解析（A+B 双轨）：
      轨道1 = 系统官方 Targeting Pack
      轨道2 = 项目内 .toolchain/net40（首次运行自动从 NuGet 下载）
    轨道1 命中则优先使用（B 能装就用 B），否则回退轨道2（A 保底）。

.PARAMETER Configuration
    构建配置：Debug | Test | Release。默认 Debug。

.PARAMETER WithDocs
    同时把 doc/ 下的四份文档复制到输出目录。

.PARAMETER SkipDisabledApiScan
    跳过禁用 API 扫描。仅供排查构建脚本自身问题，日常不得使用。

.EXAMPLE
    pwsh -File build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Test', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$WithDocs,

    [switch]$PackageZip,

    [switch]$SkipDisabledApiScan
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# 常量
# ---------------------------------------------------------------------------
$Script:Root          = $PSScriptRoot
$Script:SrcDir        = Join-Path $Root 'src'
$Script:SlnPath       = Join-Path $Root 'MQNetScan.sln'
$Script:ProjPath      = Join-Path $Root 'src\MQNetScan\MQNetScan.csproj'
$Script:TestsProjPath = Join-Path $Root 'tests\MQNetScan.Tests\MQNetScan.Tests.csproj'
$Script:OutDir        = Join-Path $Root "build\$Configuration"
$Script:ToolchainDir  = Join-Path $Root '.toolchain'
$Script:LocalRefDir   = Join-Path $ToolchainDir 'net40\build\.NETFramework\v4.0'

$Script:RefAssemblyPackUrl = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net40/1.0.3/microsoft.netframework.referenceassemblies.net40.1.0.3.nupkg'

# 允许出现在最终 exe 引用表中的程序集（project-structure.md §11）
$Script:AllowedReferences = @(
    'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Runtime.Serialization', 'System.Windows.Forms', 'System.Xml'
)

# 禁用 API 规则：错误信息 -> 正则（按 .cs 文件逐行扫描）
$Script:BannedApis = [ordered]@{
    'async 关键字（C# 5）'          = '\basync\s'
    'await 关键字（C# 5）'          = '\bawait\s'
    'Task.Run（.NET 4.5）'          = '\bTask\s*\.\s*Run\b'
    'Task 泛型（.NET 4.5）'         = '\bTask\s*<'
    'HttpClient（.NET 4.5）'        = '\bHttpClient\b'
    'BlockingCollection（并发集合）' = '\bBlockingCollection\b'
    'WebClient/HttpWebRequest'      = '\b(WebClient|HttpWebRequest)\b'
}

$Script:Warnings = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------
# 辅助输出
# ---------------------------------------------------------------------------
function Write-Step  { param([string]$m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok    { param([string]$m) Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host "  [WARN] $m" -ForegroundColor Yellow; $Script:Warnings.Add($m) }
function Write-Err   { param([string]$m) Write-Host "  [FAIL] $m" -ForegroundColor Red }
function Write-Info  { param([string]$m) Write-Host "  $m" -ForegroundColor Gray }

# ---------------------------------------------------------------------------
# 轨道解析：net40 引用程序集
# ---------------------------------------------------------------------------
function Resolve-Net40ReferencePath {
    # 轨道 1：系统官方 Targeting Pack
    $systemCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0'),
        (Join-Path $env:ProgramFiles       'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0')
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($c in $systemCandidates) {
        if (Test-Path (Join-Path $c 'mscorlib.dll')) {
            return [pscustomobject]@{ Mode = 'System'; Path = $c }
        }
    }

    # 轨道 2：项目内 .toolchain
    if (Test-Path (Join-Path $LocalRefDir 'mscorlib.dll')) {
        return [pscustomobject]@{ Mode = 'Local'; Path = $LocalRefDir }
    }

    return $null
}

function Install-LocalReferencePack {
    param([string]$Destination)

    Write-Info "本地引用程序集缺失，开始从 NuGet 下载（约 29 MB）..."
    Write-Info "来源：$Script:RefAssemblyPackUrl"

    if (-not (Get-Command Invoke-WebRequest -ErrorAction SilentlyContinue)) {
        throw '当前 PowerShell 无 Invoke-WebRequest，无法自动下载引用程序集。'
    }

    $parent = Split-Path $Destination -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

    $nupkg = Join-Path $parent 'net40.nupkg'
    $zip   = Join-Path $parent 'net40.zip'
    $stage = Join-Path $parent 'net40'

    Invoke-WebRequest -Uri $Script:RefAssemblyPackUrl -OutFile $nupkg -TimeoutSec 300
    Copy-Item $nupkg $zip -Force

    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $stage -Force

    Remove-Item $nupkg, $zip -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path (Join-Path $Destination 'mscorlib.dll'))) {
        throw "下载完成但未找到 mscorlib.dll：$Destination"
    }

    Write-Ok "本地引用程序集已就绪：$Destination"
}

# ---------------------------------------------------------------------------
# 步骤 1：禁用 API 扫描
# ---------------------------------------------------------------------------
function Invoke-BannedApiScan {
    Write-Step '1/8 禁用 API 扫描'

    if ($SkipDisabledApiScan) {
        Write-Warn2 '已跳过禁用 API 扫描（-SkipDisabledApiScan），构建结果不代表符合红线'
        return
    }

    if (-not (Test-Path $SrcDir)) { Write-Err "源码目录不存在：$SrcDir"; throw '源码目录缺失' }

    $files = Get-ChildItem $SrcDir -Recurse -File -Include *.cs
    if (-not $files) { Write-Warn2 '未找到任何 .cs 文件，跳过扫描'; return }

    $hits = New-Object System.Collections.Generic.List[string]

    foreach ($f in $files) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            $lineNo++

            # 允许用 build-scan-ignore 显式豁免（例如注释中确实需要提到这些词）
            if ($line -match 'build-scan-ignore') { continue }

            # 去掉行内注释后再匹配，避免注释里的示例文字造成误报
            $code = $line -replace '//.*$', ''
            $code = $code -replace '/\*.*?\*/', ''
            if ([string]::IsNullOrEmpty($code.Trim())) { continue }

            foreach ($rule in $Script:BannedApis.GetEnumerator()) {
                if ($code -match $rule.Value) {
                    $rel = $f.FullName.Substring($Root.Length).TrimStart('\')
                    $hits.Add(("{0}({1}): {2} -> {3}" -f $rel, $lineNo, $rule.Key, $code.Trim()))
                }
            }
        }
    }

    if ($hits.Count -gt 0) {
        Write-Err "发现 $($hits.Count) 处禁用 API："
        $hits | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw '禁用 API 扫描未通过，构建中止'
    }

    Write-Ok "已扫描 $($files.Count) 个 .cs 文件，未发现禁用 API"
}

# ---------------------------------------------------------------------------
# 步骤 2：解析引用程序集
# ---------------------------------------------------------------------------
function Resolve-ReferencePath {
    Write-Step '2/8 解析 net40 引用程序集（双轨）'

    $r = Resolve-Net40ReferencePath
    if ($r) {
        $ver = (Get-Item (Join-Path $r.Path 'mscorlib.dll')).VersionInfo.FileVersion
        Write-Ok "轨道[$($r.Mode)] 命中：$($r.Path)"
        Write-Info "mscorlib 版本：$ver"
        return $r.Path
    }

    Write-Warn2 '两条轨道均未命中，尝试自动下载到 .toolchain'
    Install-LocalReferencePack -Destination $LocalRefDir

    $r = Resolve-Net40ReferencePath
    if (-not $r) { throw '引用程序集准备失败' }
    Write-Ok "轨道[$($r.Mode)] 命中：$($r.Path)"
    return $r.Path
}

# ---------------------------------------------------------------------------
# 步骤 3：定位 MSBuild
# ---------------------------------------------------------------------------
function Resolve-MSBuild {
    Write-Step '3/8 定位 MSBuild'

    $candidates = @()

    # 优先 .NET Framework 自带的 MSBuild（4.0，可编 net40）
    $frameworkRoots = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64')
    )
    foreach ($root in $frameworkRoots) {
        $candidates += (Join-Path $root 'v4.0.30319\MSBuild.exe')
    }

    # 其次 VS 自带的 MSBuild（若用户后续装了 VS）
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($vsPath) { $candidates += (Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe') }
    }

    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) {
            Write-Ok "MSBuild：$c"
            return $c
        }
    }

    throw '未找到 MSBuild.exe。请安装 .NET Framework 4.0+ 或 Visual Studio。'
}

# ---------------------------------------------------------------------------
# 步骤 4：编译
# ---------------------------------------------------------------------------
function Invoke-Build {
    param([string]$MSBuild, [string]$RefPath, [bool]$IncludeTests)

    Write-Step "4/8 编译（Configuration=$Configuration）"

    $pdbMode = if ($Configuration -eq 'Release') { 'none' } else { 'full' }
    $debugSymbols = if ($Configuration -eq 'Release') { 'false' } else { 'true' }

    $commonArgs = @(
        '/nologo'
        '/v:minimal'
        '/p:Configuration=' + $Configuration
        '/p:Platform=Any CPU'
        '/p:TargetFrameworkVersion=v4.0'
        "/p:FrameworkPathOverride=$RefPath"
        "/p:DebugType=$pdbMode"
        "/p:DebugSymbols=$debugSymbols"
        '/p:GenerateSerializationAssemblies=Off'
    )

    if (-not (Test-Path $SlnPath)) { throw "解决方案不存在：$SlnPath" }

    Write-Info "输出目录：$OutDir"
    $out = & $MSBuild $SlnPath @commonArgs 2>&1
    $code = $LASTEXITCODE

    # 过滤掉 Targeting Pack 相关的噪音警告（我们用 FrameworkPathOverride 显式提供了）
    $out | Where-Object { $_ -notmatch 'MSB3644' } | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    if ($code -ne 0) {
        Write-Err "编译失败，退出码 $code"
        throw '编译失败'
    }

    Write-Ok '编译成功'
}

# ---------------------------------------------------------------------------
# 步骤 5：产物校验
# ---------------------------------------------------------------------------
function Test-OutputArtifacts {
    Write-Step '5/8 产物校验'

    if (-not (Test-Path $OutDir)) { throw "输出目录不存在：$OutDir" }

    $exe = Join-Path $OutDir 'MQNetScan.exe'
    if (-not (Test-Path $exe)) { Write-Err "未生成 MQNetScan.exe"; throw '产物缺失' }
    Write-Ok ("MQNetScan.exe ({0:N0} KB)" -f ((Get-Item $exe).Length / 1KB))

    # 允许的产物：exe、配置文件说明、pdb（仅非 Release）、运行期生成的 ini
    # 注意 MQNetScan.ini 是程序首次运行时在 exe 同级生成的（PRD FR-4.3.2），
    # 若开发期间从输出目录启动过程序，它会出现在这里，属正常现象。
    $allowed = @('MQNetScan.exe', 'MQNetScan.exe.config', 'MQNetScan.pdb', 'MQNetScan.ini')
    $unexpected = Get-ChildItem $OutDir -File |
        Where-Object { $allowed -notcontains $_.Name } |
        Where-Object { $_.Name -notlike '*.pdb' -or $Configuration -eq 'Release' }

    if ($unexpected) {
        Write-Err '输出目录出现非预期文件：'
        $unexpected | ForEach-Object { Write-Host "    $($_.Name)" -ForegroundColor Red }
        throw '产物校验失败'
    }

    # Release 发布前提醒：ini 是运行期产物，不应随包分发（否则会覆盖用户现场配置）
    if ($Configuration -eq 'Release' -and (Test-Path (Join-Path $OutDir 'MQNetScan.ini'))) {
        Write-Warn2 '输出目录存在 MQNetScan.ini（开发期启动过程序所致）；对外发布前请删除，避免覆盖用户现场配置'
    }

    Write-Ok '输出目录无冗余文件'
}

# ---------------------------------------------------------------------------
# 步骤 6：引用程序集白名单
# ---------------------------------------------------------------------------
function Test-ReferencedAssemblies {
    param([string]$RefPath)

    Write-Step '6/8 引用程序集白名单校验'

    $exe = Join-Path $OutDir 'MQNetScan.exe'

    # 用 ReflectionOnlyLoadFrom 避免加载到当前进程
    $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($exe)
    $refs = $asm.GetReferencedAssemblies()

    $violations = @()
    foreach ($r in $refs) {
        if ($AllowedReferences -notcontains $r.Name) { $violations += "$($r.Name) v$($r.Version)" }
    }

    Write-Info ("引用程序集（{0} 个）：{1}" -f $refs.Count, (($refs | ForEach-Object { $_.Name }) -join ', '))

    if ($violations.Count -gt 0) {
        Write-Err '发现白名单之外的引用：'
        $violations | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw '引用白名单校验失败'
    }

    Write-Ok "全部引用均在白名单内（$($AllowedReferences -join ', ')）"

    # 附加校验：确认确实编译成了 net40
    $core = $refs | Where-Object { $_.Name -eq 'mscorlib' }
    if ($core -and $core.Version.Major -ne 4) {
        Write-Err "mscorlib 引用版本异常：$($core.Version)"
        throw '目标框架校验失败'
    }
    Write-Ok "mscorlib 引用版本：$($core.Version)（符合 net40 预期）"
}

# ---------------------------------------------------------------------------
# 步骤 7：pdb 与图标校验
# ---------------------------------------------------------------------------
function Test-PdbAndIcon {
    Write-Step '7/8 pdb 与图标校验'

    $exe  = Join-Path $OutDir 'MQNetScan.exe'
    $pdb  = Join-Path $OutDir 'MQNetScan.pdb'

    if ($Configuration -eq 'Release') {
        if (Test-Path $pdb) { Write-Err 'Release 配置下不应生成 .pdb'; throw 'pdb 校验失败' }
        Write-Ok 'Release 配置未生成 .pdb'
    }
    else {
        if (-not (Test-Path $pdb)) { Write-Warn2 "$Configuration 配置下未生成 .pdb（调试体验会受影响）" }
        else { Write-Ok "$Configuration 配置已生成 .pdb" }
    }

    # 图标校验：只判断有无，不比对内容（便于用户随时替换图标）
    try {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
        if ($null -eq $icon) { Write-Err '未能从 exe 提取图标'; throw '图标校验失败' }
        Write-Ok "exe 图标资源存在（$($icon.Width)x$($icon.Height)），不比对内容"
        $icon.Dispose()
    }
    catch {
        if ($_.Exception.Message -like '*图标校验失败*') { throw }
        Write-Warn2 "图标提取失败：$($_.Exception.Message)"
    }

    # 权限级别校验：确认生效的 requestedExecutionLevel 为 asInvoker（PRD NFR-P1）
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $idx = $text.IndexOf('<requestedExecutionLevel')
    if ($idx -lt 0) {
        Write-Err 'exe 中未找到 requestedExecutionLevel 声明'
        throw '权限级别校验失败'
    }

    $segment = $text.Substring($idx, [Math]::Min(200, $text.Length - $idx))
    if ($segment -match 'level="asInvoker"') {
        Write-Ok '权限级别为 asInvoker（启动不触发 UAC）'
    }
    else {
        $shown = $segment.Substring(0, [Math]::Min(80, $segment.Length)) -replace '\s+', ' '
        Write-Err "权限级别不是 asInvoker：$shown"
        throw '权限级别校验失败'
    }
}

# ---------------------------------------------------------------------------
# 步骤 8：可选复制文档
# ---------------------------------------------------------------------------
function Copy-DocsIfRequested {
    Write-Step '8/8 文档复制'

    if (-not $WithDocs) { Write-Info '未指定 -WithDocs，跳过'; return }

    $docSrc = Join-Path $Root 'doc'
    if (-not (Test-Path $docSrc)) { Write-Warn2 'doc 目录不存在，跳过'; return }

    $docDst = Join-Path $OutDir 'doc'
    New-Item -ItemType Directory -Force -Path $docDst | Out-Null
    Copy-Item (Join-Path $docSrc '*.md') $docDst -Force

    Write-Ok "已复制 $((Get-ChildItem $docDst -Filter *.md).Count) 份文档到 $docDst"
}

# ---------------------------------------------------------------------------
# 可选：生成便携 Release ZIP
# ---------------------------------------------------------------------------
function New-PortableReleaseZip {
    if (-not $PackageZip) { return }
    if ($Configuration -ne 'Release') { throw '-PackageZip 只能与 -Configuration Release 一起使用' }

    $packageName = 'MQNetScan-v1.2.0'
    $stageRoot = Join-Path $Root 'build\package-stage'
    $packageDir = Join-Path $stageRoot $packageName
    $zipPath = Join-Path $OutDir ("{0}.zip" -f $packageName)

    if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

    foreach ($name in @('MQNetScan.exe', 'MQNetScan.exe.config')) {
        $source = Join-Path $OutDir $name
        if (-not (Test-Path $source)) { throw "发布文件缺失：$source" }
        Copy-Item $source $packageDir -Force
    }

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

    $zip = Get-Item $zipPath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        $allowed = @('MQNetScan.exe', 'MQNetScan.exe.config')
        $unexpected = @($entries | Where-Object { $allowed -notcontains $_ })
        $missing = @($allowed | Where-Object { $entries -notcontains $_ })
        if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
            throw ("Release ZIP 内容错误。Unexpected=[{0}] Missing=[{1}]" -f `
                ($unexpected -join ', '), ($missing -join ', '))
        }
    }
    finally { $archive.Dispose() }

    Write-Ok ("Portable release ZIP created: {0} ({1:N0} KB; exe + config only)" -f $zip.FullName, ($zip.Length / 1KB))
    Remove-Item $stageRoot -Recurse -Force
}

# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------
$startTime = Get-Date

if ($Configuration -eq 'Release' -and $PackageZip) {
    $stalePackage = Join-Path $Root 'build\Release\MQNetScan-v1.2.0.zip'
    if (Test-Path $stalePackage) { Remove-Item $stalePackage -Force }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor White
Write-Host ' MoeQuick NetScan - Build & Red-line Verification' -ForegroundColor White
Write-Host " Configuration : $Configuration" -ForegroundColor White
Write-Host " Root          : $Root" -ForegroundColor White
Write-Host '============================================================' -ForegroundColor White

try {
    Invoke-BannedApiScan
    $refPath = Resolve-ReferencePath
    $msbuild = Resolve-MSBuild
    Invoke-Build -MSBuild $msbuild -RefPath $refPath -IncludeTests ($Configuration -ne 'Release')
    Test-OutputArtifacts
    Test-ReferencedAssemblies -RefPath $refPath
    Test-PdbAndIcon
    Copy-DocsIfRequested
    New-PortableReleaseZip
}
catch {
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Red
    Write-Host " 构建失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host '============================================================' -ForegroundColor Red
    exit 1
}

$elapsed = (Get-Date) - $startTime

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host (" BUILD SUCCESS  ({0:N1}s)" -f $elapsed.TotalSeconds) -ForegroundColor Green
Write-Host " Output : $OutDir" -ForegroundColor Green
if ($Warnings.Count -gt 0) {
    Write-Host " Warnings ($($Warnings.Count)):" -ForegroundColor Yellow
    $Warnings | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
}
Write-Host '============================================================' -ForegroundColor Green
exit 0
