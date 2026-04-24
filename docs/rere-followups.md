# rere レビューのフォローアップ (残課題のみ)

2026-04-24 の `/rere` レビューで採用した 40 件のうち、以下のコミットで多数を適用済み:

- **2026-04-24**: Bundle 1〜9 + 11 (26 件)
- **2026-04-25**: Bundle 8 + 10 + 12 + 13 (10+ 件)

残るは **要設計判断 4 件のみ**。

---

## 🟠 要設計判断 (別 PR で議論が必要)

### #22 Ioc.Default のフィールド初期化子呼び出しをコンストラクタ注入に

`ShellViewModel.cs:52-62` で 10+ サービスがフィールド初期化子で
`Ioc.Default.GetRequiredService<T>()` を呼んでいる。DI 構築前に `new ShellViewModel(...)`
した場合に `InvalidOperationException` が投げられるリスク + テスト困難。

**議題**: `ShellViewModel` のコンストラクタ引数に 10+ サービスを並べるか、
`IShellViewModelServices` ファサードを作るか。ファサード作成時は依存関係・
ライフタイムの見直しが必要。

**ステータス**: 未着手 (大規模リファクタのため、新機能開発と同時に進める方針)。
Bundle 13 で `App.Logger` 直書きを `?.` 化したことで NRE リスクは緩和されたが、
他の `Ioc.Default.GetRequiredService<T>()` の早期呼び出しリスクは残る。

### #25 STA プール 2 本の IFileOperation 飢餓

`IFileOperation.PerformOperations()` が長時間 (数分) 走ると STA プール 2 本のうち 1 本が
長期占有され、軽量サムネイル取得がキュー詰まりする。

**議題**: プール分離 (軽量用 2 本 + 重操作用 1 本) vs CancellationToken 伝播 +
UI キャンセル vs `IFileOperation.PerformOperations` の非同期化。
トレードオフ比較が必要。

**ステータス**: 未着手 (実害が報告されてから優先度上げる)。

### #9 WinAppDriver → Unpackaged 対応

`tests/Wilds.InteractionTests/SessionManager.cs:17-21` が旧 MSIX パッケージ ID
(`FilesDev_ykqwq8d6ps0ag!App` 等) を使っており、Unpackaged 配布への完全移行で
**現時点で E2E テストが実行不能**。

加えて `WindowsElement` / `WindowsDriver<T>` などの API が新しい Appium.WebDriver で
非ジェネリック化されており、テストコード自体がビルドできなくなっている。

**議題**: WinAppDriver の `app` capability を `%LOCALAPPDATA%\Wilds\...\Wilds.exe` へ
切り替えるか、Microsoft.Playwright + Windows extension など別フレームワークへの
移行検討。CI 環境 (velopack-release.yml) でのテスト実行方針と連動させるか。

**ステータス**: ビルドエラー状態だが Wilds.slnx ビルドは Wilds.App / Wilds.Unit のみ
通せば実用上の問題なし。E2E テスト復活は機能開発のフェーズに合わせて。

### D I-4: upstream フォーク戦略

`--ff-only upstream/main` で upstream を取り込み続ける運用は、MSIX 廃止 +
リブランド + Velopack 導入で divergence が指数的に拡大している現状では次回の
upstream 変更時に破綻しうる。

**議題**:
1. divergence の多い C# ファイルを "owned by Wilds" として明示リストアップし
   merge 手順書化
2. Velopack 固有コードを `#if VELOPACK` / 部分クラス / ファイル分割で isolate
3. `merge --no-ff -X ours` 運用 (mergetool 設定込み)
4. そもそも upstream 追従をやめる (フルフォーク化)

**ステータス**: 未着手 (次回 upstream merge 試行時に方針決定)。
