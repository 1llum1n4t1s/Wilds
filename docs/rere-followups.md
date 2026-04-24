# rere レビューのフォローアップ (次セッション持ち越し)

2026-04-24 の `/rere` セキュリティ / 品質 / パフォーマンス / 設計レビューで採用した
40 件のうち、Bundle 1〜7 + 9 + 11 で 26 件を適用済み。残る 14 件の取り扱いを記録する。

修正済みコミット: `51654a2c6..3d2xxx` (Bundle 1〜9, 11)

---

## 🟡 次セッションで実装したい (実行可能・範囲大)

### Bundle 8: CLAUDE.md 規約違反 (#8 System.IO 直書き)

対象ファイル:
- `src/Wilds.App/Utils/Storage/StorageItems/ZipStorageFolder.cs` (8 箇所)
- `src/Wilds.App/Helpers/Navigation/NavigationHelpers.cs` (2 箇所)
- `src/Wilds.App/Utils/Storage/StorageItems/FtpStorageFolder.cs` (3 箇所)
- `src/Wilds.App/ViewModels/Properties/SecurityAdvancedViewModel.cs` (1 箇所)
- `src/Wilds.App/Utils/Storage/Helpers/StorageHelpers.cs` (1 箇所)
- その他 `System.IO.Path.` 直書き ~18 箇所

適用方針:
1. `using System.IO;` は削除し、`SystemIO.Path.Combine(...)` 等のエイリアスに統一
2. `using System.IO.Hashing;` / `using System.IO.Pipes;` などサブ名前空間は対象外
3. 将来的に Roslyn Analyzer で静的に強制するルールを `Wilds.Core.SourceGenerator` に追加検討

### Bundle 10: FTP 秘密情報 (#12 PasswordVault 移行)

`src/Wilds.App.Storage/Ftp/FtpManager.cs:10` の `static Dictionary<string, NetworkCredential>`
をプロセスメモリ平文保持から `Windows.Security.Credentials.PasswordVault` (DPAPI 保護) に
置換する。

`src/Wilds.App/Utils/Cloud/CredentialsHelpers.cs` に既に PasswordVault ラッパーがあるので
それを再利用。セッション終了時にメモリから消える現在の挙動は維持する (毎接続時に
PasswordVault から読む / 書く設計)。

### Bundle 12: 新規インフラ単体テスト (#31)

`Wilds.Shared/Helpers/LruCache.cs` と `Wilds.App/Helpers/AsyncDebouncer.cs` は WinUI 非依存の
ピュアロジックなので `tests/Wilds.Unit/` (MSTest) を新規作成して単体テストを追加する。

- LruCache: 容量制限 / LRU 順序 / eviction コールバック / 並列 GetOrAdd / Clear
- AsyncDebouncer: 基本デバウンス / インターバル超過 / Cancel / Dispose 後の動作 /
  並列 Trigger の冪等性
- STAThreadPool は MSTest で WinUI 依存せず動く想定。ただし STA 起動の検証は `Thread.CurrentThread.GetApartmentState()` アサートだけでほぼ十分。

---

## 🟠 要設計判断 (別 PR で議論が必要)

### #22 Ioc.Default のフィールド初期化子呼び出しをコンストラクタ注入に

`ShellViewModel.cs:52-62` で 10+ サービスがフィールド初期化子で `Ioc.Default.GetRequiredService<T>()` を
呼んでいる。DI 構築前に `new ShellViewModel(...)` した場合に `InvalidOperationException` が
投げられるリスク + テスト困難。

**議題**: `ShellViewModel` のコンストラクタ引数に 10+ サービスを並べるか、`IShellViewModelServices`
ファサードを作るか。ファサード作成時は依存関係・ライフタイムの見直しが必要。

### #23 `App.Logger = null!` を DI に移行

`App.xaml.cs:43` の `public static ILogger Logger { get; private set; } = null!;` が 58 ファイル
157 箇所から参照されている。早期フェーズで例外が起きて `App.Logger?.Log...` せずに直接呼ぶと
NRE になる。全て `?.` 化するか、DI へ移行するか。

**議題**: 短期は `App.Logger.Log...` 直書きを全 `?.` 化 (小さな機械的変更)。
長期は DI 化 (大きな設計変更)。どちらを先にやるか。

### #24 `STAThreadPool.Func<Task<T>>` overload を Obsolete / doc

コメントに「WinRT async を await してはいけない」と書いても実装上の契約だけで機械的に
防げない。`[Obsolete]` に格下げするか、overload 自体を削除して非 async の `Func<T>` だけに
制限するか。

**議題**: 現状の呼び出し元が本当に async overload を必要としているか (grep 結果では 0 件)
を精査し、必要なら `[Obsolete]` で警告を出すだけに留める。

### #25 STA プール 2 本の IFileOperation 飢餓

`IFileOperation.PerformOperations()` が長時間 (数分) 走ると STA プール 2 本のうち 1 本が
長期占有され、軽量サムネイル取得がキュー詰まりする。

**議題**: プール分離 (軽量用 2 本 + 重操作用 1 本) vs CancellationToken 伝播 + UI キャンセル vs
`IFileOperation.PerformOperations` の非同期化。トレードオフ比較が必要。

### #9 WinAppDriver → Unpackaged 対応

`tests/Wilds.InteractionTests/SessionManager.cs:17-21` が旧 MSIX パッケージ ID
(`FilesDev_ykqwq8d6ps0ag!App` 等) を使っており、Unpackaged 配布への完全移行で
**現時点で E2E テストが実行不能**。

**議題**: WinAppDriver の `app` capability を `%LOCALAPPDATA%\Wilds\...\Wilds.exe` へ
切り替えるだけで動くか、CI 環境 (velopack-release.yml) でのテスト実行方針と
連動させるか。

### D I-4: upstream フォーク戦略

`--ff-only upstream/main` で upstream を取り込み続ける運用は、MSIX 廃止 +
リブランド + Velopack 導入で divergence が指数的に拡大している現状では次回の
upstream 変更時に破綻しうる。

**議題**:
1. divergence の多い C# ファイルを "owned by Wilds" として明示リストアップし merge 手順書化
2. Velopack 固有コードを `#if VELOPACK` / 部分クラス / ファイル分割で isolate
3. `merge --no-ff -X ours` 運用 (mergetool 設定込み)
4. そもそも upstream 追従をやめる (フルフォーク化)

### #32 `Wilds.App.Server/Program.cs` パスハードコード

`Server/Program.cs:16-17` が `%LOCALAPPDATA%\Wilds\Local` をマジック文字列で組み立てる。
`WildsAppInfo.PackageName` / `AppPaths` を使いたいが、`Wilds.App.Server` が
`Wilds.Shared` / `Wilds.App` を参照できない設計上の制約がある。

**議題**: `Wilds.App.Server` から参照可能な位置 (`Wilds.Shared.Helpers` 等) に
`PackageName` 定数を移動するか。
