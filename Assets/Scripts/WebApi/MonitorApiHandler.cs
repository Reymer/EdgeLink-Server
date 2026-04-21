using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

public class MonitorApiHandler
{
    public async Task SetMonitorPortAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        MonitorPortReq req;
        try { req = JsonUtility.FromJson<MonitorPortReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.protocolName))
        {
            HttpApiServer.WriteError(ctx, 400, "protocolName is required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                var port = NetworkPortManager.Instance.GetAllPortDatas()
                    .FirstOrDefault(p =>
                        p.ProtocolName == req.protocolName &&
                        (string.IsNullOrEmpty(req.netProtocol) || p.NetProtocol == req.netProtocol) &&
                        (string.IsNullOrEmpty(req.localPort)   || (p.LocalPortDetails?.Port  ?? "") == req.localPort) &&
                        (string.IsNullOrEmpty(req.remotePort)  || (p.RemotePortDetails?.Port ?? "") == req.remotePort));

                if (port == null)
                    throw new KeyNotFoundException($"Port '{req.protocolName}' not found");

                var targetType = port.NetProtocol switch
                {
                    "TCP Server" => MonitorTargetType.TCPServer,
                    "TCP Client" => MonitorTargetType.TCPClient,
                    _ => MonitorTargetType.UDP
                };

                MonitorManager.Instance.SetMonitorPort(port, targetType);
            });

            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (KeyNotFoundException ex)
        {
            HttpApiServer.WriteError(ctx, 404, ex.Message);
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 400, ex.Message);
        }
    }

    public Task ClearMonitorPortAsync(HttpListenerContext ctx)
    {
        MainThreadTaskDispatcher.RunOnMainThread(() =>
            MonitorManager.Instance.SetMonitorPort(null, MonitorTargetType.UDP));

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        return Task.CompletedTask;
    }

    public Task GetMonitorPortAsync(HttpListenerContext ctx)
    {
        var (portData, _) = MonitorManager.Instance.GetMonitorInfo();
        var resp = new MonitorPortResponse
        {
            protocolName = portData?.ProtocolName ?? ""
        };
        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(resp));
        return Task.CompletedTask;
    }
}
