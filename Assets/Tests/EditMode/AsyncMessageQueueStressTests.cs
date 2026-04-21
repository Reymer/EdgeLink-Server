using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.TestTools;

public class AsyncMessageQueueStressTests
{
    // ── 多生產者單消費者 ──────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator MultiProducer_SingleConsumer_NoDataLoss() => UniTask.ToCoroutine(async () =>
    {
        const int producers = 8;
        const int perProducer = 500;
        const int total = producers * perProducer;

        var q = new AsyncMessageQueue<int>();
        var received = new List<int>();
        var allSent = new TaskCompletionSource<bool>();

        // 消費者
        var consumer = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(10000);
            while (received.Count < total)
                received.Add(await q.DequeueAsync(cts.Token));
        });

        // 多個生產者並發寫入
        var producers_tasks = Enumerable.Range(0, producers).Select(p => Task.Run(() =>
        {
            for (int i = 0; i < perProducer; i++)
                q.Enqueue(p * perProducer + i);
        })).ToArray();

        await Task.WhenAll(producers_tasks);
        await consumer;

        Assert.AreEqual(total, received.Count, "不應有資料遺失");

        var sorted = received.OrderBy(x => x).ToArray();
        for (int i = 0; i < total; i++)
            Assert.AreEqual(i, sorted[i], $"遺失了 item {i}");
    });

    // ── 超過佇列上限 ─────────────────────────────────────────────────────────

    [Test]
    public void Enqueue_BeyondMaxSize_DropsExtra()
    {
        const int max = 10000;
        var q = new AsyncMessageQueue<int>();

        // 填滿
        for (int i = 0; i < max; i++) q.Enqueue(i);
        int countAtMax = q.Count;

        // 再塞 100 個，應該被丟棄
        for (int i = 0; i < 100; i++) q.Enqueue(99999);

        Assert.AreEqual(max, countAtMax, "填滿後計數應等於上限");
        Assert.AreEqual(max, q.Count, "超出上限的項目應被丟棄，計數不變");
    }

    // ── 取消 ─────────────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Cancel_WhileQueueEmpty_DoesNotDeadlock() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<int>();
        using var cts = new CancellationTokenSource(200);

        bool threw = false;
        try { await q.DequeueAsync(cts.Token); }
        catch (OperationCanceledException) { threw = true; }

        Assert.IsTrue(threw, "應在 200ms 內拋出取消例外，不應死鎖");
    });

    [UnityTest]
    public IEnumerator Cancel_DuringBurst_ConsumerExitsCleanly() => UniTask.ToCoroutine(async () =>
    {
        const int total = 200;
        var q = new AsyncMessageQueue<int>();
        using var cts = new CancellationTokenSource();

        int consumed = 0;
        var consumer = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await q.DequeueAsync(cts.Token);
                    Interlocked.Increment(ref consumed);
                    Thread.Sleep(1); // 每項 ~1ms，確保取消能中斷迴圈
                }
            }
            catch (OperationCanceledException) { }
        });

        // 生產者寫入固定數量
        var producer = Task.Run(() =>
        {
            for (int i = 0; i < total; i++) q.Enqueue(i);
        });

        await producer;
        await Task.Delay(30); // 約消化 ~30 項後取消
        cts.Cancel();
        await consumer;

        Assert.Greater(consumed, 0, "消費者應有處理到部分資料");
        Assert.Less(consumed, total, "取消後不應全部消費完");
    });

    // ── 高頻並發寫入讀取 ─────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator HighFrequency_ConcurrentReadWrite_CountConsistent() => UniTask.ToCoroutine(async () =>
    {
        var q = new AsyncMessageQueue<int>();
        int enqueued = 0;
        int dequeued = 0;
        using var cts = new CancellationTokenSource(3000);

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 3000; i++)
            {
                q.Enqueue(i);
                Interlocked.Increment(ref enqueued);
                Thread.Sleep(0); // 讓出 CPU，增加交錯機會
            }
        });

        var reader = Task.Run(async () =>
        {
            try
            {
                while (dequeued < 3000)
                {
                    await q.DequeueAsync(cts.Token);
                    Interlocked.Increment(ref dequeued);
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.WhenAll(writer, reader);

        Assert.AreEqual(enqueued, dequeued, "寫入與讀出數量應一致");
        Assert.AreEqual(0, q.Count, "佇列最終應為空");
    });

    // ── 多消費者（雖非設計用途，驗證不崩潰）────────────────────────────────

    [UnityTest]
    public IEnumerator MultiConsumer_TotalConsumedEqualsEnqueued() => UniTask.ToCoroutine(async () =>
    {
        const int itemCount = 1000;
        var q = new AsyncMessageQueue<int>();
        int totalConsumed = 0;
        using var cts = new CancellationTokenSource(5000);

        for (int i = 0; i < itemCount; i++) q.Enqueue(i);

        var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await q.DequeueAsync(cts.Token);
                    Interlocked.Increment(ref totalConsumed);
                }
            }
            catch (OperationCanceledException) { }
        })).ToArray();

        // 等到全部消費完或超時
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (totalConsumed < itemCount && DateTime.UtcNow < deadline)
            await UniTask.Delay(10);

        cts.Cancel();
        await Task.WhenAll(consumers);

        Assert.AreEqual(itemCount, totalConsumed, "多消費者下總消費量應等於寫入量");
    });
}
