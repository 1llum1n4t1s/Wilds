// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Data.Contracts
{
	/// <summary>
	/// Velopack ベースの更新サービス契約。
	/// </summary>
	public interface IUpdateService : INotifyPropertyChanged
	{
		/// <summary>
		/// 新しいバージョンが検出されダウンロード済みかどうか。
		/// </summary>
		bool IsUpdateAvailable { get; }

		/// <summary>
		/// 更新のダウンロード/適用が進行中かどうか。
		/// </summary>
		bool IsUpdating { get; }

		/// <summary>
		/// このセッションが初回 (= 前回のバージョンから更新されている) かどうか。
		/// </summary>
		bool IsAppUpdated { get; }

		/// <summary>
		/// リリースノートが取得可能かどうか。
		/// </summary>
		bool AreReleaseNotesAvailable { get; }

		/// <summary>
		/// 更新をダウンロードして適用し、アプリを再起動する。
		/// </summary>
		Task DownloadUpdatesAsync();

		/// <summary>
		/// GitHub Releases に新しいバージョンがあるか確認する。
		/// </summary>
		Task CheckForUpdatesAsync();

		/// <summary>
		/// リリースノート URL が到達可能かチェックする。
		/// </summary>
		Task CheckForReleaseNotesAsync();
	}
}
