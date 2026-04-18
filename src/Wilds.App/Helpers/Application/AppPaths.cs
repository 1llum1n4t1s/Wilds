// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Wilds.App.Helpers
{
	/// <summary>
	/// ApplicationData.Current に代わる、Unpackaged ビルド向けパス解決ヘルパー。
	/// 旧 LocalFolder/RoamingFolder/LocalCacheFolder/TemporaryFolder を実ファイルシステムパスへ射影する。
	/// </summary>
	public static class AppPaths
	{
		/// <summary>
		/// %LOCALAPPDATA%\Files\Local
		/// 旧 ApplicationData.Current.LocalFolder.Path 相当。
		/// </summary>
		public static string LocalFolderPath { get; } = EnsureDir(
			SystemIO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WildsAppInfo.PackageName, "Local"));

		/// <summary>
		/// %LOCALAPPDATA%\Files\Roaming (Unpackaged ではローミングしないが API 互換用に提供)
		/// </summary>
		public static string RoamingFolderPath { get; } = EnsureDir(
			SystemIO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WildsAppInfo.PackageName, "Roaming"));

		/// <summary>
		/// %LOCALAPPDATA%\Files\Cache
		/// </summary>
		public static string LocalCacheFolderPath { get; } = EnsureDir(
			SystemIO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WildsAppInfo.PackageName, "Cache"));

		/// <summary>
		/// %TEMP%\Files
		/// 旧 ApplicationData.Current.TemporaryFolder 相当。
		/// </summary>
		public static string TemporaryFolderPath { get; } = EnsureDir(
			SystemIO.Path.Combine(SystemIO.Path.GetTempPath(), WildsAppInfo.PackageName));

		private static string EnsureDir(string path)
		{
			try
			{
				SystemIO.Directory.CreateDirectory(path);
			}
			catch
			{
				// ignore — ディレクトリ作成に失敗しても呼び出し側で個別に扱う
			}
			return path;
		}
	}
}
