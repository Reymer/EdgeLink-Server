public static class MonitorCounter
{
    private static int counter = 0;

    public static int Next()
    {
        return System.Threading.Interlocked.Increment(ref counter);
    }

    public static void Reset()
    {
        System.Threading.Interlocked.Exchange(ref counter, 0);
    }
}
