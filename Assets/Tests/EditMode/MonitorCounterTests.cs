using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;

public class MonitorCounterTests
{
    [SetUp]
    public void SetUp() => MonitorCounter.Reset();

    [Test]
    public void Next_StartsFromOne()
    {
        Assert.AreEqual(1, MonitorCounter.Next());
    }

    [Test]
    public void Next_Increments()
    {
        MonitorCounter.Next();
        MonitorCounter.Next();
        Assert.AreEqual(3, MonitorCounter.Next());
    }

    [Test]
    public void Reset_ResetsToZero()
    {
        MonitorCounter.Next();
        MonitorCounter.Next();
        MonitorCounter.Reset();
        Assert.AreEqual(1, MonitorCounter.Next());
    }

    [Test]
    public void Next_ThreadSafe_AllValuesUnique()
    {
        const int count = 500;
        var results = new int[count];

        Parallel.For(0, count, i => results[i] = MonitorCounter.Next());

        var distinct = results.Distinct().Count();
        Assert.AreEqual(count, distinct, "並行呼叫應產生不重複的遞增值");
    }
}
