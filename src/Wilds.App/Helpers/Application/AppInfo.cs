// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Reflection;

namespace Wilds.App.Helpers
{
	/// <summary>
	/// Package.Current に代わるアプリ情報アクセサ。Unpackaged 配布 (Velopack) 前提。
	/// </summary>
	public static class WildsAppInfo
	{
		/// <summary>
		/// アプリ名。旧 Package.Current.Id.Name に相当。レジストリキー等に使う安定した識別子。
		/// </summary>
		public const string PackageName = "Wilds";

		/// <summary>
		/// 表示名。旧 Package.Current.DisplayName に相当。
		/// </summary>
		public const string DisplayName = "Wilds";

		/// <summary>
		/// ファミリ名代替。クリップボード/ドラッグ元判定用の安定した識別子。
		/// </summary>
		public const string FamilyName = "Wilds_1llum1n4t1s";

		/// <summary>
		/// アプリインストールディレクトリ。旧 Package.Current.InstalledLocation.Path / EffectivePath に相当。
		/// </summary>
		public static string InstalledPath { get; } = AppContext.BaseDirectory.TrimEnd(SystemIO.Path.DirectorySeparatorChar);

		private static readonly Version _version = LoadVersion();

		/// <summary>
		/// アプリバージョン。旧 Package.Current.Id.Version に相当。
		/// </summary>
		public static Version Version => _version;

		private static Version LoadVersion()
		{
			var asm = Assembly.GetExecutingAssembly();
			var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (!string.IsNullOrEmpty(info))
			{
				// "+commit" などを除去
				var plus = info.IndexOf('+');
				if (plus > 0) info = info[..plus];
				if (Version.TryParse(info, out var v))
					return v;
			}
			return asm.GetName().Version ?? new Version(0, 0, 0, 0);
		}
	}
}
