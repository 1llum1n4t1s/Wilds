// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace Wilds.App.Services
{
	/// <summary>
	/// GitHub Releases から Velopack パッケージを取得して自動更新を行うサービス。
	/// </summary>
	public sealed partial class VelopackUpdateService : ObservableObject, IUpdateService, IDisposable
	{
		// フォーク先のリポジトリ URL。velopack-release.yml が vpk upload github でここへ公開する。
		private const string GitHubRepoUrl = "https://github.com/1llum1n4t1s/Wilds";
		private const string ReleaseChannel = "win";

		private readonly UpdateManager _updateManager;

		private UpdateInfo? _pendingUpdate;

		private readonly ILogger? _logger = Ioc.Default.GetService<ILogger<App>>();

		public VelopackUpdateService()
		{
			var source = new GithubSource(GitHubRepoUrl, string.Empty, false);
			var options = new UpdateOptions { ExplicitChannel = ReleaseChannel };
			_updateManager = new UpdateManager(source, options);
		}

		private bool _isUpdateAvailable;
		public bool IsUpdateAvailable
		{
			get => _isUpdateAvailable;
			private set => SetProperty(ref _isUpdateAvailable, value);
		}

		private bool _isUpdating;
		public bool IsUpdating
		{
			get => _isUpdating;
			private set => SetProperty(ref _isUpdating, value);
		}

		public bool IsAppUpdated => AppLifecycleHelper.IsAppUpdated;

		private bool _areReleaseNotesAvailable;
		public bool AreReleaseNotesAvailable
		{
			get => _areReleaseNotesAvailable;
			private set => SetProperty(ref _areReleaseNotesAvailable, value);
		}

		public async Task CheckForUpdatesAsync()
		{
			// Velopack のランタイムが未インストール (開発ビルド等) の場合は何もしない
			if (!_updateManager.IsInstalled)
			{
				_logger?.LogInformation("Velopack: アプリが Velopack でインストールされていないため、更新チェックをスキップします。");
				return;
			}

			try
			{
				_logger?.LogInformation("Velopack: 更新チェックを開始します。");
				var updateInfo = await _updateManager.CheckForUpdatesAsync();

				if (updateInfo is null)
				{
					_logger?.LogInformation("Velopack: 更新はありません。");
					IsUpdateAvailable = false;
					_pendingUpdate = null;
					return;
				}

				_logger?.LogInformation($"Velopack: 新しいバージョンを検出しました: {updateInfo.TargetFullRelease.Version}");
				_pendingUpdate = updateInfo;

				// バックグラウンドでダウンロードまで完了させてから UI にボタンを出す
				await _updateManager.DownloadUpdatesAsync(updateInfo);
				IsUpdateAvailable = true;
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Velopack: 更新チェック中にエラーが発生しました。");
			}
		}

		public Task DownloadUpdatesAsync()
		{
			if (!IsUpdateAvailable || _pendingUpdate is null || !_updateManager.IsInstalled)
				return Task.CompletedTask;

			IsUpdating = true;

			try
			{
				// 更新を適用して再起動。この呼び出しはプロセスを終了させるため、以降のコードは原則到達しない。
				_updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Velopack: 更新適用中にエラーが発生しました。");
				IsUpdating = false;
			}

			return Task.CompletedTask;
		}

		public async Task CheckForReleaseNotesAsync()
		{
			using var client = new HttpClient();

			try
			{
				var response = await client.GetAsync(Constants.ExternalUrl.ReleaseNotesUrl);
				AreReleaseNotesAvailable = response.IsSuccessStatusCode;
			}
			catch
			{
				AreReleaseNotesAvailable = false;
			}
		}

		public void Dispose()
		{
			// UpdateManager は IDisposable ではないが、将来拡張用にメソッド自体は残す
		}
	}
}
