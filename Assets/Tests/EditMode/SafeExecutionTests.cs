using NUnit.Framework;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

public class SafeExecutionTests
{
    // ── Safe（同步）────────────────────────────────────────────────────────────

    [Test]
    public void Safe_NormalAction_Executes()
    {
        bool executed = false;
        SafeExecution.Safe(() => executed = true);
        Assert.IsTrue(executed);
    }

    [Test]
    public void Safe_ThrowingAction_DoesNotPropagate()
    {
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*test error.*"));
        Assert.DoesNotThrow(() =>
            SafeExecution.Safe(() => throw new InvalidOperationException("test error")));
    }

    [Test]
    public void Safe_NullAction_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => SafeExecution.Safe(null));
    }

    // ── SafeAsync（非同步）────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator SafeAsync_NormalTask_Executes() => UniTask.ToCoroutine(async () =>
    {
        bool executed = false;
        await SafeExecution.SafeAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });
        Assert.IsTrue(executed);
    });

    [UnityTest]
    public IEnumerator SafeAsync_ThrowingTask_DoesNotPropagate() => UniTask.ToCoroutine(async () =>
    {
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*async error.*"));
        bool threw = false;
        try
        {
            await SafeExecution.SafeAsync(async () =>
            {
                await Task.Delay(1);
                throw new Exception("async error");
            });
        }
        catch { threw = true; }
        Assert.IsFalse(threw, "SafeAsync 應吞掉例外，不拋給呼叫端");
    });

    [UnityTest]
    public IEnumerator SafeAsync_NullAction_DoesNotThrow() => UniTask.ToCoroutine(async () =>
    {
        bool threw = false;
        try { await SafeExecution.SafeAsync(null); }
        catch { threw = true; }
        Assert.IsFalse(threw);
    });

    // ── WithCancellation<T>（取消支援）────────────────────────────────────────

    [UnityTest]
    public IEnumerator WithCancellation_CompletesBeforeCancel_ReturnsResult() => UniTask.ToCoroutine(async () =>
    {
        var result = await SafeExecution.WithCancellation(Task.FromResult(42), CancellationToken.None);
        Assert.AreEqual(42, result);
    });

    [UnityTest]
    public IEnumerator WithCancellation_AlreadyCancelled_Throws() => UniTask.ToCoroutine(async () =>
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool threw = false;
        try { await SafeExecution.WithCancellation(new TaskCompletionSource<int>().Task, cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw);
    });

    [UnityTest]
    public IEnumerator WithCancellation_CancelledDuringWait_Throws() => UniTask.ToCoroutine(async () =>
    {
        using var cts = new CancellationTokenSource(100);

        bool threw = false;
        try { await SafeExecution.WithCancellation(new TaskCompletionSource<int>().Task, cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw);
    });

    // ── WithCancellation（無回傳值）────────────────────────────────────────────

    [UnityTest]
    public IEnumerator WithCancellationVoid_CompletesNormally() => UniTask.ToCoroutine(async () =>
    {
        await SafeExecution.WithCancellation(Task.CompletedTask, CancellationToken.None);
    });

    [UnityTest]
    public IEnumerator WithCancellationVoid_AlreadyCancelled_Throws() => UniTask.ToCoroutine(async () =>
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool threw = false;
        try { await SafeExecution.WithCancellation((Task)new TaskCompletionSource<bool>().Task, cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw);
    });
}
