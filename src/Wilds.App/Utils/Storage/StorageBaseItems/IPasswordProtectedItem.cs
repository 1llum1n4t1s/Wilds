// Copyright (c) Files Community
// Licensed under the MIT License.

using FluentFTP.Exceptions;
using Cube.FileSystem.SevenZip;

namespace Wilds.App.Utils.Storage
{
	public interface IPasswordProtectedItem
	{
		StorageCredential Credentials { get; set; }

		Func<IPasswordProtectedItem, Task<StorageCredential>> PasswordRequestedCallback { get; set; }

		async Task<TOut> RetryWithCredentialsAsync<TOut>(Func<Task<TOut>> func, Exception exception)
		{
			var handled = exception is EncryptionException ||
				exception is FtpAuthenticationException;

			if (!handled || PasswordRequestedCallback is null)
				throw exception;

			// Why (P1-9): 新しい Credentials インスタンスに差し替えると親子の参照共有が切れてしまい、
			// 子で取得した password が親に反映されず再ダイアログが出続ける。
			// 既存の Credentials の中身を更新することで親子の参照共有を維持する。
			var fresh = await PasswordRequestedCallback(this);
			if (fresh is not null && Credentials is not null)
			{
				Credentials.UserName = fresh.UserName;
				Credentials.Password = fresh.Password;
			}
			else if (fresh is not null)
			{
				Credentials = fresh;
			}

			return await func();
		}

		async Task RetryWithCredentialsAsync(Func<Task> func, Exception exception)
		{
			var handled = exception is EncryptionException ||
				exception is FtpAuthenticationException;

			if (!handled || PasswordRequestedCallback is null)
				throw exception;

			var fresh = await PasswordRequestedCallback(this);
			if (fresh is not null && Credentials is not null)
			{
				Credentials.UserName = fresh.UserName;
				Credentials.Password = fresh.Password;
			}
			else if (fresh is not null)
			{
				Credentials = fresh;
			}

			await func();
		}

		void CopyFrom(IPasswordProtectedItem parent)
		{
			// Why (P1-9): 参照共有。親で Credentials.Password が更新されたら子にも自動反映される。
			Credentials = parent.Credentials;
			PasswordRequestedCallback = parent.PasswordRequestedCallback;
		}
	}
}
