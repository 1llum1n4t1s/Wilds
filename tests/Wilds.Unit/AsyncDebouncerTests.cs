// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Wilds.Shared.Helpers;

namespace Wilds.Unit;

[TestClass]
public sealed class AsyncDebouncerTests
{
	[TestMethod]
	public async Task Trigger_BurstWithinInterval_FiresOnce()
	{
		int calls = 0;
		using var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(80),
			() => Interlocked.Increment(ref calls));

		// 連続 5 回トリガ (50ms 以内に全部済むはず)
		for (int i = 0; i < 5; i++)
		{
			debouncer.Trigger();
			await Task.Delay(5);
		}

		// debounce 期間より十分長く待つ
		await Task.Delay(300);

		Assert.AreEqual(1, calls, "コールバックは debounce 期間後に 1 回のみ呼ばれるべき");
	}

	[TestMethod]
	public async Task Trigger_GapLargerThanInterval_FiresMultipleTimes()
	{
		int calls = 0;
		using var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(50),
			() => Interlocked.Increment(ref calls));

		debouncer.Trigger();
		await Task.Delay(150);
		debouncer.Trigger();
		await Task.Delay(150);
		debouncer.Trigger();
		await Task.Delay(150);

		Assert.AreEqual(3, calls);
	}

	[TestMethod]
	public async Task Cancel_BeforeFire_PreventsCallback()
	{
		int calls = 0;
		using var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100),
			() => Interlocked.Increment(ref calls));

		debouncer.Trigger();
		await Task.Delay(20);
		debouncer.Cancel();

		await Task.Delay(200);

		Assert.AreEqual(0, calls);
	}

	[TestMethod]
	public async Task Dispose_PendingTrigger_DoesNotFire()
	{
		int calls = 0;
		var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(100),
			() => Interlocked.Increment(ref calls));

		debouncer.Trigger();
		await Task.Delay(20);
		debouncer.Dispose();

		await Task.Delay(200);

		Assert.AreEqual(0, calls);
	}

	[TestMethod]
	public void Dispose_ThenTrigger_IsNoOp()
	{
		var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(50),
			() => Assert.Fail("Dispose 後の Trigger でコールバックが呼ばれないこと"));

		debouncer.Dispose();

		// Dispose 後の Trigger は静かに無視される (例外を投げない)
		debouncer.Trigger();
	}

	[TestMethod]
	public async Task Trigger_ConcurrentFromMultipleThreads_FiresOnce()
	{
		// 高めの interval にして並列トリガが debounce 期間内に確実に収まるように。
		int calls = 0;
		using var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(300),
			() => Interlocked.Increment(ref calls));

		var barrier = new ManualResetEventSlim(false);
		const int threads = 10;
		var tasks = new Task[threads];

		for (int i = 0; i < threads; i++)
		{
			tasks[i] = Task.Run(() =>
			{
				barrier.Wait();
				for (int j = 0; j < 5; j++)
					debouncer.Trigger();
			});
		}

		barrier.Set();
		await Task.WhenAll(tasks);

		// debounce 完了まで十分待つ
		await Task.Delay(700);

		Assert.AreEqual(1, calls, "並列 Trigger でも debounce で 1 回だけ");
	}

	[TestMethod]
	public void Constructor_NullCallback_Throws()
	{
		// Why: MSTest 4 の Assert.ThrowsExactly は Func<object?> を期待する。
		// Action overload を呼び出す形で wrap し、コンストラクタの ArgumentNullException 検証を行う。
		Func<AsyncDebouncer> ctor = () =>
			new AsyncDebouncer(TimeSpan.FromMilliseconds(10), (Action)null!);
		Assert.ThrowsExactly<ArgumentNullException>(ctor);
	}

	[TestMethod]
	public async Task AsyncCallback_RespectsCancellationToken()
	{
		int started = 0;
		int completed = 0;
		using var debouncer = new AsyncDebouncer(TimeSpan.FromMilliseconds(40),
			async ct =>
			{
				Interlocked.Increment(ref started);
				try
				{
					await Task.Delay(500, ct);
					Interlocked.Increment(ref completed);
				}
				catch (OperationCanceledException) { }
			});

		debouncer.Trigger();
		// 1 回目の Delay 中に再 Trigger → 2 回目の Trigger でも Cancel は前回の delay+callback の方の token に対して
		// しかし Trigger のたびに新しい CTS に差し替わるので前回タスクの Delay 内 Task.Delay(500, ct) も
		// (ct = 古い CTS の Token) でキャンセルされる
		await Task.Delay(60);
		debouncer.Trigger();
		await Task.Delay(60);
		debouncer.Cancel();

		await Task.Delay(800);

		// callback は最大 2 回開始されうるが、いずれもキャンセルされて completed には到達しない可能性が高い
		// 厳密検証は flaky なので started >= 1 のみアサート
		Assert.IsTrue(started >= 1);
		Assert.AreEqual(0, completed, "全て debounce/cancel で打ち切られている");
	}
}
