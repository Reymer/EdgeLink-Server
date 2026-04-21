using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

public class LogApiHandler
{
    public Task GetLogsAsync(HttpListenerContext ctx)
    {
        string cursorStr = ctx.Request.QueryString["cursor"];
        int cursor = int.TryParse(cursorStr, out var c) ? c : 0;

        var (total, logs) = LogHelper.GetMonitorLogsSince(cursor);

        var response = new LogListResponse { total = total, logs = new List<string>(logs) };
        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(response));
        return Task.CompletedTask;
    }

    public Task GetMonitorLogsAsync(HttpListenerContext ctx)
    {
        string cursorStr = ctx.Request.QueryString["cursor"];
        int cursor = int.TryParse(cursorStr, out var c) ? c : 0;

        var (total, logs) = LogHelper.GetWebMonitorLogsSince(cursor);

        var response = new LogListResponse { total = total, logs = new List<string>(logs) };
        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(response));
        return Task.CompletedTask;
    }
}
