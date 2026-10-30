# Jenkins 自動出包

本機 Jenkins（http://127.0.0.1:8080/）上有兩個任務：

| 任務 | 平台 | 專案路徑 |
|---|---|---|
| `blender-WebGL` | WebGL | `D:\Claude\blender` |
| `blender-Android` | Android | `D:\Claude\blender_android` |

兩個平台用不同的專案資料夾，切平台要重新匯入，很慢。

## 參數（三個勾選）

| 勾選 | 做什麼 |
|---|---|
| `BUILD_FULL` 完整出包 | 代碼包（重產 AOT dll）＋資源包＋主體。WebGL 的網頁本體上傳 GCS `blender/WebGL/web/`；Android 的 APK 複製到 `D:\Claude\UnityOutput\APK`，資料夾不存在會自動建立。勾了這個，另外兩個不看 |
| `BUILD_ASSET` 資源包 | 打資源包並上傳 |
| `BUILD_CODE` 代碼包 | 打代碼包並上傳。沿用上次完整出包的 AOT dll，只適用主體沒變的時候（Entry、link.xml 沒改） |

- 資源包和代碼包預設都勾。兩個都勾時兩個都會打，但不出主體。
- 三個都沒勾，第一步就會失敗，不會動到專案。

## 流程

1. **清空改動並同步 main**：`git reset --hard`、`git clean -fd`、切到 main、`git merge --ff-only origin/main`。
   - ⚠️ 專案裡沒 commit 的改動會被清掉。
   - 本機 main 有還沒推的 commit 時，這步會失敗停下，不會把 commit 丟掉。
   - 要出包的程式碼要先推上 origin/main。
2. **同步 GCS 版號**：把 `ServerData/<平台>/<bundleVersion>/<套件>/<套件>.version` 指針改成 GCS 上的版號。版號由指針加 1 配號，這樣不同機器出包才不會撞號。
3. **Unity 打包**：用 batchmode 跑 `CIBuild.Run`（`Assets/Editor/CI/CIBuild.cs`）。
   - 專案的 Unity 開著時會直接失敗，要先關掉。
   - 完整 log 存成建置產物 `unity.log`。
4. **上傳 bundle**：上傳順序是 bundle、這一版的清單，最後才傳 `.version` 指針。GCS 上的舊版保留，不刪除。
5. **發佈主體**（只有完整出包才跑）：WebGL 先傳 `Build/`、`TemplateData/`，最後傳 `index.html`；Android 的 APK 檔名會帶版號與時間。

Jenkins 同時只跑一個建置，兩個任務會排隊。

## 安裝與維護

```powershell
powershell -ExecutionPolicy Bypass -File Tools\Jenkins\install-jenkins.ps1   # 下載 JRE、jenkins.war 到 D:\Jenkins，設定登入自動啟動並啟動
powershell -ExecutionPolicy Bypass -File Tools\Jenkins\setup-jobs.ps1        # 裝 Pipeline 外掛，建立或更新兩個任務
```

- Jenkins 用目前登入的 Windows 帳號執行，所以會沿用這個帳號的 Unity 授權、gcloud 登入（要能寫 `gs://peter_hybridclr_test`），以及 git 的 SSH 金鑰。
- Jenkins 只聽 127.0.0.1，不設帳號。
- 自動啟動用「啟動」資料夾的 `Jenkins.lnk`，執行 `D:\Jenkins\start-jenkins.ps1`。
- 改了 `Jenkinsfile` 或任務參數，要重跑 `setup-jobs.ps1`。不要直接在 Jenkins 網頁上改任務。
- 步驟細節與可覆寫的環境變數（`CI_GCS_ROOT`、`CI_APK_OUTPUT`、`UNITY_EXE`）見 `ci.ps1` 開頭的說明。
