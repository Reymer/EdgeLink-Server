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
    public Task GetAllAsync(HttpListenerContext ctx)
    {
        // NetPortRegistry.GetAll() 內部有 lock 並回傳 ToList() 快照，執行緒安全，不需主執行緒
        var list = NetworkPortManager.Instance.GetAllPortDatas()
            .Select(p => new PortDto
            {
                id = p.Id ?? "",
                protocolName = p.ProtocolName,
                netProtocol = p.NetProtocol,
                maskType = p.MaskType ?? "",
                responseMaskType = p.ResponseMaskType ?? "",
                requestMode = p.RequestMode ?? "serial",
                localPort = p.LocalPortDetails?.Port ?? "",
                remotePort = p.RemotePortDetails?.Port ?? "",
                targetIp = p.TargetIP ?? "",
                isConnected = p.IsConnected,
                isEnabled = p.IsEnabled,
                sourceProtocolName = p.SourceProtocolName ?? "",
                sourceProtocolId = p.SourceProtocolId ?? "",
                currentConnections = p.CurrentConnections,
                totalConnections = p.TotalConnections,
                totalReceivedBytes = p.TotalReceivedBytes
            })
            .ToList();

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new PortListResponse { ports = list }));
        return Task.CompletedTask;
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
                    ResponseMaskType = req.responseMaskType ?? "",
                    RequestMode = string.IsNullOrEmpty(req.requestMode) ? "serial" : req.requestMode,
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
                var port = NetworkPortManager.Instance.GetAllPortDatas()
                    .FirstOrDefault(p => p.Id == req.id);

                if (port == null)
                    throw new KeyNotFoundException($"Port '{req.id}' not found");

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

    public async Task UpdateAsync(HttpListenerContext ctx, string id)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        UpdatePortReq req;
        try { req = JsonUtility.FromJson<UpdatePortReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.protocolName) || string.IsNullOrWhiteSpace(req.netProtocol))
        {
            HttpApiServer.WriteError(ctx, 400, "protocolName and netProtocol are required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(async () =>
            {
                var old = NetworkPortManager.Instance.GetAllPortDatas()
                    .FirstOrDefault(p => p.Id == id);

                if (old == null)
                    throw new KeyNotFoundException($"Port '{id}' not found");

                string srcId = req.sourceProtocolId ?? "";
                string srcName = req.sourceProtocolName ?? "";
                if (!string.IsNullOrEmpty(srcId) && string.IsNullOrEmpty(srcName))
                {
                    var srcPort = NetworkPortManager.Instance.GetAllPortDatas()
                        .FirstOrDefault(p => p.Id == srcId);
                    if (srcPort != null) srcName = srcPort.ProtocolName;
                }

                var req2 = new PortData
                {
                    ProtocolName = req.protocolName,
                    NetProtocol = req.netProtocol,
                    LocalPortDetails  = new PortDetails { Port = string.IsNullOrEmpty(req.localPort)  ? "--" : req.localPort },
                    RemotePortDetails = new PortDetails { Port = string.IsNullOrEmpty(req.remotePort) ? "--" : req.remotePort },
                    TargetIP = req.targetIp ?? "",
                    MaskType = string.IsNullOrEmpty(req.maskType) ? "OriginalData" : req.maskType,
                    ResponseMaskType = req.responseMaskType ?? "",
                    RequestMode = string.IsNullOrEmpty(req.requestMode) ? "serial" : req.requestMode,
                    SourceProtocolName = srcName,
                    SourceProtocolId = srcId,
                };

                await NetworkPortManager.Instance.UpdatePortData(old, req2);
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

    public async Task ChangeMaskAsync(HttpListenerContext ctx, string id)
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
                    .FirstOrDefault(p => p.Id == id);

                if (port == null)
                    throw new KeyNotFoundException($"Port '{id}' not found");

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

    public Task GetClientsAsync(HttpListenerContext ctx, string id)
    {
        var port = NetworkPortManager.Instance.GetAllPortDatas().FirstOrDefault(p => p.Id == id);
        if (port == null) { HttpApiServer.WriteError(ctx, 404, "Port not found"); return Task.CompletedTask; }
        if (port.NetProtocol != "TCP Server") { HttpApiServer.WriteError(ctx, 400, "Only TCP Server supports client listing"); return Task.CompletedTask; }
        var clients = NetworkPortManager.Instance.networkConnectorCore.GetTcpServerClients(port.Key);
        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ClientDetailListResponse { clients = clients }));
        return Task.CompletedTask;
    }

    public async Task ToggleEnabledAsync(HttpListenerContext ctx, string id)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        ToggleEnabledReq req;
        try { req = JsonUtility.FromJson<ToggleEnabledReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        try
        {
            await NetworkPortManager.Instance.TogglePortEnabled(id, req.enabled);
            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (KeyNotFoundException ex) { HttpApiServer.WriteError(ctx, 404, ex.Message); }
        catch (Exception ex)            { HttpApiServer.WriteError(ctx, 500, ex.Message); }
    }
}
