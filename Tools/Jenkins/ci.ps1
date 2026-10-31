<#
Jenkins 出包流程的各步驟，由 Tools/Jenkins/Jenkinsfile 呼叫。也可以手動跑，例如：
  powershell -ExecutionPolicy Bypass -File Tools\Jenkins\ci.ps1 -Step build -Platform Android -ProjectDir D:\Claude\blender_android -Mode full

步驟：
  version ＝把本機 ServerData 的 .version 指針同步成 GCS 上的版號。版號由指針加 1 配號，不同機器出包才不會撞號。
  build   ＝Unity batchmode 跑 CIBuild.Run（Assets/Editor/CI/CIBuild.cs），log 篩重點即時印到 Jenkins。
  upload  ＝上傳這次打的代碼包／資源包：bundle 先傳，版本清單再傳，.version 指針最後才傳，玩家不會讀到還沒傳完的版號。
  publish ＝只有完整出包才跑：WebGL 網頁本體上傳 GCS；Android 的 APK 複製到 APK 輸出資料夾，資料夾不存在就建立。
模式（-Mode）：full＝完整出包、asset＝只打資源包、code＝只打代碼包、asset+code＝資源包和代碼包都打（不出主體）。

環境變數（可不設）：
  CI_GCS_ROOT    GCS 根目錄，預設 gs://peter_hybridclr_test/blender
  CI_APK_OUTPUT  APK 輸出資料夾，預設 D:\Claude\UnityOutput\APK
  UNITY_EXE      Unity.exe 路徑，預設照 ProjectVersion.txt 到 D:\Program Files\Unity\Hub\Editor 找
#>
param(
    [Parameter(Mandatory)][ValidateSet('version', 'build', 'upload', 'publish')][string]$Step,
    [Parameter(Mandatory)][ValidateSet('WebGL', 'Android')][string]$Platform,
    [Parameter(Mandatory)][string]$ProjectDir,
    [ValidateSet('full', 'asset', 'code', 'asset+code')][string]$Mode = 'full'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$GcsRoot = if ($env:CI_GCS_ROOT) { $env:CI_GCS_ROOT.TrimEnd('/') } else { 'gs://peter_hybridclr_test/blender' }
$ApkOutput = if ($env:CI_APK_OUTPUT) { $env:CI_APK_OUTPUT } else { 'D:\Claude\UnityOutput\APK' }
$NoCache = 'Cache-Control:no-cache'
$ResultFile = Join-Path $ProjectDir 'Builds\ci_result.txt'
$UnityLog = Join-Path $ProjectDir 'Builds\ci_unity.log'
# Android 完整出包的 versionCode（version 步驟算好，build 步驟傳給 Unity）
$VersionCodeFile = Join-Path $ProjectDir 'Builds\ci_version_code.txt'
$IsAppBuild = $Platform -eq 'Android' -and $Mode -eq 'full'

# 執行外部指令，輸出照印，結束碼非 0 就丟例外。
# stderr 合併成一般輸出：PowerShell 5.1 在 Jenkins 底下會把 native stderr 當錯誤，gsutil 的進度訊息就會讓步驟失敗。
# 注意：gsutil 是 .cmd，參數會經過 cmd.exe，不能含 | ^ & 這類字元。
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    Write-Host "> $Exe $($Arguments -join ' ')"
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments 2>&1 | ForEach-Object {
            # stderr 的空行包成 ErrorRecord 後字串會變成「System.Management.Automation.RemoteException」，跳過
            $line = if ($_ -is [Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" }
            if ($line -and $line -ne 'System.Management.Automation.RemoteException') { Write-Host $line }
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    if ($code -ne 0) { throw "$Exe 失敗（結束碼 $code）" }
}

function Get-BundleVersion {
    $text = Get-Content (Join-Path $ProjectDir 'ProjectSettings\ProjectSettings.asset') -Raw
    if ($text -notmatch '(?m)^\s*bundleVersion:\s*(\S+)') { throw '讀不到 ProjectSettings.asset 的 bundleVersion' }
    return $Matches[1]
}

function Get-Packages {
    if ($Mode -eq 'full') { return @('CodePackage', 'AssetPackage') }
    $packages = @()
    if ($Mode -split '\+' -contains 'code') { $packages += 'CodePackage' }
    if ($Mode -split '\+' -contains 'asset') { $packages += 'AssetPackage' }
    return $packages
}

function Get-ServerDataDir { Join-Path $ProjectDir "ServerData\$Platform\$(Get-BundleVersion)" }

function Get-GcsDir { "$GcsRoot/$Platform/$(Get-BundleVersion)" }

function Read-Result {
    if (-not (Test-Path $ResultFile)) { throw "找不到 $ResultFile，Unity 打包沒有跑完" }
    $result = @{}
    foreach ($line in Get-Content $ResultFile -Encoding UTF8) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $result[$line.Substring(0, $i)] = $line.Substring($i + 1) }
    }
    return $result
}

function Get-UnityExe {
    if ($env:UNITY_EXE) { return $env:UNITY_EXE }
    $text = Get-Content (Join-Path $ProjectDir 'ProjectSettings\ProjectVersion.txt') -Raw
    if ($text -notmatch 'm_EditorVersion:\s*(\S+)') { throw '讀不到 ProjectVersion.txt 的 Unity 版本' }
    $exe = "D:\Program Files\Unity\Hub\Editor\$($Matches[1])\Editor\Unity.exe"
    if (-not (Test-Path $exe)) { throw "找不到 $exe（可用環境變數 UNITY_EXE 指定）" }
    return $exe
}

# 印出 Unity log 裡值得看的行（整份 log 太長，完整版另外存成 Jenkins 產物）
$LogFilter = '\[(CIBuild|\w*BuildTool|LinkGeneratorCommand|HybridCLR\w*)\]|error CS\d+|Exception|Build Finished|Build completed|DisplayProgressbar:'

function Step-Version {
    Invoke-Native gsutil @('ls', "$GcsRoot/")   # 先確認權限正常，下面讀不到檔案才能當成「遠端還沒有版本」
    foreach ($pkg in @('CodePackage', 'AssetPackage')) {
        $url = "$(Get-GcsDir)/$pkg/$pkg.version"
        $local = Join-Path (Get-ServerDataDir) "$pkg\$pkg.version"
        $old = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $remote = (& gsutil cat $url 2>$null) -join ''
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $old
        }
        $localVer = if (Test-Path $local) { (Get-Content $local -Raw).Trim() } else { '（無）' }
        if ($code -ne 0) {
            Write-Host "$pkg：遠端還沒有版號，沿用本機 $localVer"
            continue
        }
        $remote = $remote.Trim()
        if ($remote -notmatch '^\d+(\.\d+)+$') { throw "$url 內容不是版號：'$remote'" }
        New-Item -ItemType Directory -Force (Split-Path $local) | Out-Null
        [IO.File]::WriteAllText($local, $remote)   # 不帶 BOM，與 YooAsset 寫的格式一致
        Write-Host "$pkg：本機 $localVer → 遠端 $remote，這次出包會從 $remote 往後配號"
    }
    if ($IsAppBuild) {
        # 主體 versionCode：GCS 上 Android/app/version.json 的 versionCode + 1（還沒有時從 2 開始：最早裝的 APK 是 1）
        $remoteCode = 1
        $json = Get-RemoteText "$GcsRoot/Android/app/version.json"
        if ($json) { $remoteCode = [long](($json | ConvertFrom-Json).versionCode) }
        $next = $remoteCode + 1
        New-Item -ItemType Directory -Force (Split-Path $VersionCodeFile) | Out-Null
        [IO.File]::WriteAllText($VersionCodeFile, "$next")
        Write-Host "主體 versionCode：遠端 $remoteCode → 這次 $next"
    }
}

# 讀 GCS 上的文字檔；不存在回 $null（權限問題由 Step-Version 一開始的 gsutil ls 擋掉）
function Get-RemoteText([string]$Url) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $text = (& gsutil cat $Url 2>$null) -join "`n"
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    if ($code -ne 0) { return $null }
    return $text
}

function Step-Build {
    $lock = Join-Path $ProjectDir 'Temp\UnityLockfile'
    if (Test-Path $lock) {
        try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Close() }
        catch { throw "Unity 正開著 $ProjectDir，先關掉再出包" }
    }
    $unity = Get-UnityExe
    New-Item -ItemType Directory -Force (Split-Path $UnityLog) | Out-Null
    if (Test-Path $UnityLog) { Remove-Item $UnityLog }
    $argLine = "-batchmode -quit -projectPath `"$ProjectDir`" -buildTarget $Platform -executeMethod CIBuild.Run -ciMode $Mode -logFile `"$UnityLog`""
    if ($IsAppBuild) {
        if (-not (Test-Path $VersionCodeFile)) { throw "找不到 $VersionCodeFile（version 步驟沒跑？）" }
        $argLine += " -ciVersionCode $((Get-Content $VersionCodeFile -Raw).Trim())"
    }
    Write-Host "> $unity $argLine"
    $start = Get-Date
    # Unity.exe 是視窗程式，直接 & 呼叫不會等它結束，所以用 Start-Process，邊等邊讀 log
    $p = Start-Process -FilePath $unity -ArgumentList $argLine -PassThru -WindowStyle Hidden
    $null = $p.Handle   # 先取 Handle，結束後才讀得到 ExitCode
    $pos = 0
    $utf8 = [Text.Encoding]::UTF8
    do {
        $exited = $p.WaitForExit(5000)
        if (-not (Test-Path $UnityLog)) { continue }
        $fs = [IO.File]::Open($UnityLog, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            $len = [int]($fs.Length - $pos)
            if ($len -le 0) { continue }
            $buf = New-Object byte[] $len
            $null = $fs.Seek($pos, 'Begin')
            $n = $fs.Read($buf, 0, $len)
            # 只處理到最後一個換行，避免把寫到一半的行（或切開的 UTF-8 字）印出來；結束時全部印完
            $end = if ($exited) { $n } else { [Array]::LastIndexOf($buf, [byte]10, $n - 1) + 1 }
            if ($end -le 0) { continue }
            $pos += $end
            foreach ($line in $utf8.GetString($buf, 0, $end) -split "`r?`n") {
                if ($line -match $LogFilter) { Write-Host $line }
            }
        } finally {
            $fs.Close()
        }
    } until ($exited)
    $minutes = [math]::Round(((Get-Date) - $start).TotalMinutes, 1)
    if ($env:WORKSPACE) { Copy-Item $UnityLog (Join-Path $env:WORKSPACE 'unity.log') -Force }
    if ($p.ExitCode -ne 0) { throw "Unity 打包失敗（結束碼 $($p.ExitCode)，耗時 $minutes 分），完整 log 見 $UnityLog" }
    $result = Read-Result
    Write-Host "Unity 打包完成，耗時 $minutes 分：$($result.summary)"
}

function Step-Upload {
    $null = Read-Result
    foreach ($pkg in Get-Packages) {
        $src = Join-Path (Get-ServerDataDir) $pkg
        $dst = "$(Get-GcsDir)/$pkg"
        $ver = (Get-Content (Join-Path $src "$pkg.version") -Raw).Trim()
        Write-Host "=== $pkg $ver → $dst"
        # 1. bundle：排除版本清單（<套件>_<版號>.*）與指針（<套件>.version）。本機留著的舊版清單不上傳，遠端舊版保留不刪。
        Invoke-Native gsutil @('-m', '-h', $NoCache, 'rsync', '-r', '-x', "$pkg[._].*", $src, $dst)
        # 2. 這一版的清單
        $manifests = @(Get-ChildItem $src -File -Filter "$($pkg)_$ver.*" | ForEach-Object { $_.FullName })
        if ($manifests.Count -eq 0) { throw "$src 找不到 $($pkg)_$ver.* 版本清單" }
        Invoke-Native gsutil (@('-m', '-h', $NoCache, 'cp') + $manifests + "$dst/")
        # 3. 指針最後傳
        Invoke-Native gsutil @('-h', $NoCache, 'cp', (Join-Path $src "$pkg.version"), "$dst/$pkg.version")
        Write-Host "$pkg 上傳完成：$ver"
    }
}

function Step-Publish {
    $result = Read-Result
    if ($Mode -ne 'full') { Write-Host '不是完整出包，略過主體'; return }
    if ($Platform -eq 'WebGL') {
        $web = Join-Path $ProjectDir 'Builds\WebGL'
        $dst = "$GcsRoot/WebGL/web"
        if (-not (Test-Path (Join-Path $web 'index.html'))) { throw "$web 沒有 index.html" }
        # .unityweb 要 Content-Type application/vnd.unity，Unity 的 decompression fallback 認這個
        Invoke-Native gsutil @('-m', '-h', $NoCache, '-h', 'Content-Type:application/vnd.unity', 'cp', (Join-Path $web 'Build\*.unityweb'), "$dst/Build/")
        Invoke-Native gsutil @('-m', '-h', $NoCache, 'rsync', '-r', '-x', '.*\.unityweb', (Join-Path $web 'Build'), "$dst/Build")
        Invoke-Native gsutil @('-m', '-h', $NoCache, 'rsync', '-r', (Join-Path $web 'TemplateData'), "$dst/TemplateData")
        Invoke-Native gsutil @('-h', $NoCache, 'cp', (Join-Path $web 'index.html'), "$dst/index.html")   # 入口最後傳
        $url = $dst -replace '^gs://', 'https://storage.googleapis.com/'
        Write-Host "網頁本體上傳完成：$url/index.html"
    } else {
        $apk = Join-Path $ProjectDir "Builds\Android\$($result.productName)-$($result.bundleVersion).apk"
        if (-not (Test-Path $apk)) { throw "找不到 $apk" }
        if (-not (Test-Path $ApkOutput)) {
            New-Item -ItemType Directory -Force $ApkOutput | Out-Null
            Write-Host "建立 APK 輸出資料夾 $ApkOutput"
        }
        $dir = Get-ServerDataDir
        $code = (Get-Content (Join-Path $dir 'CodePackage\CodePackage.version') -Raw).Trim()
        $asset = (Get-Content (Join-Path $dir 'AssetPackage\AssetPackage.version') -Raw).Trim()
        $name = '{0}-{1}-code{2}-asset{3}-{4:yyyyMMdd-HHmm}.apk' -f $result.productName, $result.bundleVersion, $code, $asset, (Get-Date)
        $target = Join-Path $ApkOutput $name
        Copy-Item $apk $target
        Write-Host "APK 已複製到 $target（$([math]::Round((Get-Item $target).Length / 1MB, 1)) MB）"

        # 主體自動更新：APK 傳到 GCS 的 Android/app/，版本資訊 version.json 最後傳（玩家不會讀到 APK 還沒傳完的版本）
        $versionCode = [long]$result.versionCode
        $appDir = "$GcsRoot/Android/app"
        $apkName = "$($result.productName)-$versionCode.apk"
        Invoke-Native gsutil @('cp', $apk, "$appDir/$apkName")
        $info = [ordered]@{
            versionCode = $versionCode
            versionName = $result.bundleVersion
            apk         = $apkName
            size        = (Get-Item $apk).Length
            sha256      = (Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $infoFile = Join-Path $ProjectDir 'Builds\Android\version.json'
        [IO.File]::WriteAllText($infoFile, ($info | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
        Invoke-Native gsutil @('-h', $NoCache, '-h', 'Content-Type:application/json', 'cp', $infoFile, "$appDir/version.json")
        $url = $appDir -replace '^gs://', 'https://storage.googleapis.com/'
        Write-Host "主體已發佈：versionCode $versionCode → $url/$apkName"
    }
}

Write-Host "[ci] $Step：平台 $Platform、模式 $Mode、專案 $ProjectDir"
switch ($Step) {
    'version' { Step-Version }
    'build' { Step-Build }
    'upload' { Step-Upload }
    'publish' { Step-Publish }
}
# 走到這裡就是成功：失敗都會丟例外。明確回 0，否則 Jenkins 會拿最後一個外部指令的結束碼
# （例如 gsutil cat 讀不到還不存在的 version.json 回 1）當成這一步的結果
exit 0
