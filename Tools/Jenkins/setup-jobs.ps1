<#
裝 Pipeline 外掛，並建立／更新兩個出包任務（Jenkins 要先由 install-jenkins.ps1 裝好並在執行中）：
  powershell -ExecutionPolicy Bypass -File Tools\Jenkins\setup-jobs.ps1

任務：
  blender-WebGL    → D:\Claude\blender
  blender-Android  → D:\Claude\blender_android
任務的 pipeline 內容取自同資料夾的 Jenkinsfile。改了 Jenkinsfile 重跑這支即可更新，已經有的外掛不會重裝。
#>
param(
    [string]$JenkinsUrl = 'http://127.0.0.1:8080',
    [string]$Java = '',
    [hashtable]$Jobs = [ordered]@{
        'blender-WebGL'   = @{ Platform = 'WebGL'; ProjectDir = 'D:\Claude\blender' }
        'blender-Android' = @{ Platform = 'Android'; ProjectDir = 'D:\Claude\blender_android' }
    }
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
# 三個勾選參數（名稱要與 Jenkinsfile 的 modeOf() 一致）。勾「完整出包」時另外兩個不看；資源包、代碼包可以一起勾，預設兩個都勾。
# 每次出包都會先清空專案裡未 commit 的改動、同步到最新的 origin/main，並把版號同步成 GCS 上的版號再打包。
$ModeParams = [ordered]@{
    'BUILD_FULL'  = '完整出包（主體＋資源包＋代碼包）：代碼包重產 AOT dll。WebGL 的網頁本體上傳 GCS；Android 的 APK 放到 D:\Claude\UnityOutput\APK（資料夾不存在會自動建立）。勾了這個，下面兩個不用勾。'
    'BUILD_ASSET' = '資源包（AssetPackage）：打資源包並上傳 GCS。'
    'BUILD_CODE'  = '代碼包（CodePackage）：打代碼包並上傳 GCS。沿用上次完整出包的 AOT dll，只適用主體沒變（Entry、link.xml 沒改）的時候。'
}
$Plugins = @(
    'workflow-job', 'workflow-cps', 'workflow-basic-steps', 'workflow-durable-task-step',
    'pipeline-model-definition', 'pipeline-stage-view'
)

if (-not $Java) { $Java = (Get-ChildItem 'D:\Jenkins\jre' -Recurse -Filter java.exe | Select-Object -First 1).FullName }
$cliJar = Join-Path $env:TEMP 'jenkins-cli.jar'
Invoke-WebRequest "$JenkinsUrl/jnlpJars/jenkins-cli.jar" -OutFile $cliJar -UseBasicParsing

function Invoke-Cli([string[]]$CliArgs, [string]$InputText = $null) {
    $all = @('-Dfile.encoding=UTF-8', '-jar', $cliJar, '-s', $JenkinsUrl, '-webSocket') + $CliArgs
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($null -ne $InputText) {
            # 用檔案當標準輸入：PowerShell 5.1 用管線送字串給 native 程式會把中文轉成問號
            $in = New-TemporaryFile
            $outFile = New-TemporaryFile
            [IO.File]::WriteAllText($in, $InputText, (New-Object Text.UTF8Encoding $false))
            $quoted = $all | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }
            $p = Start-Process -FilePath $Java -ArgumentList $quoted -NoNewWindow -Wait -PassThru `
                -RedirectStandardInput $in -RedirectStandardOutput $outFile
            $out = Get-Content $outFile -Encoding UTF8
            Remove-Item $in, $outFile
            $global:LASTEXITCODE = $p.ExitCode
        } else {
            $out = & $Java @all 2>&1 | ForEach-Object { "$_" }
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    return [pscustomobject]@{ Code = $code; Output = ($out -join "`n") }
}

function Wait-Jenkins {
    $deadline = (Get-Date).AddMinutes(5)
    do {
        Start-Sleep -Seconds 3
        try { $ok = (Invoke-WebRequest "$JenkinsUrl/api/json" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $ok = $false }
    } until ($ok -or (Get-Date) -gt $deadline)
    if (-not $ok) { throw 'Jenkins 5 分鐘內沒起來' }
}

# 1. 外掛（相依的外掛會一起裝）
$installed = (Invoke-Cli @('list-plugins')).Output
$missing = @($Plugins | Where-Object { $installed -notmatch "(?m)^$([regex]::Escape($_))\s" })
if ($missing.Count -gt 0) {
    Write-Host "安裝外掛：$($missing -join ', ')"
    $r = Invoke-Cli (@('install-plugin') + $missing + '-deploy')
    Write-Host $r.Output
    if ($r.Code -ne 0) { throw '外掛安裝失敗' }
    Write-Host '重新啟動 Jenkins 讓外掛生效'
    $null = Invoke-Cli @('safe-restart')
    Start-Sleep -Seconds 10
    Wait-Jenkins
} else {
    Write-Host '外掛都已安裝'
}

# 2. 任務
$template = Get-Content (Join-Path $Here 'Jenkinsfile') -Raw -Encoding UTF8
$xmlEscape = { param($s) [Security.SecurityElement]::Escape($s) }
foreach ($name in $Jobs.Keys) {
    $job = $Jobs[$name]
    # Groovy 單引號字串裡反斜線要寫兩個
    $script = $template.Replace('@PLATFORM@', $job.Platform).Replace('@PROJECT_DIR@', $job.ProjectDir.Replace('\', '\\'))
    $desc = "《熱浪急件》$($job.Platform) 出包：專案 $($job.ProjectDir)。由 Tools/Jenkins/setup-jobs.ps1 產生，請改 repo 裡的 Jenkinsfile 後重跑該腳本，不要直接在網頁上改。"
    $paramXml = ($ModeParams.Keys | ForEach-Object {
        "        <hudson.model.BooleanParameterDefinition><name>$_</name><description>$(& $xmlEscape $ModeParams[$_])</description><defaultValue>$(if ($_ -eq 'BUILD_FULL') { 'false' } else { 'true' })</defaultValue></hudson.model.BooleanParameterDefinition>"
    }) -join "`n"
    $xml = @"
<?xml version='1.1' encoding='UTF-8'?>
<flow-definition plugin="workflow-job">
  <description>$(& $xmlEscape $desc)</description>
  <keepDependencies>false</keepDependencies>
  <properties>
    <org.jenkinsci.plugins.workflow.job.properties.DisableConcurrentBuildsJobProperty/>
    <hudson.model.ParametersDefinitionProperty>
      <parameterDefinitions>
$paramXml
      </parameterDefinitions>
    </hudson.model.ParametersDefinitionProperty>
  </properties>
  <definition class="org.jenkinsci.plugins.workflow.cps.CpsFlowDefinition" plugin="workflow-cps">
    <script>$(& $xmlEscape $script)</script>
    <sandbox>true</sandbox>
  </definition>
  <disabled>false</disabled>
</flow-definition>
"@
    $exists = (Invoke-Cli @('get-job', $name)).Code -eq 0
    $r = Invoke-Cli @($(if ($exists) { 'update-job' } else { 'create-job' }), $name) $xml
    if ($r.Code -ne 0) { throw "任務 $name 建立失敗：$($r.Output)" }
    Write-Host "$(if ($exists) { '已更新' } else { '已建立' })任務 $name → $JenkinsUrl/job/$name/"
}
