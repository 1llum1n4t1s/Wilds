# 1llum1n4t1s.Sevenzip に欲しい機能リスト

**対象ライブラリ:** [1llum1n4t1s.Sevenzip 1.0.66](https://www.nuget.org/packages/1llum1n4t1s.Sevenzip/) (`Cube.FileSystem.SevenZip` 名前空間)

**調査対象:** [Wilds](https://github.com/1llum1n4t1s/Wilds) (files-community/Files フォーク) 内の既存アーカイブ処理

**調査日:** 2026-04-19

**結論:** SevenZipSharp / SharpZipLib からの置換には、ストリームベース API・同期的エントリ列挙・per-file 進捗イベントが不足していて、現状そのままでは互換シムなしに差し替え不可。

---

## 🚨 Critical — これが無いと致命的な機能

アーカイブを**ファイルではなくストリームとして扱う**ユースケース (= アーカイブ内のサブフォルダをブラウジングしながら直接編集する) に対応できない。

| # | 機能 | 現コード使用箇所 | Cube での状況 |
|---|---|---|---|
| **1** | **`Stream` ベースの全 API** | `ZipStorageFile.cs` / `ZipStorageFolder.cs` (アーカイブブラウジング全般) | ❌ `ArchiveReader`/`ArchiveWriter` は `string path` のみ受け付け、メモリ内操作不可 |
| **2** | **`SetFormatFromExistingArchive(Stream)`** | `ZipStorageFolder.cs` 6 箇所 | ❌ 既存アーカイブのフォーマット自動検出不可 |
| **3** | **`ModifyArchiveAsync(stream, Dictionary<int, string> renameMap, password, outStream)`** | `ZipStorageFile.cs` / `ZipStorageFolder.cs` (エントリの rename/delete) | ❌ `Update()`/`Remove()` はファイルパス前提で、in-place modification 不可 |
| **4** | **`CompressStreamDictionaryAsync(Dictionary<string, Stream>, password, outStream)`** | `ZipStorageFolder.cs` (ストリームからの圧縮) | ❌ ディスク経由が必須 |
| **5** | **`ExtractFile(int index, Stream outputStream)`** | `ZipStorageFile.cs` (単一エントリのストリーム抽出) | ❌ 存在しない |

### 具体例 (現行コード)

```csharp
// ZipStorageFolder.cs:319 — 新規エントリ追加
compressor.SetFormatFromExistingArchive(archiveStream);
await compressor.CompressStreamDictionaryAsync(
    archiveStream,
    new Dictionary<string, Stream>() { { fileName, null } },
    Credentials.Password,
    ms);

// ZipStorageFile.cs:332 — エントリ rename
compressor.SetFormatFromExistingArchive(archiveStream);
await compressor.ModifyArchiveAsync(
    archiveStream,
    new Dictionary<int, string>() { { index, fileName } },
    Credentials.Password,
    ms);
```

---

## 🟡 High — UI に影響する機能

イベント駆動の per-file 進捗と同期的エントリ列挙。WinUI の `x:Bind` と StatusCenter (per-file progress UI) に直結する。

| # | 機能 | 用途 | Cube での状況 |
|---|---|---|---|
| **6** | **イベント駆動進捗**<br>`FileExtractionStarted` / `FileExtractionFinished` / `FileCompressionStarted` / `Extracting` / `Compressing` | StatusCenter の per-file 進捗表示 (どのファイルを処理中かリアルタイム表示) | ⚠️ `IProgress<Report>` は存在するが **現在処理中のファイル名** を取れる粒度ではない |
| **7** | **`ArchiveFileData` 同期列挙プロパティ**<br>(`IReadOnlyCollection<ArchiveFileInfo>`) | **35 箇所** で使用<br>エントリの `FileName` / `Size` / `IsDirectory` / `CreationTime` / `LastWriteTime` / `Index` を同期参照 | ❌ `ArchiveCollection` は Enumerable / async が主で、WinUI の同期バインドに不向き |
| **8** | **`PasswordRequest` イベント** (抽出中にパスワード要求) | `PasswordRequestedCallback` との接続で、暗号化検出時に UI の ContentDialog を出す | ⚠️ `Query<string>` パターン。async UI dialog とつなぎ直しが必要 |
| **9** | **`ExtractArchiveAsync(destPath)`** (全展開 + 進捗) | `StorageArchiveService.cs:167` | ⚠️ `Save(dest, progress)` は同期 (ブロッキング)。スレッド分離が必須 |

### 使用統計 (ArchiveFileInfo プロパティ)

```
23  .FileName          ← エントリ名参照
14  .Index             ← ModifyArchiveAsync の renameMap キー
10  .FirstOrDefault()  ← LINQ でエントリ検索
 8  .CreationTime      ← UI 表示
 7  .Size
 4  .LastWriteTime
 3  .IsDirectory
 2  .Where()
```

---

## 🟢 Medium — 圧縮オプションの柔軟性

SevenZipSharp の `CustomParameters` は 7z.exe の低レベルパラメータを直接叩けるのが強み。Cube の enum ベースでは細かい制御ができない。

| # | 機能 | 用途 | Cube での状況 |
|---|---|---|---|
| **10** | **`CustomParameters` 任意キー注入**<br>(`Dictionary<string, string>`) | `mt=<N>` (CPU 数) / `cu=on` (UTF-8 ZIP) / `d=<size>` (辞書サイズ) / `fb=<N>` (LZMA word size) | ❌ 固定 enum のみ (`CompressionLevel` / `CompressionMethod`) |
| **11** | **`VolumeSize` プロパティ** (ボリューム分割) | 7z 分割書き出し (`CompressArchiveModel.cs:163` — `SplittingSize` 設定) | ⚠️ 未確認。`CompressionOption` に公開されているか検証が必要 |
| **12** | **`PreserveDirectoryRoot`** | 複数ソース時にルートディレクトリを保持するか | ❌ 挙動不明 |
| **13** | **`IncludeEmptyDirectories`** | 空ディレクトリをアーカイブに含めるか | ❌ 存在しない |

### 具体例 (現行コード)

```csharp
// CompressArchiveModel.cs:170
compressor.CustomParameters.Add("mt", CPUThreads.ToString());  // マルチスレッド数
compressor.CustomParameters.Add("cu", "on");                   // ZIP の UTF-8 強制
compressor.CustomParameters.Add("d", dictParam);               // LZMA 辞書サイズ
compressor.CustomParameters.Add("fb", wordParam);              // LZMA word size
```

---

## 🔵 SharpZipLib 固有 — 別ライブラリ扱い、Cube では完全に無い

Legacy Windows ZIP (Shift_JIS / CP437) の文字化け対策に `SharpZipLib` を使っているが、Cube には相当機能なし。

| # | 機能 | 用途 | Cube での状況 |
|---|---|---|---|
| **14** | **`ZipFile(path, StringCodec.FromEncoding(cp437))`** | CP437 / Shift_JIS で書かれた legacy Windows ZIP を正しい文字コードで開く | ⚠️ `CodePage.Oem` / `CodePage.Japanese` はあるが `StringCodec` 相当の柔軟性なし |
| **15** | **`ZipEntry.IsUnicodeText`** 判定 | Unicode フラグで文字化け検出 → CP437 で再オープンするヒューリスティック | ❌ 存在しない |
| **16** | **`ZipEntry` 直接操作** (flag / extra field 参照) | 文字化けヒューリスティック / generic file attributes | ❌ `ArchiveEntity` は抽象化レイヤが厚く low-level flags 参照不可 |

### 具体例 (現行コード)

```csharp
// StorageArchiveService.cs:210 — 文字化け検出付き ZIP 読み取り
using var zipFile = new ZipFile(archiveFilePath, StringCodec.FromEncoding(encoding));
if (!zipFile.Cast<ZipEntry>().All(entry => entry.IsUnicodeText))
{
    // Unicode フラグが立ってない entry があれば CP437 で再オープン
    using var zipFile2 = new ZipFile(archiveFilePath, StringCodec.FromEncoding(cp437));
    // ...
}
```

---

## 📊 影響範囲サマリ

| ファイル | 主要使用機能 | 書き換え難度 |
|---|---|---|
| `CompressArchiveModel.cs` | #6, #10, #11 | High (`CustomParameters` 互換が必須) |
| `StorageArchiveService.cs` | #6, #9, #14, #15 | High (SharpZipLib + SevenZipSharp 混在) |
| `ZipStorageFile.cs` | #1, #2, #3, #5, #8 | **Very High** (ストリーム API 前提で設計) |
| `ZipStorageFolder.cs` | #1, #2, #3, #4, #7 | **Very High** (同上) |
| `ArchivePreviewViewModel.cs` | #7 | Medium |
| `CreateArchiveDialog.xaml.cs` | enum 型のみ | Low |
| `CompressIntoArchiveAction.cs` / `BaseDecompressArchiveAction.cs` | enum 型のみ | Low |
| `ICompressArchiveModel.cs` / `IStorageArchiveService.cs` / `IPasswordProtectedItem.cs` | enum / interface | Low |

---

## 🎯 優先度提案

もし 1llum1n4t1s.Sevenzip に機能追加するなら、以下の順で実装されれば Wilds 互換シムで吸収可能:

1. **#1–#5 (Stream API)** — これが無いとどうしようもない。`ArchiveReader(Stream, password, opts)` と `ArchiveWriter.Save(Stream)` / `Update(Stream, Stream)` の追加
2. **#6 (per-file progress event)** — `event EventHandler<ArchiveProgressEventArgs> FileStarted/FileFinished` を追加、または `IProgress<Report>` の `Report` に `CurrentFileName` フィールドを足す
3. **#7 (`ArchiveFileData` 同期プロパティ)** — `ArchiveReader.Entries : IReadOnlyList<ArchiveEntity>` を公開
4. **#10 (`CustomParameters`)** — `CompressionOption.CustomParameters : IDictionary<string, string>` を追加 (7z.exe パススルー)
5. **#8 (`PasswordRequest` イベント)** — 現 `Query<string>` と並行で `event EventHandler<PasswordRequestEventArgs>` を追加
6. **#14 (encoding 指定)** — `ArchiveReader(path, Encoding zipEntryEncoding, ...)` オーバーロード追加

これらが揃えば、`SevenZipSharp` / `SharpZipLib` 完全置換 + 互換シム層 2〜300 行程度で Wilds に乗せられる見込みです💕
