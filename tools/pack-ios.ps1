# 把 Unity 导出的 ios-build 打成分卷并推送到 GitHub
#
# 用法（在仓库根目录，即包含 ios-build 的那一层）：
#   powershell -ExecutionPolicy Bypass -File tools\pack-ios.ps1 -RepoUrl https://github.com/你的用户名/ar-issue-tracker.git
#
# 做四件事：
#   1. 写 .gitignore（忽略 Library/Temp/node_modules 等，但保留 ios-build 的分卷）
#   2. 把 ios-build 压成 tar.gz，再切成 45MB 的分卷（绕开 GitHub 单文件 100MB 限制）
#   3. git init + 提交源码 + 推送
#   4. 逐个推送分卷，每个失败自动重试 3 次（避免一次传几百 MB 触发 HTTP 408 超时）

param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [int]$PartSizeMB = 45
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [!]  $msg" -ForegroundColor Yellow }

# ---------- 0. 定位仓库根目录 ----------
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root "ios-build"))) { $root = (Get-Location).Path }
Set-Location $root
Write-Step "工作目录：$root"

if (-not (Test-Path "ios-build\Unity-iPhone.xcodeproj")) {
    Write-Host "找不到 ios-build\Unity-iPhone.xcodeproj" -ForegroundColor Red
    Write-Host "请先在 Unity 里 File -> Build Settings -> iOS -> Build，导出到本目录的 ios-build 文件夹。" -ForegroundColor Red
    exit 1
}
Write-Ok "找到 ios-build\Unity-iPhone.xcodeproj"

# ---------- 1. .gitignore ----------
Write-Step "写入 .gitignore"
$ignore = @(
    "# Unity",
    "[Ll]ibrary/",
    "[Tt]emp/",
    "[Oo]bj/",
    "[Bb]uild/",
    "[Ll]ogs/",
    "[Uu]serSettings/",
    "*.csproj",
    "*.sln",
    "*.user",
    "",
    "# 前端",
    "node_modules/",
    "dist/",
    "",
    "# 运行期数据",
    "backend/issues.json",
    "backend/issues.json.tmp",
    "",
    "# iOS 原始目录（用 ios-build.part* 分卷代替）",
    "ios-build/",
    "*.tar.gz",
    "*.ipa"
)
Set-Content -Path ".gitignore" -Value $ignore -Encoding Ascii
Write-Ok ".gitignore 已写入"

# ---------- 2. 打包 + 分卷 ----------
$needPack = $true
if (Test-Path "ios-build.tar.gz") {
    $tarLen = (Get-Item "ios-build.tar.gz").Length
    $existParts = @(Get-ChildItem "ios-build.part*" -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($existParts.Count -gt 0) {
        $sum = ($existParts | Measure-Object -Property Length -Sum).Sum
        if ($sum -eq $tarLen) {
            $needPack = $false
            $count = $existParts.Count
            Write-Ok "已存在完整分卷（$count 个，合计 $sum 字节），跳过打包与切分"
        }
    }
}

if ($needPack) {
    Write-Step "压缩 ios-build（约 230MB，需要 30~60 秒）"
    if (Test-Path "ios-build.tar.gz") { Remove-Item "ios-build.tar.gz" -Force }
    & tar.exe -czf "ios-build.tar.gz" "ios-build"
    if ($LASTEXITCODE -ne 0) { Write-Host "tar 打包失败" -ForegroundColor Red; exit 1 }
    $tarSize = (Get-Item "ios-build.tar.gz").Length
    Write-Ok ("ios-build.tar.gz = {0:N0} 字节" -f $tarSize)

    Write-Step "按 ${PartSizeMB}MB 切分"
    Get-ChildItem "ios-build.part*" -ErrorAction SilentlyContinue | Remove-Item -Force
    $bytes    = [IO.File]::ReadAllBytes((Join-Path $root "ios-build.tar.gz"))
    $partSize = $PartSizeMB * 1MB
    $count    = [Math]::Ceiling($bytes.Length / $partSize)
    for ($i = 0; $i -lt $count; $i++) {
        $start = $i * $partSize
        $len   = [Math]::Min($partSize, $bytes.Length - $start)
        $chunk = New-Object byte[] $len
        [Array]::Copy($bytes, $start, $chunk, 0, $len)
        [IO.File]::WriteAllBytes((Join-Path $root "ios-build.part$i"), $chunk)
        Write-Ok ("ios-build.part{0} = {1:N0} 字节" -f $i, $len)
    }
    Write-Ok "共 $count 个分卷"
}

# ---------- 3. git 初始化 ----------
Write-Step "配置 git 仓库"
git config --global core.longpaths true
git config --global http.postBuffer 524288000

if (-not (Test-Path ".git")) { git init | Out-Null }
git branch -M main 2>$null

$existing = git remote 2>$null
if ($existing -contains "origin") {
    git remote set-url origin $RepoUrl
    Write-Ok "已更新 origin -> $RepoUrl"
} else {
    git remote add origin $RepoUrl
    Write-Ok "已添加 origin -> $RepoUrl"
}

# ---------- 4. 推送源码（先只推几 MB 的源码，绝不大文件一起推） ----------
Write-Step "提交并推送源码"
git add -A
git reset -q -- "ios-build.part*" 2>$null   # 分卷留到后面一个个推
git commit -m "feat: Unity AR + Go backend + React frontend" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Warn2 "没有新改动需要提交，继续" }

git push -u origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "源码推送失败。请检查：仓库是否已创建、是否登录了 GitHub（弹出的浏览器授权窗口要点同意）" -ForegroundColor Red
    exit 1
}
Write-Ok "源码已推送"

# ---------- 5. 逐个推送分卷 ----------
Write-Step "逐个推送分卷（每个失败自动重试 3 次）"
for ($i = 0; $i -lt $count; $i++) {
    $name = "ios-build.part$i"
    git add $name
    git commit -m "chore: add $name" 2>$null
    $ok = $false
    for ($try = 1; $try -le 3; $try++) {
        git push origin main
        if ($LASTEXITCODE -eq 0) { $ok = $true; break }
        Write-Warn2 "$name 第 $try 次推送失败，10 秒后重试"
        Start-Sleep -Seconds 10
    }
    if ($ok) { Write-Ok "$name 已上传" }
    else { Write-Host "$name 上传失败，请重新运行本脚本（已上传的会自动跳过）" -ForegroundColor Red; exit 1 }
}

Write-Step "全部完成"
Write-Host "接下来：" -ForegroundColor Cyan
Write-Host "  1. 打开 GitHub 仓库的 Actions 页面"
Write-Host "  2. 左侧选 Build Unsigned iOS IPA -> 右侧 Run workflow"
Write-Host "  3. 等 4~6 分钟变绿勾，进任务页底部 Artifacts 下载 ar-issue-unsigned-ipa"
