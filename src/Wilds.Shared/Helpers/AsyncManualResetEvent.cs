// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Wilds.Shared.Helpers;

public sealed class AsyncManualResetEvent
{
	// Why (rere P0 #2): RunContinuationsAsynchronously を付けると TrySetResult の継続が
	// 呼び出し元スレッドをジャックせず安全にスケジュールされるため、Set() 側の
	// 複雑な Task.Factory.StartNew + Task.Wait ダンスを廃して単純な TrySetResult 1 行にできる。
	// 従来実装は ThreadPool 枯渇 + PreferFairness (キュー末尾) で確実にデッドロックしていた。
	private volatile TaskCompletionSource<bool> m_tcs =
		new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public async Task WaitAsync(CancellationToken cancellationToken = default)
	{
		var tcs = m_tcs;
		var cancelTcs = new TaskCompletionSource<bool>();

		cancellationToken.Register(
			s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), cancelTcs);

		await await Task.WhenAny(tcs.Task, cancelTcs.Task);
	}

	private async Task<bool> Delay(int milliseconds)
	{
		await Task.Delay(milliseconds);
		return false;
	}

	public async Task<bool> WaitAsync(int milliseconds, CancellationToken cancellationToken = default)
	{
		var tcs = m_tcs;
		var cancelTcs = new TaskCompletionSource<bool>();

		cancellationToken.Register(
			s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), cancelTcs);

		return await await Task.WhenAny(tcs.Task, cancelTcs.Task, Delay(milliseconds));
	}

	public void Set()
	{
		// Why (rere P0 #2): RunContinuationsAsynchronously 前提なら TrySetResult だけで十分。
		// 呼び出し元スレッド (特にファイル変更 watcher スレッド) をブロックしない。
		m_tcs.TrySetResult(true);
	}

	public void Reset()
	{
		var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		while (true)
		{
			var tcs = m_tcs;
			if (!tcs.Task.IsCompleted ||
				Interlocked.CompareExchange(ref m_tcs, newTcs, tcs) == tcs)
				return;
		}
	}
}
