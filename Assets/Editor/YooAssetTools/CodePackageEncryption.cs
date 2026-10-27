using System.IO;
using EntryScene;
using YooAsset;

/// <summary>
/// CodePackage 打包時的歸檔資源包加密器（YooAsset 3.0.6 的 <see cref="IBundleEncryptor"/>）。
///
/// 演算法本體在 <see cref="CodePackageCipher"/>（Assets/Entry/CodePackageDecryption.cs），
/// 與載入端共用同一份，不存在「兩端演算法不一致」這種故障模式。
///
/// 掛載方式：<c>ArchiveFileBuildParameters.BundleEncryptor = new CodePackageEncryption()</c>
/// （見 CodePackageBuildTool）。3.0.6 的 ArchiveFileBuildPipeline 把 TaskEncryption_AFBP 排在
/// TaskUpdateBundleInfo_AFBP 之前，後者對 PackageSourceFilePath（加密後的檔案）重算
/// FileHash（MD5）、FileCRC、FileSize，**所以 manifest 記錄的都是「加密後」內容的值**，
/// 回傳長度與輸入不同是允許的。
/// </summary>
public sealed class CodePackageEncryption : IBundleEncryptor
{
    public BundleEncryptResult Encrypt(BundleEncryptArgs args)
    {
        // CodePackage 裡除了 HybridCLR 的 dll 歸檔沒有別的東西，一律加密即可，不必按 BundleName 過濾。
        var fileData = File.ReadAllBytes(args.FilePath);
        // Pack = LZMA 壓縮 + XOR
        return new BundleEncryptResult(true, CodePackageCipher.Pack(fileData));
    }
}
