using System;
using System.IO;
using YooAsset;

namespace EntryScene
{
    /// <summary>
    /// CodePackage(HybridCLR 模組 dll)的封裝/解封裝演算法:**LZMA 壓縮 + XOR 混淆**。
    ///
    /// 打包端 <c>CodePackageEncryption</c>(Assets/Editor/ 底下,editor-build 負責)呼叫 <see cref="Pack"/>,
    /// 載入端 <see cref="CodePackageDecryption"/> 呼叫 <see cref="Unpack"/>。兩個方向刻意寫在
    /// 同一個類別、同一個檔案裡,改動時一定會同時看到對方。打包端在 Assembly-CSharp-Editor,
    /// 會自動參照本 assembly(EntryScene 的 asmdef autoReferenced 為 true),因此可以直接呼叫。
    ///
    /// ── 為什麼要壓縮 ────────────────────────────────────────────────
    /// YooAsset(3.0.6 已查證)的 ArchiveFileBuildPipeline 完全不壓縮(ArchiveFileBuildParameters 只有
    /// IncludePathInHash 與 FileAlignment 兩個欄位,沒有 CompressOption;ECompressOption 只出現在
    /// Legacy / Scriptable / InstantAsset 這幾條管線的參數裡,歸檔管線用不到)。
    /// dll 是壓縮率極高的資料,HPS 實測 16.46 MB → 4.31 MB(26%),不壓縮等於白付 12 MB。
    ///
    /// 壓縮必須在加密**之前**:XOR 之後的位元組已經失去可壓縮的規律,
    /// HPS 實測先加密再壓縮只剩 82%(幾乎沒有效果)。
    ///
    /// ── 為什麼可以這樣做而不用改 YooAsset ──────────────────────────
    /// 打包管線的順序是
    ///   TaskBuilding_AFBP → TaskEncryption_AFBP → TaskUpdateBundleInfo_AFBP → TaskCreateManifest_AFBP
    /// (3.0.6:ArchiveFileBuildPipeline.cs 第 36-39 行)
    /// TaskEncryption 把加密器的回傳另存成 .encrypt 檔,接著 TaskUpdateBundleInfo 做
    /// PackageSourceFilePath = EncryptedFilePath(3.0.6:TaskUpdateBundleInfo.cs 第 44 行),
    /// manifest 的 FileHash / FileCrc / FileSize
    /// 三個欄位**全部從加密後的檔案計算**,對加密器的輸出長度沒有任何限制。
    /// 因此壓縮對 YooAsset 是完全透明的:下載進度顯示的是壓縮後的真實傳輸量,
    /// manifest 記錄的 MD5 也就是 CDN 上那個最終檔案的 MD5。
    ///
    /// ── 加密的定位 ──────────────────────────────────────────────────
    /// 「防呆不防駭」:金鑰終究隨 App 出貨,拆包必得;目的只是讓 CDN 上的檔案
    /// 不是一眼可辨的 .NET 組件、避免無意間被抓去分析。真要防逆向得靠別的手段。
    /// </summary>
    public static class CodePackageCipher
    {
        // 多位元組輪轉金鑰。單一位元組 XOR 對有大量已知明文的檔案幾乎等於沒加密,
        // 多位元組成本一樣、至少不會一眼看穿。
        private static readonly byte[] Key =
        {
            0x5A, 0xC3, 0x11, 0x7E, 0xB6, 0x2D, 0x94, 0xF0,
            0x3B, 0xA8, 0x6C, 0xD5, 0x27, 0x81, 0xE9, 0x40,
        };

        /// <summary>容器識別碼,解封裝時先檢查。金鑰或格式不符會在這裡就報明確錯誤。</summary>
        private static readonly byte[] Magic = { (byte)'P', (byte)'C', (byte)'L', (byte)'1' };

        /// <summary>Magic(4) + 原始長度 int64(8) + LZMA properties(5)</summary>
        private const int HeaderSize = 4 + 8 + LzmaPropsSize;
        private const int LzmaPropsSize = 5;

        /// <summary>
        /// LZMA 字典大小。**解壓端會配置同樣大小的視窗**,所以這個值直接決定裝置端的
        /// 暫時記憶體用量。實測字典大小對本專案的壓縮率幾乎沒有影響:
        ///   1 MB → 4.35 MB、4 MB → 4.32 MB、8 MB 以上 → 4.31 MB
        /// (以上為 HPS 的實測數字。)用 1 MB 換掉 7 MB 的裝置端配置,只多付 40 KB 傳輸量,是明顯划算的取捨。
        /// (SDK 預設是 1&lt;&lt;22 = 4 MB,這裡刻意調低。)
        /// </summary>
        private const int DictionarySize = 1 << 20;

        /// <summary>
        /// 明文 → 壓縮 → XOR。打包端用。
        /// </summary>
        /// <param name="plain">原始位元組(不會被修改)</param>
        /// <returns>可直接落地上 CDN 的位元組</returns>
        public static byte[] Pack(byte[] plain)
        {
            if (plain == null)
                throw new ArgumentNullException(nameof(plain));

            var encoder = new PasSevenZip.Compression.LZMA.Encoder();
            encoder.SetCoderProperties(
                new[] { PasSevenZip.CoderPropID.DictionarySize },
                new object[] { DictionarySize });

            using (var output = new MemoryStream(plain.Length / 3))
            {
                output.Write(Magic, 0, Magic.Length);
                var lengthBytes = BitConverter.GetBytes((long)plain.Length);
                // BitConverter 在目標平台上都是小端(x86 / ARM),但寫死順序才不會被平台影響。
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);
                output.Write(lengthBytes, 0, lengthBytes.Length);

                // 5 bytes:lc / lp / pb 與字典大小。解壓端從這裡讀參數,
                // 所以打包端日後調 DictionarySize 也不會讓舊客戶端解不開。
                encoder.WriteCoderProperties(output);

                using (var input = new MemoryStream(plain, false))
                {
                    encoder.Code(input, output, plain.Length, -1, null);
                }

                var packed = output.ToArray();
                Obfuscate(packed);
                return packed;
            }
        }

        /// <summary>
        /// XOR → 解壓 → 明文。載入端用。
        /// </summary>
        /// <param name="packed">CDN 上的位元組;會就地被反混淆(呼叫端不應再使用原陣列)</param>
        /// <returns>原始位元組</returns>
        public static byte[] Unpack(byte[] packed)
        {
            if (packed == null)
                throw new ArgumentNullException(nameof(packed));
            if (packed.Length < HeaderSize)
                throw new InvalidDataException(
                    $"[CodePackageCipher] 資料長度 {packed.Length} 不足最小容器長度 {HeaderSize}");

            Obfuscate(packed);

            for (var i = 0; i < Magic.Length; i++)
            {
                if (packed[i] != Magic[i])
                    throw new InvalidDataException(
                        "[CodePackageCipher] 容器識別碼不符。金鑰不一致,或這份檔案是舊格式" +
                        "(未壓縮的純 XOR)——打包端與客戶端版本必須配對。");
            }

            var lengthBytes = new byte[8];
            Array.Copy(packed, Magic.Length, lengthBytes, 0, 8);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);
            var plainLength = BitConverter.ToInt64(lengthBytes, 0);
            if (plainLength < 0 || plainLength > int.MaxValue)
                throw new InvalidDataException($"[CodePackageCipher] 原始長度不合理: {plainLength}");

            var props = new byte[LzmaPropsSize];
            Array.Copy(packed, Magic.Length + 8, props, 0, LzmaPropsSize);

            var decoder = new PasSevenZip.Compression.LZMA.Decoder();
            decoder.SetDecoderProperties(props);

            // 預先配置正確大小的輸出緩衝,避免 MemoryStream 反覆倍增造成多餘的 GC。
            var plain = new byte[plainLength];
            using (var input = new MemoryStream(packed, HeaderSize, packed.Length - HeaderSize, false))
            using (var output = new MemoryStream(plain, true))
            {
                decoder.Code(input, output, input.Length, plainLength, null);
                if (output.Position != plainLength)
                    throw new InvalidDataException(
                        $"[CodePackageCipher] 解壓長度不符:預期 {plainLength}、實得 {output.Position}");
            }

            return plain;
        }

        /// <summary>
        /// 就地 XOR(自身即反運算)。除了輪轉金鑰外再混入位置項,
        /// 避免整份檔案呈現「固定週期 XOR」的明顯規律。
        /// </summary>
        private static void Obfuscate(byte[] data)
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] ^= (byte)(Key[i % Key.Length] ^ (byte)(i * 31));
            }
        }
    }

    /// <summary>
    /// CodePackage 的歸檔資源包(ArchiveBundle)解密器。
    ///
    /// YooAsset 3.0.6 的 ArchiveBundle 載入路徑**只接受 <see cref="IBundleMemoryDecryptor"/>**
    /// (見 LoadLocalArchiveBundleOperation.cs 第 68-78 行,不是記憶體式解密器會直接 SetError),
    /// 所以這裡是整檔進記憶體解密,不能用 offset 或 stream 式。dll 總共才十幾 MB,
    /// 記憶體與耗時都無虞。
    ///
    /// 掛載方式見 <c>CodePackageLoader</c>:
    /// <c>cacheFileSystemParams.AddParameter(EFileSystemParameter.ArchiveBundleDecryptor, new CodePackageDecryption())</c>
    /// ⚠️ 參數鍵是 <c>ArchiveBundleDecryptor</c>,掛到 <c>RawBundleDecryptor</c> 上
    /// 既不生效也不報錯——要到載入時才會失敗。
    /// </summary>
    public sealed class CodePackageDecryption : IBundleMemoryDecryptor
    {
        byte[] IBundleMemoryDecryptor.GetDecryptedData(BundleDecryptArgs args)
        {
            // ArchiveBundle 走本地快取時 args.FileData 一定是 null——YooAsset 在
            // LoadLocalArchiveBundleOperation.LoadFromMemory() 內以
            // new BundleDecryptArgs(bundle, null, filePath) 建構(3.0.6 第 127 行),第二個參數硬寫 null,
            // 檔案內容要解密器自己讀。只有 Web 情境才會預先帶入 FileData。
            //
            // 官方範例(Samples~/Test Sample/Runtime/CryptoHelpers/TestArchiveBundleDecryptor.cs)此處用的 FileUtility
            // 是 YooAsset 的 internal 型別,本 assembly
            // (EntryScene,不在 YooAsset 的 InternalsVisibleTo 清單)取不到,改用 System.IO.File。
            // ⚠️ args.FileData 非 null 時要先複製一份再交出去:Unpack 會**就地**反混淆傳進去
            // 的陣列,而那個 buffer 的所有權在 YooAsset 手上,不是我們的。就地改寫別人的
            // buffer 之後,任何重試或再次使用都會拿到被 XOR 過一次的內容——而 XOR 是自身的
            // 反運算,第二次剛好還原成密文,magic 比對失敗、報出「金鑰不一致,或這份檔案是舊
            // 格式」,把人指往一個完全錯誤的方向。
            // 自己 File.ReadAllBytes 出來的那份是本地新配置的,就地改寫沒有問題,不必複製。
            var data = args.FileData != null
                ? (byte[])args.FileData.Clone()
                : File.ReadAllBytes(args.FilePath);

            // 回傳的必須是「原始的歸檔位元組」——YooAsset 拿去解析 YARK 檔頭與索引表。
            // Unpack 會就地反混淆 data 再解壓成新陣列,所以 data 之後不可再使用。
            return CodePackageCipher.Unpack(data);
        }
    }
}
