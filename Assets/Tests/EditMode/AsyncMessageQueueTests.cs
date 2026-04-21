using NUnit.Framework;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.TestTools;

public class AsyncMessageQueueTests
{
    // ── 基本入隊/出隊 ─────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Enqueue_Dequeue_ReturnsSameItem() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<string>();
        q.Enqueue("hello");
        var result = await q.DequeueAsync();
        Assert.AreEqual("hello", result);
    });

    [UnityTest]
    public IEnumerator Enqueue_Multiple_FIFOOrder() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<int>();
        q.Enqueue(1);
        q.Enqueue(2);
        q.Enqueue(3);

        Assert.AreEqual(1, await q.DequeueAsync());
        Assert.AreEqual(2, await q.DequeueAsync());
        Assert.AreEqual(3, await q.DequeueAsync());
    });

    [Test]
    public void Count_ReflectsEnqueued()
    {
        var q = new AsyncMessageQueue<string>();
        Assert.AreEqual(0, q.Count);
        q.Enqueue("a");
        q.Enqueue("b");
        Assert.AreEqual(2, q.Count);
    }

    [UnityTest]
    public IEnumerator Count_DecreasesAfterDequeue() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<string>();
        q.Enqueue("x");
        await q.DequeueAsync();
        Assert.AreEqual(0, q.Count);
    });

    // ── 取消 ─────────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator DequeueAsync_AlreadyCancelled_Throws() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool threw = false;
        try { await q.DequeueAsync(cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw);
    });

    [UnityTest]
    public IEnumerator DequeueAsync_CancelledAfterWait_Throws() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<string>();
        using var cts = new CancellationTokenSource(100);

        bool threw = false;
        try { await q.DequeueAsync(cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw);
    });

    // ── 並行生產者/消費者 ─────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Concurrent_ProducerConsumer_AllItemsReceived() => UniTask.ToCoroutine(async () =>
    {
        const int count = 100;
        var q = new AsyncMessageQueue<int>();
        var received = new int[count];

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < count; i++) q.Enqueue(i);
        });

        var consumer = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
                received[i] = await q.DequeueAsync();
        });

        await Task.WhenAll(producer, consumer);

        Array.Sort(received);
        for (int i = 0; i < count; i++)
            Assert.AreEqual(i, received[i]);
    });
}
