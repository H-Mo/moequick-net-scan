# 假设备模拟器（开发期专用，不随产品发布）
#
# 用途：在本机 192.168.122.39 上同时监听多个特征端口，
#       让 MQNetScan 能验证「端口扫描 + 角色判定」链路。
#
# 局限（务必知悉）：
#   · 只有一个 IP，因此角色判定会按优先级合并为单一角色；
#   · 无法模拟"多台不同角色设备"，那需要宿主机添加 IP 别名；
#   · 本脚本仅用于开发自测，正式验收必须使用真实局域网设备。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\fake-devices.ps1
#   powershell -ExecutionPolicy Bypass -File tools\fake-devices.ps1 -Ports 9100,1433,445
#
# 停止：Ctrl+C

param(
    [int[]]$Ports = @(9100, 515, 631, 4001, 5000, 8000, 3000, 1433, 445, 3389, 135, 5555),

    [string]$BindAddress = '0.0.0.0'
)

$ErrorActionPreference = 'Stop'

# 端口 -> 角色说明（与 IniSchema 的内置规则保持一致）
$RoleMap = @{
    9100 = '打印机 (RAW 直打)'
    515  = '打印机 (LPR)'
    631  = '打印机 (IPP)'
    4001 = '电子秤 (计价秤通讯)'
    5000 = '电子秤 (条码秤通讯)'
    8000 = '电子秤 (备用)'
    3000 = '电子秤 (备用)'
    1433 = '本地服务器 (SQL Server)'
    445  = '收银电脑 (SMB)'
    3389 = '收银电脑 (RDP)'
    135  = '收银电脑 (RPC)'
    5555 = '安卓 POS (ADB)'
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' 萌快网维 - 假设备模拟器（开发期专用）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host "绑定地址: $BindAddress"
Write-Host "待监听端口: $($Ports -join ', ')"
Write-Host ''

$listeners = @()
$failed = @()

foreach ($port in $Ports) {
    try {
        $listener = New-Object System.Net.Sockets.TcpListener(
            [System.Net.IPAddress]::Parse($BindAddress), $port)
        $listener.Start()
        $listeners += $listener

        $role = if ($RoleMap.ContainsKey($port)) { $RoleMap[$port] } else { '(自定义)' }
        Write-Host ("  [启动] 端口 {0,-5}  模拟: {1}" -f $port, $role) -ForegroundColor Green
    }
    catch {
        $failed += $port
        Write-Host ("  [跳过] 端口 {0,-5}  原因: {1}" -f $port, $_.Exception.Message) -ForegroundColor Yellow
    }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host "未能监听的端口（可能已被占用）: $($failed -join ', ')" -ForegroundColor Yellow
}

if ($listeners.Count -eq 0) {
    Write-Host '没有任何端口监听成功，退出。' -ForegroundColor Red
    exit 1
}

Write-Host "已监听 $($listeners.Count) 个端口。" -ForegroundColor Green
Write-Host ''
Write-Host '现在可以用 MQNetScan 扫描本机地址验证：'
Write-Host '  Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" }'
Write-Host ''
Write-Host '按 Ctrl+C 停止所有监听。' -ForegroundColor DarkGray
Write-Host ''

# 保持前台运行，直到 Ctrl+C
try {
    while ($true) {
        Start-Sleep -Seconds 1
    }
}
finally {
    Write-Host ''
    Write-Host '正在停止监听...' -ForegroundColor Yellow
    foreach ($l in $listeners) {
        try { $l.Stop() } catch { }
    }
    Write-Host '已全部停止。' -ForegroundColor Green
}
