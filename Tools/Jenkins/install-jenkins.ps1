<#
在這台電腦裝 Jenkins（用目前登入的帳號跑 jenkins.war，所以 Unity 授權、gcloud 登入、git 的 SSH 金鑰都直接沿用）：
  powershell -ExecutionPolicy Bypass -File Tools\Jenkins\install-jenkins.ps1

做的事：
  1. 下載 Temurin 21 JRE（zip，免安裝）與 Jenkins LTS 的 jenkins.war 到 D:\Jenkins
  2. 寫 init.groovy.d：只聽 127.0.0.1、不設帳號（本機自用）、跳過安裝精靈、同時只跑 1 個建置（兩個平台不會同時開 Unity）
  3. 寫 D:\Jenkins\start-jenkins.ps1，並在「啟動」資料夾放捷徑，登入 Windows 就在背景啟動
  4. 啟動 Jenkins
裝完接著跑 setup-jobs.ps1 裝外掛、建任務。重跑這支不會重新下載已經有的檔案。
#>
param(
    [string]$Root = 'D:\Jenkins',
    [int]$Port = 8080
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest 的進度條會讓下載慢好幾倍
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$JreUrl = 'https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse'
$WarUrl = 'https://get.jenkins.io/war-stable/latest/jenkins.war'
$Home_ = Join-Path $Root 'home'
New-Item -ItemType Directory -Force $Root, $Home_, (Join-Path $Home_ 'init.groovy.d') | Out-Null

# 1. JRE
$java = Get-ChildItem (Join-Path $Root 'jre') -Recurse -Filter java.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $java) {
    $zip = Join-Path $Root 'jre.zip'
    Write-Host "下載 JRE：$JreUrl"
    Invoke-WebRequest $JreUrl -OutFile $zip -UseBasicParsing
    Expand-Archive $zip (Join-Path $Root 'jre') -Force
    Remove-Item $zip
    $java = Get-ChildItem (Join-Path $Root 'jre') -Recurse -Filter java.exe | Select-Object -First 1
}
Write-Host "Java：$($java.FullName)"

# 2. jenkins.war
$war = Join-Path $Root 'jenkins.war'
if (-not (Test-Path $war)) {
    Write-Host "下載 Jenkins：$WarUrl"
    Invoke-WebRequest $WarUrl -OutFile $war -UseBasicParsing
}

# 3. 首次啟動設定（每次 Jenkins 啟動都會跑，重複套用沒關係）
$initGroovy = @'
import jenkins.model.Jenkins
import jenkins.install.InstallState
import hudson.security.SecurityRealm
import hudson.security.AuthorizationStrategy

def j = Jenkins.get()
// 本機自用、只聽 127.0.0.1：不設帳號
j.setSecurityRealm(SecurityRealm.NO_AUTHENTICATION)
j.setAuthorizationStrategy(AuthorizationStrategy.UNSECURED)
// 同時只跑一個建置：WebGL、Android 打包都很吃資源，排隊比較穩
j.setNumExecutors(1)
if (!j.installState.isSetupComplete()) InstallState.INITIAL_SETUP_COMPLETED.initializeState()
j.save()
// Jenkins URL：沒設的話 jenkins-cli 的 WebSocket 連線會被拒絕（setup-jobs.ps1 要用）
def loc = jenkins.model.JenkinsLocationConfiguration.get()
loc.setUrl('@URL@')
loc.save()
'@
$initGroovy = $initGroovy.Replace('@URL@', "http://127.0.0.1:$Port/")
[IO.File]::WriteAllText((Join-Path $Home_ 'init.groovy.d\local-setup.groovy'), $initGroovy)

# 4. 啟動腳本
$start = @"
# 背景啟動 Jenkins（install-jenkins.ps1 產生）。已經在跑就不重複啟動。
`$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { Write-Host 'Jenkins 已在執行'; exit 0 }
`$env:JENKINS_HOME = '$Home_'
`$jvm = '-Dfile.encoding=UTF-8 -Dsun.jnu.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Djenkins.install.runSetupWizard=false'
`$args_ = "`$jvm -jar ``"$war``" --httpListenAddress=127.0.0.1 --httpPort=$Port"
Start-Process -FilePath '$($java.FullName)' -ArgumentList `$args_ -WindowStyle Hidden ``
    -RedirectStandardOutput '$Home_\jenkins.out.log' -RedirectStandardError '$Home_\jenkins.err.log'
Write-Host 'Jenkins 啟動中：http://127.0.0.1:$Port/'
"@
$startPs1 = Join-Path $Root 'start-jenkins.ps1'
[IO.File]::WriteAllText($startPs1, $start, (New-Object Text.UTF8Encoding $true))

# 5. 登入自動啟動：「啟動」資料夾放捷徑（不用工作排程器、不用系統管理員權限）
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Jenkins.lnk'
$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($lnk)
$sc.TargetPath = 'powershell.exe'
$sc.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$startPs1`""
$sc.WorkingDirectory = $Root
$sc.Save()
Write-Host "登入自動啟動捷徑：$lnk"

# 6. 啟動並等它起來
& powershell -NoProfile -ExecutionPolicy Bypass -File $startPs1
$deadline = (Get-Date).AddMinutes(5)
do {
    Start-Sleep -Seconds 3
    try { $ok = (Invoke-WebRequest "http://127.0.0.1:$Port/api/json" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $ok = $false }
} until ($ok -or (Get-Date) -gt $deadline)
if (-not $ok) { throw "Jenkins 5 分鐘內沒起來，看 $Home_\jenkins.err.log" }
Write-Host "Jenkins 已啟動：http://127.0.0.1:$Port/"
