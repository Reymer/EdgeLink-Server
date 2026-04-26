using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class SettingsApiHandler
{
    /// <summary>GET /api/settings/export</summary>
    public async Task ExportAsync(HttpListenerContext ctx)
    {
        var dto = await MainThreadTaskDispatcher.RunOnMainThread(() =>
        {
            var result = new SettingsExportDto();

            foreach (var p in NetworkPortManager.Instance.GetAllPortDatas())
            {
                result.ports.Add(new PortExportDto
                {
                    protocolName      = p.ProtocolName,
                    netProtocol       = p.NetProtocol,
                    localPort         = p.LocalPortDetails?.Port  ?? "",
                    remotePort        = p.RemotePortDetails?.Port ?? "",
                    targetIp          = p.TargetIP    ?? "",
                    maskType          = p.MaskType    ?? "OriginalData",
                    responseMaskType  = p.ResponseMaskType ?? "",
                    requestMode       = p.RequestMode ?? "serial",
                    sourceProtocolName= p.SourceProtocolName ?? "",
                    sourceProtocolId  = p.SourceProtocolId ?? "",
                    isEnabled         = p.IsEnabled,
                });
            }

            foreach (var id in MaskDefinitionManager.Instance.GetMaskTypeIds())
            {
                if (id == "OriginalData") continue;
                var def = MaskDefinitionManager.Instance.GetDefinition(id);
                if (def == null) continue;
                result.masks.Add(new MaskDefinitionDto
                {
                    maskId           = def.maskId,
                    localizationKey  = def.localizationKey,
                    description      = def.description,
                    fieldDelimiter   = def.fieldDelimiter,
                    kvSeparator      = def.kvSeparator,
                    outputTemplate   = def.outputTemplate,
                    sampleData       = def.sampleData,
                    routeMode        = def.routeMode ?? "",
                    correlationIdField = def.correlationIdField ?? "",
                });
            }

            return result;
        });

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(dto));
    }

    /// <summary>POST /api/settings/import</summary>
    public async Task ImportAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        SettingsExportDto dto;
        try { dto = JsonUtility.FromJson<SettingsExportDto>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON"); return; }
        if (dto == null) { HttpApiServer.WriteError(ctx, 400, "Invalid format"); return; }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                foreach (var mask in dto.masks ?? new List<MaskDefinitionDto>())
                {
                    if (string.IsNullOrEmpty(mask.maskId)) continue;
                    if (!MaskDefinitionManager.Instance.HasMaskType(mask.maskId))
                        MaskDefinitionManager.Instance.AddMaskType(mask.maskId, mask.localizationKey);
                    MaskDefinitionManager.Instance.SaveDefinition(new MaskDefinition
                    {
                        maskId           = mask.maskId,
                        localizationKey  = mask.localizationKey,
                        description      = mask.description,
                        fieldDelimiter   = mask.fieldDelimiter,
                        kvSeparator      = mask.kvSeparator,
                        outputTemplate   = mask.outputTemplate,
                        sampleData       = mask.sampleData,
                        routeMode        = mask.routeMode ?? "",
                        correlationIdField = mask.correlationIdField ?? "",
                    });
                }

                foreach (var p in dto.ports ?? new List<PortExportDto>())
                {
                    if (string.IsNullOrEmpty(p.protocolName) || string.IsNullOrEmpty(p.netProtocol)) continue;
                    var portData = new PortData
                    {
                        ProtocolName       = p.protocolName,
                        NetProtocol        = p.netProtocol,
                        LocalPortDetails   = new PortDetails { Port = string.IsNullOrEmpty(p.localPort)  ? "--" : p.localPort  },
                        RemotePortDetails  = new PortDetails { Port = string.IsNullOrEmpty(p.remotePort) ? "--" : p.remotePort },
                        TargetIP           = p.targetIp ?? "",
                        MaskType           = string.IsNullOrEmpty(p.maskType) ? "OriginalData" : p.maskType,
                        ResponseMaskType   = p.responseMaskType ?? "",
                        RequestMode        = string.IsNullOrEmpty(p.requestMode) ? "serial" : p.requestMode,
                        SourceProtocolName = p.sourceProtocolName ?? "",
                        SourceProtocolId   = p.sourceProtocolId ?? "",
                        IsEnabled          = p.isEnabled,
                        IsConnected        = false,
                    };
                    if (NetworkPortManager.Instance.IsPortUnique(portData))
                        NetworkPortManager.Instance.AddPortData(portData);
                }
            });

            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 500, ex.Message);
        }
    }
}
