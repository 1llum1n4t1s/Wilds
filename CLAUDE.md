# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

**Wilds** (Windows 用モダンファイルマネージャ) の個人フォーク。upstream は `files-community/Files`。WinUI 3 + Windows App SDK + .NET 10 の Windows 専用デスクトップアプリで、**Unpackaged (非 MSIX) + Velopack 自動更新**として配布される。

- `origin` = `1llum1n4t1s/Wilds` (このフォーク、push 可)
- `upstream` = `files-community/Files` (push URL は `DISABLE_PUSH` に設定済み、誤爆防止)
- `rtk git fetch upstream && rtk git merge --ff-only upstream/main` でアップストリーム取り込み
- **MSIX 完全廃止** — `Package.appxmanifest` / AppxBundle 系 / Store / Sideload AppInstaller 等は全て削除済み。配布は **Velopack** (`vpk`) による GitHub Releases + 単一 exe。
- **名前空間・アプリ名は `Wilds` に統一** — upstream の `Files.*` 名前空間 / `Files.exe` / `Package.Current.*` はすべて `Wilds.*` に書き換え済み。

## ビルド

ソリューションは **`Wilds.slnx`** (旧 .sln ではない新フォーマット)。MSBuild または `dotnet build` のどちらでも動く (unpackaged なので MSIX ツーリング依存なし)。

```powershell
# Restore (slnx 全体)
msbuild Wilds.slnx -t:Restore -p:Platform=x64 -p:Configuration=Debug

# 開発ビルド
msbuild src\Wilds.App\Wilds.App.csproj -t:Build `
  -p:Configuration=Debug -p:Platform=x64

# リリース publish (Velopack 同梱用、self-contained 単一フォルダ出力)
dotnet publish src\Wilds.App\Wilds.App.csproj `
  -c Release -r win-x64 -p:Version=x.y.z
# → src/Wilds.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/Wilds.exe

# Velopack パッケージング
vpk pack --packId Wilds --packVersion x.y.z `
  --mainExe Wilds.exe --packDir <publish_dir> `
  --outputDir artifacts/velopack --channel win `
  --shortcuts "StartMenu,Desktop"
```

- **対応プラットフォーム**: `x64` / `arm64`
- **Configurations**: `Debug` / `Release`
- **.NET SDK**: `global.json` で `10.0.102` ピン留め (rollForward: latestMajor)
- **TargetFramework**: `net10.0-windows10.0.26100.0` (最小 19041)
- `<LangVersion>preview</LangVersion>` — C# プレビュー言語機能有効
- `<WindowsPackageType>None</WindowsPackageType>` + `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` + `<SelfContained>true</SelfContained>`

### ビルド順序の注意

`Wilds.App.csproj` 内の **`BuildWildsAppServer` ターゲット** (`BeforeTargets="ResolveAssemblyReferences"`) が `Wilds.App.Server` を先にビルドする。`Wilds.App.Server` はアウトプロセス WinRT COM サーバーで、その `.winmd` を CsWinRT が消費するため。プロジェクト参照ではなく MSBuild 呼び出しで連鎖している点に注意。

## テスト

テストランナーは **`Microsoft.Testing.Platform`** (`global.json` の `test.runner` 設定)。MSTest 形式。

- **`tests/Wilds.App.UITests`** — WinUI コンポーネントのユニット UI テスト
- **`tests/Wilds.InteractionTests`** — WinAppDriver + Appium による E2E 統合テスト

```powershell
# 統合テスト実行 (WinAppDriver を先に起動しておく)
dotnet test tests\Wilds.InteractionTests\Wilds.InteractionTests.csproj `
  -c Release -r win-x64 `
  --report-trx --report-trx-filename testResults.trx
```

## XAML フォーマット

- **XamlStyler.Console** を使用 (`dotnet tool install --global XamlStyler.Console`)
- 設定は `Settings.XamlStyler` — **タブインデント**、`x:Bind`/`Binding`/`controls:ThemedIconMarkup` は改行しない
- コミット前: `xstyler -p -l None -f <file.xaml>` (exit code 1 で未整形検出)
- PR 上で `/format` コメントを投げると GitHub Actions が自動整形して push する

## コーディング規約

- **`.editorconfig`**: C# と XAML どちらも **タブ (size 4)**
- **`Nullable` は `Enable`**
- ファイルヘッダ: `// Copyright (c) Files Community` + `// Licensed under the MIT License.` (upstream ライセンス継承のため文言自体は維持)
- 名前空間は `Wilds.App.X` / `Wilds.Core.X` / `Wilds.Shared` パターン
- **`GlobalUsings.cs`** に多数の `global using` 宣言あり。よく使うネームスペース (例: `CommunityToolkit.Mvvm.*`, `Wilds.App.Data.*`, `Wilds.App.Services.*`, `OwlCore.Storage`, `Wilds.App.Helpers`) は改めて `using` を書かない
- `System.IO` だけは別名 `SystemIO` に aliased (Windows の `File`/`Path` と衝突回避)
- `Package.Current.*` や `ApplicationData.Current.*` を**使わない**。代わりに `WildsAppInfo.*` / `AppPaths.*` / `AppSettingsStore.Values` を使う (下記参照)

## アーキテクチャ全体像

### プロジェクト構成 (`src/`)

| プロジェクト | 役割 |
|---|---|
| `Wilds.App` | WinUI 3 メインアプリ。エントリポイント、UI、ViewModels、Services 実装 |
| `Wilds.App.Server` | **アウトプロセス WinRT COM サーバー** (CsWinRT component)。`.winmd` を Wilds.App が参照 |
| `Wilds.App.Controls` | カスタムコントロール (Sidebar, Omnibar, BreadcrumbBar, ThemedIcon, Toolbar など) |
| `Wilds.App.Storage` | ストレージ実装層 (`Ftp/`, `Legacy/`, `Windows/`) |
| `Wilds.App.CsWin32` | CsWin32 ソースジェネレータ経由の Win32 P/Invoke |
| `Wilds.Core.Storage` | ストレージ契約層。`OwlCore.Storage` ベースの抽象 |
| `Wilds.Core.SourceGenerator` | Roslyn Analyzer + Source Generator (下記) |
| `Wilds.Shared` | クロスプロジェクト共有型 |

### Source Generators (`Wilds.Core.SourceGenerator/Generators/`)

以下は **コンパイル時に自動生成されるコード**。該当ファイルを直接探しても見つからないことに注意:

- **`CommandManagerGenerator`** — `IAction` 実装を全スキャンして `CommandManager.CreateCommands()` を自動生成。新しいアクションを作ったら自動で登録される
- **`StringsPropertyGenerator`** — `Strings/en-US/Resources.resw` の各キーに対する型付きプロパティを自動生成
- **`RegistrySerializationGenerator`** — レジストリ値の (de)serialization
- **`VTableFunctionGenerator`** — WinRT VTable 関数の生成

### 起動シーケンス (`Wilds.App/Program.cs` → `App.xaml.cs`)

1. `Program.Main` (同期・非 async — Narrator アクセシビリティ対応のため): **先頭で `Velopack.VelopackApp.Build().Run()`** を呼び、インストール/アンインストール等の特殊引数を先に処理
2. `Semaphore` で単一インスタンス検出 → 既存プロセスがあれば `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync` で委譲
3. `Application.Start` → `App` コンストラクタ → `OnLaunched`
4. `OnLaunched` 内で `AppLifecycleHelper.ConfigureHost()` が DI コンテナを構築し `Ioc.Default.ConfigureServices(host.Services)` に登録
5. `MainWindow` 表示 → `AppLifecycleHelper.InitializeAppComponentsAsync()` で QuickAccess/Libraries/CloudDrives/WSL/FileTags を並列初期化

### Unpackaged 専用互換ヘルパー (重要)

MSIX 廃止に伴い、以下のヘルパーが `Package.Current.*` / `ApplicationData.Current.*` の代替として用意されている:

| ヘルパー | 責務 |
|---|---|
| `WildsAppInfo` (`Wilds.App/Helpers/Application/AppInfo.cs`) | `PackageName`/`DisplayName`/`FamilyName`/`InstalledPath`/`Version` を提供 |
| `AppPaths` (`.../AppPaths.cs`) | `LocalFolderPath`/`RoamingFolderPath`/`LocalCacheFolderPath`/`TemporaryFolderPath` → `%LOCALAPPDATA%\Wilds\*` と `%TEMP%\Wilds` に解決 |
| `AppSettingsStore` (`.../AppSettingsStore.cs`) | `ApplicationData.Current.LocalSettings.Values` の代替。`AppPaths.LocalFolderPath\local-settings.json` に JSON 永続化 |

**新規コードで `Package.Current` / `ApplicationData.Current` を書かないこと。**

### DI (Dependency Injection)

**CommunityToolkit.Mvvm の `Ioc.Default`** 経由。登録は **`AppLifecycleHelper.ConfigureHost()`** 一箇所に集中 (`src/Wilds.App/Helpers/Application/AppLifecycleHelper.cs` 内)。

**`IUpdateService` は Velopack 単一実装** (`VelopackUpdateService`)。GitHub Releases (`1llum1n4t1s/Wilds`, channel `win`) から更新を取得。Velopack でインストールされていない Dev ビルド等では `_updateManager.IsInstalled` が false になり自動で no-op。

`App.xaml.cs` 先頭の `QuickAccessManager`/`HistoryWrapper`/`FileTagsManager`/`LibraryManager`/`AppModel`/`Logger` は `// TODO: Replace with DI` コメント付きの静的プロパティで、DI 移行途中。

### Actions / Commands / Hotkeys

| 層 | 説明 |
|---|---|
| `IAction` | ひとつのユーザー操作 (例: `CopyItemAction`)。`Label`/`Description`/`Glyph`/`HotKey`/`IsExecutable`/`ExecuteAsync` を実装 |
| `CommandCodes` | 全アクションの列挙 (enum) |
| `IRichCommand` (`ActionCommand`/`ModifiableCommand`) | `IAction` をラップし、`ICommand` + ラベル + ホットキー + 有効状態 + アイコンを統一的に提供 |
| `ICommandManager` | `commands[CommandCodes.X]` / `commands["X"]` / `commands[HotKey]` で引く。ホットキー検索表も管理 |

- アクション実装は `src/Wilds.App/Actions/{Category}/*.cs` (Display/FileSystem/Git/Global/Navigation/Open/Show/Sidebar/Start/Content)
- 新アクションを追加したら `CommandManagerGenerator` (source generator) が `CommandManager._commands` 配列を自動再生成
- ホットキーは `IActionsSettingsService.ActionsV2` でユーザーカスタマイズ可。`CommandManager.OverwriteKeyBindings()` が既定キーを差し替え

### UI 階層

- **`MainWindow`** (WinUIEx 拡張) → `MainPage` (タブバー) → 各タブに `ShellPanesPage` (分割ペイン) → `BaseShellPage` (`ModernShellPage`/`ColumnShellPage`) → `BaseLayoutPage` (`DetailsLayoutPage`/`GridLayoutPage`/`ColumnLayoutPage`/`ColumnsLayoutPage`)
- **`ShellViewModel`** (95KB超の大型 VM) が各シェル 1 個に対応し、ナビゲーション・ファイル列挙・選択状態を管理
- **Contexts** (`IContentPageContext` など) は **現在アクティブなペイン・ページをブロードキャストする観測可能オブジェクト**。Command や ViewModel はこれを購読して自身の状態を更新

### ローカライゼーション

- `src/Wilds.App/Strings/{locale}/Resources.resw` — upstream ミラーのまま (Crowdin 同期は切断)
- コードからは `StringsPropertyGenerator` が生成する型付きプロパティでアクセス
- **Unpackaged では `.pri` ファイル生成が必要**。未対応の状態では en-US フォールバックのみが読み込まれる可能性あり (要後続対応)

### Velopack 自動更新

- `Program.Main` 冒頭で `VelopackApp.Build().Run()` が Velopack 特殊引数 (--install/--uninstall/--afterUpdate 等) を処理
- 起動後 `AppLifecycleHelper.CheckAppUpdate()` → `VelopackUpdateService.CheckForUpdatesAsync()` が GitHub Releases (channel `win`) を確認
- 更新あり → バックグラウンドダウンロード → `IsUpdateAvailable = true` でツールバーに更新ボタン表示
- ユーザーが `UpdateCommand` 実行 → `ApplyUpdatesAndRestart` で適用 & 再起動

### リリースフロー

1. `release/x.y.z` ブランチを作成して push
2. [velopack-release.yml](.github/workflows/velopack-release.yml) が発火
3. `dotnet publish` → `vpk pack` → `vpk upload github` で GitHub Releases に自動アップロード
4. 既存ユーザーのアプリが自動で新版を検出 → ダウンロード → 次回起動で適用

## やってはいけないこと

- `Package.Current.*` / `ApplicationData.Current.*` を新規コードに書かない — `WildsAppInfo` / `AppPaths` / `AppSettingsStore` を使う
- 新規 `using` を書く前に `GlobalUsings.cs` を確認 — 既に `global using` されている可能性が高い
- `System.IO.File`/`System.IO.Path` を直書きしない — `SystemIO.File` / `SystemIO.Path` を使う (エイリアス済み)
- `upstream` リモートへ push しない (URL が `DISABLE_PUSH` に設定済みなので失敗するが念のため)
- コミット前に XAML を編集したら `xstyler -p -l None -f <file>` で整形確認 (CI が落ちる)
- WPF 構文 (`Visibility.Collapsed`, `Trigger`/`DataTrigger`) を XAML で使わない — WinUI 3 は **`IsVisible=false`** と **Style セレクタ** を使う
- MSIX 系プロパティ (`AppxBundle`, `GenerateAppxPackageOnBuild`, `EnableMsixTooling` 等) を csproj に追加しない — Unpackaged 前提
