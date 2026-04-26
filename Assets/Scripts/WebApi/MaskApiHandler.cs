using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class MaskApiHandler
{
    public async Task GetAllAsync(HttpListenerContext ctx)
    {
        var response = await MainThreadTaskDispatcher.RunOnMainThread(() =>
        {
            var ids = MaskDefinitionManager.Instance.GetMaskTypeIds();
            return new MaskListResponse { maskTypes = ids };
        });

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(response));
    }

    public async Task GetDefinitionAsync(HttpListenerContext ctx, string maskId)
    {
        var dto = await MainThreadTaskDispatcher.RunOnMainThread(() =>
        {
            var def = MaskDefinitionManager.Instance.GetDefinition(maskId);
            if (def == null) return null;
            return ToDto(def);
        });

        if (dto == null)
        {
            HttpApiServer.WriteError(ctx, 404, $"MaskType '{maskId}' not found");
            return;
        }

        HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(dto));
    }

    public async Task SaveDefinitionAsync(HttpListenerContext ctx, string maskId)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        MaskDefinitionDto dto;
        try { dto = JsonUtility.FromJson<MaskDefinitionDto>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (dto == null || string.IsNullOrWhiteSpace(dto.maskId))
        {
            HttpApiServer.WriteError(ctx, 400, "maskId is required");
            return;
        }

        if (dto.maskId != maskId)
        {
            HttpApiServer.WriteError(ctx, 400, "maskId in body must match URL");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                MaskDefinitionManager.Instance.SaveDefinition(FromDto(dto));
            });

            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 400, ex.Message);
        }
    }

    public async Task RenameAsync(HttpListenerContext ctx, string maskId)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        RenameReq req;
        try { req = JsonUtility.FromJson<RenameReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.newId))
        {
            HttpApiServer.WriteError(ctx, 400, "newId is required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                // 先更新 port 引用，再 Rename（Rename 會同步觸發 OnMaskTypesChanged，
                // Table 的事件回呼此時需要 port.MaskType 已是新名稱才能選到正確索引）
                bool anyUpdated = false;
                foreach (var port in NetworkPortManager.Instance.GetAllPortDatas())
                {
                    if (port.MaskType == maskId)
                    {
                        port.MaskType = req.newId;
                        anyUpdated = true;
                    }
                }

                MaskDefinitionManager.Instance.RenameMask(maskId, req.newId);

                if (anyUpdated) NetworkPortManager.Instance.SaveData();
            });
            HttpApiServer.WriteJson(ctx, 200, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (KeyNotFoundException ex) { HttpApiServer.WriteError(ctx, 404, ex.Message); }
        catch (Exception ex)            { HttpApiServer.WriteError(ctx, 400, ex.Message); }
    }

    public async Task AddAsync(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await sr.ReadToEndAsync();

        AddMaskReq req;
        try { req = JsonUtility.FromJson<AddMaskReq>(body); }
        catch { HttpApiServer.WriteError(ctx, 400, "Invalid JSON body"); return; }

        if (string.IsNullOrWhiteSpace(req?.maskId))
        {
            HttpApiServer.WriteError(ctx, 400, "maskId is required");
            return;
        }

        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                if (MaskDefinitionManager.Instance.HasMaskType(req.maskId))
                    throw new InvalidOperationException($"MaskType '{req.maskId}' already exists");

                MaskDefinitionManager.Instance.AddMaskType(req.maskId, req.localizationKey);
            });

            HttpApiServer.WriteJson(ctx, 201, JsonUtility.ToJson(new ApiResult { success = true }));
        }
        catch (Exception ex)
        {
            HttpApiServer.WriteError(ctx, 400, ex.Message);
        }
    }

    public async Task DeleteAsync(HttpListenerContext ctx, string maskId)
    {
        try
        {
            await MainThreadTaskDispatcher.RunOnMainThread(() =>
            {
                if (!MaskDefinitionManager.Instance.HasMaskType(maskId))
                    throw new KeyNotFoundException($"MaskType '{maskId}' not found");

                MaskDefinitionManager.Instance.RemoveMaskType(maskId);
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

    private static MaskDefinitionDto ToDto(MaskDefinition def) => new MaskDefinitionDto
    {
        maskId = def.maskId,
        localizationKey = def.localizationKey,
        description = def.description,
        fieldDelimiter = def.fieldDelimiter,
        kvSeparator = def.kvSeparator,
        outputTemplate = def.outputTemplate,
        sampleData = def.sampleData,
        routeMode = def.routeMode ?? "",
        correlationIdField = def.correlationIdField ?? ""
    };

    private static MaskDefinition FromDto(MaskDefinitionDto dto) => new MaskDefinition
    {
        maskId = dto.maskId,
        localizationKey = dto.localizationKey,
        description = dto.description,
        fieldDelimiter = dto.fieldDelimiter,
        kvSeparator = dto.kvSeparator,
        outputTemplate = dto.outputTemplate,
        sampleData = dto.sampleData,
        routeMode = dto.routeMode ?? "",
        correlationIdField = dto.correlationIdField ?? ""
    };
}
