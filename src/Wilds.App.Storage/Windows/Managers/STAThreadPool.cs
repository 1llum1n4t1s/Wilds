// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading.Channels;
using Windows.Win32;

namespace Wilds.App.Storage
{
	/// <summary>
	/// 常駐 STA スレッド + <see cref="Channel{T}"/> による STA ワークプール。
	/// <see cref="STATask.Run(Action, Microsoft.Extensions.Logging.ILogger)"/> 系 API の裏で使われる。
	/// </summary>
	/// <remarks>
	/// Why: 従来の <see cref="STATask"/> は呼び出し毎に <c>new Thread()</c> + <c>OleInitialize</c>
	/// を実行しており、サムネイル取得のような高頻度呼び出しでスレッド生成コストが爆発していた
	/// (100 ファイルで最悪 200+ スレッド)。このプールは固定 2 本の STA スレッドを常駐させ、
	/// <c>OleInitialize/Uninitialize</c> はスレッドライフタイム全体で 1 回だけに抑える。
	///
	/// 契約:
	/// - ワークアイテムは 1 関数実行内で完結すること (関数の戻り値が COM RCW でないこと)。
	/// - 戻り値を STA 境界を越えて持ち出せるのは値型 / マネージドオブジェクト / byte[] 等のみ。
	/// - キュー投入は任意スレッドから安全。
	/// </remarks>
	internal sealed class STAThreadPool : IDisposable
	{
		/// <summary>アプリ全体で共有するシングルトン (遅延初期化)。</summary>
		public static readonly STAThreadPool Shared = new(threadCount: 2, "Wilds-STA");

		private readonly Channel<IWorkItem> _channel = Channel.CreateUnbounded<IWorkItem>(
			new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
		private readonly CancellationTokenSource _shutdownCts = new();
		private readonly Thread[] _threads;

		private STAThreadPool(int threadCount, string name)
		{
			_threads = new Thread[threadCount];
			for (int i = 0; i < threadCount; i++)
			{
				var t = new Thread(WorkLoop)
				{
					Name = $"{name}-{i}",
					IsBackground = true,
				};
				t.SetApartmentState(ApartmentState.STA);
				_threads[i] = t;
				t.Start();
			}
		}

		/// <summary>同期デリゲートをエンキュー。</summary>
		public Task<TResult> EnqueueAsync<TResult>(Func<TResult> func, CancellationToken ct = default)
		{
			var item = new SyncWorkItem<TResult>(func, ct);
			if (!_channel.Writer.TryWrite(item))
				item.SetCanceled();
			return item.Task;
		}

		/// <summary>Action をエンキュー (戻り値なし)。</summary>
		public Task EnqueueAsync(Action action, CancellationToken ct = default)
		{
			var item = new SyncWorkItem<bool>(() => { action(); return true; }, ct);
			if (!_channel.Writer.TryWrite(item))
				item.SetCanceled();
			return item.Task;
		}

		/// <summary>
		/// 非同期デリゲート (Func&lt;Task&gt;) をエンキュー。
		/// STA スレッド内で <c>GetAwaiter().GetResult()</c> で同期待機する。
		/// </summary>
		/// <remarks>
		/// ⚠️ <b>重要な制約 (rere P2 #24)</b>:
		/// このオーバーロードに渡す <paramref name="func"/> 内で、<b>WinRT の IAsyncOperation 系を
		/// AsTask().GetAwaiter().GetResult() で待ったり ConfigureAwait(false) なしで await したりしてはいけない</b>。
		/// STA スレッドは <see cref="System.Threading.SynchronizationContext"/> を null 化しているが、WinRT の
		/// 非同期 API は STA メッセージポンプを必要とすることがあり、ポンプを回さずブロックすると
		/// 永久ハング (デッドロック) する。
		/// 軽量な C# Task ベース処理 (例: <c>await Task.Run(...)</c> 結果の取得) なら安全。
		/// 不安なら同期版 (<see cref="EnqueueAsync{TResult}(Func{TResult}, CancellationToken)"/>) を使うこと。
		/// </remarks>
		public Task<TResult> EnqueueAsync<TResult>(Func<Task<TResult>> func, CancellationToken ct = default)
		{
			var item = new SyncWorkItem<TResult>(() => func().GetAwaiter().GetResult(), ct);
			if (!_channel.Writer.TryWrite(item))
				item.SetCanceled();
			return item.Task;
		}

		/// <summary>非同期デリゲート (Func&lt;Task&gt;) をエンキュー (戻り値なし)。</summary>
		/// <remarks>
		/// ⚠️ 同上の制約 (WinRT 非同期 API を内部で待ってはいけない)。
		/// </remarks>
		public Task EnqueueAsync(Func<Task> func, CancellationToken ct = default)
		{
			var item = new SyncWorkItem<bool>(() => { func().GetAwaiter().GetResult(); return true; }, ct);
			if (!_channel.Writer.TryWrite(item))
				item.SetCanceled();
			return item.Task;
		}

		private void WorkLoop()
		{
			// WinRT の SynchronizationContext が STA スレッドに自動設置されると、
			// Channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult() で
			// デッドロックするリスクがある。明示的に null 化して回避。
			SynchronizationContext.SetSynchronizationContext(null);

			PInvoke.OleInitialize();
			try
			{
				var reader = _channel.Reader;
				var token = _shutdownCts.Token;

				while (!token.IsCancellationRequested)
				{
					try
					{
						// ValueTask<bool> → Task → 同期ブロック (STA メインループの代替)
						var hasItem = reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult();
						if (!hasItem)
							break;
					}
					catch (OperationCanceledException)
					{
						break;
					}

					while (reader.TryRead(out var item))
					{
						try
						{
							item.Execute();
						}
						catch
						{
							// WorkItem 側で例外キャッチ済みだが、念のため WorkLoop は生かす。
						}
					}
				}
			}
			finally
			{
				PInvoke.OleUninitialize();
			}
		}

		public void Dispose()
		{
			_shutdownCts.Cancel();
			_channel.Writer.TryComplete();
			foreach (var t in _threads)
			{
				try
				{
					t.Join(millisecondsTimeout: 2000);
				}
				catch
				{
					// ignore
				}
			}
			_shutdownCts.Dispose();
		}

		private interface IWorkItem
		{
			void Execute();
		}

		private sealed class SyncWorkItem<T> : IWorkItem
		{
			private readonly Func<T> _func;
			private readonly TaskCompletionSource<T> _tcs;
			private readonly CancellationToken _ct;

			public Task<T> Task => _tcs.Task;

			public SyncWorkItem(Func<T> func, CancellationToken ct)
			{
				_func = func;
				_ct = ct;
				_tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			public void Execute()
			{
				if (_ct.IsCancellationRequested)
				{
					_tcs.TrySetCanceled(_ct);
					return;
				}

				try
				{
					var result = _func();
					_tcs.TrySetResult(result);
				}
				catch (OperationCanceledException oce)
				{
					_tcs.TrySetCanceled(oce.CancellationToken);
				}
				catch (Exception ex)
				{
					_tcs.TrySetException(ex);
				}
			}

			public void SetCanceled()
			{
				_tcs.TrySetCanceled();
			}
		}
	}
}
