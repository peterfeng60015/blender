# SevenZipLzma —— 7-Zip LZMA SDK（C#）

CodePackage 的歸檔壓縮用。`CodePackageCipher.Pack()` / `Unpack()` 是唯一的呼叫端，
其餘程式碼不該直接碰 `PasSevenZip.*`。

## 來源

檔案原封不動取自 HybridCLR **8.13.0** 套件內附的同一份 SDK（`Editor/3rds/7zip/`），
經由 HPS 專案 `Assets/Entry/SevenZipLzma/` 複製過來，內容與 HPS 逐位元組相同。

**與上游的差異只有一行**：根命名空間 `SevenZip` → `PasSevenZip`。

⚠️ 本專案用的 HybridCLR **9.0.3 已不再內附** 7zip（`Editor/3rds/` 只剩 `UnityHook`），
所以無法在本專案的 `Library/PackageCache` 裡直接 `diff`。要比對時拿 HPS 的
`Library/PackageCache/com.code-philosophy.hybridclr@*/Editor/3rds/7zip/`（8.13.0）：

```bash
H=<HPS 專案>/Library/PackageCache/com.code-philosophy.hybridclr@*/Editor/3rds/7zip
diff -r "$H" Assets/Entry/SevenZipLzma --exclude=README.md --exclude='*.meta' \
     --exclude=CommandLineParser.cs
# 預期：每個檔案只有 namespace 那一行不同
```

## 為什麼要改命名空間

HPS（HybridCLR 8.13.0）裡，HybridCLR 那份 SDK 在 `HybridCLR.Editor` assembly
（`autoReferenced: true`、Editor-only），Assembly-CSharp-Editor 看得到它。若這份副本沿用
`SevenZip`，`SevenZip.Compression.LZMA.Encoder` 在 Editor 端會變成模稜兩可的型別參照而編譯失敗。
HybridCLR 9.0.3 雖然已移除那份 SDK，命名空間仍沿用 `PasSevenZip`：與 HPS 一致，
也避免日後 HybridCLR 或其他套件再帶進 `SevenZip` 時撞名。

## 為什麼要自己留一份，不直接用 HybridCLR 那份

那份是 Editor-only，**執行期拿不到解壓器**。而且它在別人的套件裡，
HybridCLR 升版隨時可能移除或改路徑（9.0.3 就已經移除了）。

## 未收錄的檔案

`Common/CommandLineParser.cs`（274 行）—— 只有 SDK 附的命令列範例會用到，
本專案不需要。其餘 14 個檔案全部收錄（含 `Common/OutBuffer.cs`，
雖然目前沒有呼叫端，但保留才能維持與上游逐檔對應）。

## 授權

7-Zip LZMA SDK 由 Igor Pavlov 釋出至 **public domain**，可無條件使用與修改。
