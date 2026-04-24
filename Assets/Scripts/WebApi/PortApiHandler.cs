using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class PortApiHandler
{
    public async Task GetAllAsync(HttpListenerContext ctx)
    {
        var response = await MainThreadTaskDispatcher.RunOnMainThread(() =>
        {
            var list = NetworkPortManager.Instance.GetAllPortDatas()
                .Select(p => new PortDto
                {
                    id = p.Id ?? "",
                    protocolName = p.ProtocolName,
                    netProtocol = p.NetProtocol,
                    maskType = p.MaskType ?? "",
                    localPort = p.LocalPortDetails?.Port ?? "",
                    remotePort = p.RemotePortDetails?.Port ?? "",
                    targetIp = p.TargetIP ?? "",
                    isConnected = p.IsConnected,
                    sourceProtocolName = p.SourceProtocolName ?? "",
                    sourceProtocolId = p.SourceProtocolId ?? ""
                })
                .ToList();

            return new PortListResponse { ports = list };
        });

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(response));
    }

    public async Task AddAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        AddPortReq req;
        try { req = JsonUtility.FromJson<AddPortReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.protocolName) || string.IsNullOrWhiteSpace(req.netProtocol))
        {
            HttpApiServer.WriteError(ctx, 400, "protocolName and netProtocol are required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                string srcId = req.sourceProtocolId ?? "";
                string srcName = req.sourceProtocolName ?? "";
                if (!string.IsNullOrEmpty(srcId) && string.IsNullOrEmpty(srcName))
                {
                    var srcPort = NetworkPortManager.Instance.GetAllPortDatas()
                        .FirstOrDefault(p => p.Id == srcId);
                    if (srcPort != null) srcName = srcPort.ProtocolName;
                }

                var portData = new PortData
                {
                    ProtocolName = req.protocolName,
                    NetProtocol = req.netProtocol,
                    LocalPortDetails  = new PortDetails { Port = string.IsNullOrEmpty(req.localPort)  ? "--" : req.localPort },
                    RemotePortDetails = new PortDetails { Port = string.IsNullOrEmpty(req.remotePort) ? "--" : req.remotePort },
                    TargetIP = req.targetIp ?? "",
                    MaskType = string.IsNullOrEmpty(req.maskType) ? "OriginalData" : req.maskType,
                    SourceProtocolName = srcName,
                    SourceProtocolId = srcId,
                    IsConnected = false,
                };

                if (!NetworkPortManager.Instance.IsPortUnique(portData))
                    throw new InvalidOperationException("Port already exists (same protocol + port number)");

                NetworkPortManager.Instance.AddPortData(portData);
            });

            HttpApiServer.WriteJson(ctx, 201, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (InvalidOperationException ex)
        {
            HttpApiServer.WriteError(ctx, 409, ex.Message);
        }
        catch (ArgumentException ex)
        {
            HttpApiServer.WriteError(ctx, 400, ex.Message);
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 500, ex.Message);
        }
    }

    public async Task DeleteAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        DeletePortReq req;
        try { req = JsonUtility.FromJson<DeletePortReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(async () =>
            {
                var port = NetworkPortManager.Instance.GetAllPortDatas().FirstOrDefault(p =>
                    p.ProtocolName == req.protocolName &&
                    (string.IsNullOrEmpty(req.netProtocol) || p.NetProtocol == req.netProtocol) &&
                    (string.IsNullOrEmpty(req.localPort)   || (p.LocalPortDetails?.Port  ?? "") == req.localPort) &&
                    (string.IsNullOrEmpty(req.remotePort)  || (p.RemotePortDetails?.Port ?? "") == req.remotePort));

                if (port == null)
                    throw new KeyNotFoundException($"Port '{req.protocolName}' not found");

                await NetworkPortManager.Instance.RemovePortData(port);
            });

            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (KeyNotFoundException ex)
        {
            HttpApiServer.WriteError(ctx, 404, ex.Message);
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 500, ex.Message);
        }
    }

    public async Task ChangeMaskAsync(HttpListenerContext ctx, string protocolName)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        ChangeMaskReq req;
        try { req = JsonUtility.FromJson<ChangeMaskReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.maskType))
        {
            HttpApiServer.WriteError(ctx, 400, "maskType is required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                var port = NetworkPortManager.Instance.GetAllPortDatas()
                    .FirstOrDefault(p => p.ProtocolName == protocolName);

                if (port == null)
                    throw new KeyNotFoundException($"Port '{protocolName}' not found");

                if (port.NetProtocol == "TCP Server")
                    throw new InvalidOperationException("TCP Server does not support mask switching");

                if (!MaskTypeManager.Instance.HasMaskType(req.maskType))
                    throw new ArgumentException($"MaskType '{req.maskType}' not registered");

                port.MaskType = req.maskType;
                NetworkPortManager.Instance.SaveData();
                _ = NetworkPortManager.Instance.MaskSwitch(port); // restart connector
                MaskDefinitionManager.Instance.NotifyChanged();
            });

            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (KeyNotFoundException ex)
        {
            HttpApiServer.WriteError(ctx, 404, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            HttpApiServer.WriteError(ctx, 403, ex.Message);
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 400, ex.Message);
        }
    }
}
