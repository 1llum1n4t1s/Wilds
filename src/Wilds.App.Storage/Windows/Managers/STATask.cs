// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Wilds.App.Storage
{
	/// <summary>
	/// STA スレッド上でワークを実行するエントリポイント。
	/// </summary>
	/// <remarks>
	/// 従来実装は呼び出しごとに <c>new Thread()</c> + <c>OleInitialize</c> を走らせていたが、
	/// 高頻度呼び出し (サムネイル取得等) でスレッド生成コストが爆発していた。
	/// 内部実装を常駐 STA プール (<see cref="STAThreadPool"/>) への委譲に切り替えた。
	/// 公開 API (この 4 overload) は従来と完全互換を保つ。
	/// </remarks>
	public partial class STATask
	{
		/// <summary>STA スレッド上で <paramref name="action"/> を実行する。</summary>
		public static Task Run(Action action, ILogger? logger)
		{
			return STAThreadPool.Shared.EnqueueAsync(action).ContinueWith(t =>
			{
				if (t.IsFaulted && logger is not null)
					logger.LogWarning(t.Exception?.GetBaseException(), "An exception was occurred during the execution within STA.");
			}, TaskContinuationOptions.ExecuteSynchronously);
		}

		/// <summary>STA スレッド上で <paramref name="func"/> を実行し、戻り値を得る。</summary>
		public static Task<T> Run<T>(Func<T> func, ILogger? logger)
		{
			return STAThreadPool.Shared.EnqueueAsync(func).ContinueWith(t =>
			{
				if (t.IsFaulted)
				{
					if (logger is not null)
						logger.LogWarning(t.Exception?.GetBaseException(), "An exception was occurred during the execution within STA.");
					return default(T)!;
				}
				return t.Result;
			}, TaskContinuationOptions.ExecuteSynchronously);
		}

		/// <summary>STA スレッド上で非同期 <paramref name="func"/> を実行する。</summary>
		public static Task Run(Func<Task> func, ILogger? logger)
		{
			return STAThreadPool.Shared.EnqueueAsync(func).ContinueWith(t =>
			{
				if (t.IsFaulted && logger is not null)
					logger.LogWarning(t.Exception?.GetBaseException(), "An exception was occurred during the execution within STA.");
			}, TaskContinuationOptions.ExecuteSynchronously);
		}

		/// <summary>STA スレッド上で非同期 <paramref name="func"/> を実行し、戻り値を得る。</summary>
		public static Task<T?> Run<T>(Func<Task<T>> func, ILogger? logger)
		{
			return STAThreadPool.Shared.EnqueueAsync(func).ContinueWith(t =>
			{
				if (t.IsFaulted)
				{
					if (logger is not null)
						logger.LogWarning(t.Exception?.GetBaseException(), "An exception was occurred during the execution within STA.");
					return default(T);
				}
				return (T?)t.Result;
			}, TaskContinuationOptions.ExecuteSynchronously);
		}
	}
}
