public static class ByteSizeFormatter
{
    /// <summary>
    /// 格式化
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;
        const long TB = 1024 * GB;

        if (bytes >= TB)
            return $"{(bytes / (double)TB):0.##} TB";
        else if (bytes >= GB)
            return $"{(bytes / (double)GB):0.##} GB";
        else if (bytes >= MB)
            return $"{(bytes / (double)MB):0.##} MB";
        else if (bytes >= KB)
            return $"{(bytes / (double)KB):0.##} KB";
        else
            return $"{bytes} Bytes";
    }
}
