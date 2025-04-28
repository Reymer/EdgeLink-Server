public static class MonitorCounter
{
    private static int counter = 0;

    public static int Next()
    {
        counter++;
        return counter;
    }

    public static void Reset()
    {
        counter = 0;
    }
}
