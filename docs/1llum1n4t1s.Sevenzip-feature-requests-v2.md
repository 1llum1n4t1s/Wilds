# 1llum1n4t1s.Sevenzip 追加要望（v2）

**対象ライブラリ:** [1llum1n4t1s.Sevenzip 1.0.68](https://www.nuget.org/packages/1llum1n4t1s.Sevenzip/) (`Cube.FileSystem.SevenZip` 名前空間)

**前回調査:** [1llum1n4t1s.Sevenzip-feature-requests.md](./1llum1n4t1s.Sevenzip-feature-requests.md) (2026-04-19)

**再調査日:** 2026-04-19（1.0.68 反映）

**結論:** v1 で挙げた **Critical / High の大半が 1.0.66 までに実装済み**、残 5 件 (rename 直接 API / VolumeSize / AsyncPasswordQuery / IsUnicodeText / ZipArchiveEntity) は 1.0.67〜1.0.68 ですべて取り込み済み。Wilds 側の全面移行も完了。

---

## ✅ v1 から解決済みの機能（記録用・1.0.66 時点）

| # | 機能 | 実装箇所 |
|---|---|---|
| 1 | Stream ベース全 API | `ArchiveReader(Stream, …)` 6 オーバーロード / `ArchiveWriter.Save(Stream)` / `ArchiveWriter.Add(Stream, name)` |
| 2 | 既存アーカイブの Format 自動検出 | `FormatFactory.From(Stream)` |
| 3 | in-place 編集（追加・置換・削除） | `ArchiveWriter.Update(Stream, Stream, sourcePassword, …)` + `Remove(string)` |
| 4 | Stream からの圧縮 | `Add(Stream, name)` + `Save(Stream)` |
| 5 | 単一エントリのストリーム抽出 | `ArchiveReader.Extract(int index, Stream output, IProgress<Report>)` / 辞書版 |
| 6 | per-file 進捗イベント | `FileCompressing` / `FileCompressed` / `FileExtracting` / `FileExtracted` + `ArchiveFileEventArgs.Target/Index/Count/TotalCount/Cancel` |
| 7 | 同期エントリ列挙 | `ArchiveReader.Items : IReadOnlyList<ArchiveEntity>` |
| 10 | CustomParameters | `CompressionOption.CustomParameters : IDictionary<string, string>` |
| 13 | IncludeEmptyDirectories | `CompressionOption.IncludeEmptyDirectories` |
| 14 | エンコーディング指定 | `ArchiveOption.Encoding` + `ArchiveOption.CodePage`（CP437 / Shift_JIS 等任意） |

---

## ✅ 1.0.67〜1.0.68 で追加解決した機能（v2 残要望の全消化）

| 旧 # | 機能 | 1.0.67〜1.0.68 実装箇所 |
|---|---|---|
| 要望 1 | **rename 直接 API** | `ArchiveWriter.Update(Stream, Stream, IReadOnlyDictionary<int, string> renameMap, …)` (value=null で削除) |
| 要望 2 | **VolumeSize（分割書き出し）** | `CompressionOption.VolumeSize: long` + `Internal/VolumeArchiveStreamWriter.cs` |
| 要望 3 | **async PasswordRequest** | `AsyncPasswordQuery : IQuery<string>`（`Func<CancellationToken, Task<string>>` 受付） |
| 要望 4 | **ZIP Unicode フラグ検出** | `ArchiveEntity.IsUnicodeText: bool` + `Entity.RawName` |
| 要望 5 | **ZIP low-level 情報** | `ZipArchiveEntity : ArchiveEntity` (`GeneralPurposeBitFlag` / `ExtraField`) |

---

## 📊 Wilds 側の移行状況

- NuGet ローカル配置: `src/Wilds.App/Assets/Libraries/1llum1n4t1s.Sevenzip.1.0.68.nupkg`
- `SevenZipSharp 1.0.2` + `SharpZipLib 1.4.2` を完全撤去、1llum1n4t1s.Sevenzip 1 本化
- Archive 処理は `ArchiveUpdateHelper` (新規) 経由で atomic rename + DRY 抽出完了
- `rename/delete/create` は `Update(renameMap)` + `Add(Stream, name)` で 1 本化、MemoryStream → 一時ファイル化で OOM 回避

---

## 🔗 参考

- v1 要望書: [1llum1n4t1s.Sevenzip-feature-requests.md](./1llum1n4t1s.Sevenzip-feature-requests.md)
- リポジトリ: https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip
- NuGet: https://www.nuget.org/packages/1llum1n4t1s.Sevenzip/
