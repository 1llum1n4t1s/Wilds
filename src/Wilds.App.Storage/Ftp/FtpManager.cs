// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Net;
using Windows.Security.Credentials;

namespace Wilds.App.Storage
{
	/// <summary>
	/// FTP 接続資格情報の保管庫。
	/// </summary>
	/// <remarks>
	/// Why (rere P1 #12): 従来は <c>static Dictionary&lt;string, NetworkCredential&gt;</c> で
	/// プロセスメモリに平文 (SecureString バックエンド経由) で保持しており、メモリダンプや
	/// startup-crash.log の例外スタックトレース経由で漏洩する恐れがあった。
	/// Windows の <see cref="PasswordVault"/> (DPAPI 保護) を使うように切り替え、必要なときだけ
	/// 取り出して短時間だけメモリに置くようにする。リソース名は <c>Wilds:ftp:&lt;host&gt;</c>。
	/// </remarks>
	public static class FtpManager
	{
		private const string FtpResourcePrefix = "Wilds:ftp:";

		public static readonly NetworkCredential Anonymous = new("anonymous", "anonymous");

		/// <summary>
		/// 指定 host に保存された資格情報を取得する。未登録なら <see cref="Anonymous"/> を返す。
		/// </summary>
		/// <remarks>
		/// 戻り値の <see cref="NetworkCredential"/> は呼び出し側が短時間だけ保持する想定。
		/// 永続的にキャッシュしないこと (DPAPI 保護のメリットが失われる)。
		/// </remarks>
		public static NetworkCredential GetCredential(string host)
		{
			if (string.IsNullOrWhiteSpace(host))
				return Anonymous;

			try
			{
				var vault = new PasswordVault();
				var resource = FtpResourcePrefix + host;
				PasswordCredential? credential = null;
				try
				{
					var creds = vault.FindAllByResource(resource);
					if (creds.Count > 0)
						credential = creds[0];
				}
				catch
				{
					// FindAllByResource はリソース未登録時に例外を投げる
				}

				if (credential is not null)
				{
					credential.RetrievePassword();
					return new NetworkCredential(credential.UserName, credential.Password);
				}
			}
			catch
			{
				// PasswordVault 失敗時は Anonymous フォールバック
			}

			return Anonymous;
		}

		/// <summary>
		/// 指定 host の資格情報を <see cref="PasswordVault"/> に保存する。
		/// 同一 host の既存エントリは上書き (古いものは削除される)。
		/// </summary>
		public static void SaveCredential(string host, string username, string password)
		{
			if (string.IsNullOrWhiteSpace(host) || string.IsNullOrEmpty(username))
				return;

			try
			{
				var vault = new PasswordVault();
				var resource = FtpResourcePrefix + host;

				// 既存の同一 resource のエントリを削除 (上書き相当)
				try
				{
					var existing = vault.FindAllByResource(resource);
					foreach (var c in existing)
						vault.Remove(c);
				}
				catch
				{
					// 未登録時の例外は無視
				}

				vault.Add(new PasswordCredential(resource, username, password ?? string.Empty));
			}
			catch
			{
				// 永続化失敗はランタイムエラーにしない
			}
		}

		/// <summary>
		/// 指定 host の資格情報を削除する。
		/// </summary>
		public static void DeleteCredential(string host)
		{
			if (string.IsNullOrWhiteSpace(host))
				return;

			try
			{
				var vault = new PasswordVault();
				var resource = FtpResourcePrefix + host;
				var existing = vault.FindAllByResource(resource);
				foreach (var c in existing)
					vault.Remove(c);
			}
			catch
			{
				// 未登録 / 削除失敗は無視
			}
		}
	}
}
