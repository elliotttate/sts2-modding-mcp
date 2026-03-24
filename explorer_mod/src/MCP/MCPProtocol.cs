using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotExplorer.MCP;

// ==================== JSON-RPC 2.0 ====================

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

// ==================== MCP Protocol Types ====================

public class MCPInitializeResult
{
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "2024-11-05";
    [JsonPropertyName("capabilities")] public MCPCapabilities Capabilities { get; set; } = new();
    [JsonPropertyName("serverInfo")] public MCPServerInfo ServerInfo { get; set; } = new();
}

public class MCPCapabilities
{
    [JsonPropertyName("tools")] public object Tools { get; set; } = new { };
}

public class MCPServerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "GodotExplorer";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
}

public class MCPToolInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("inputSchema")] public MCPToolSchema InputSchema { get; set; } = new();
}

public class MCPToolSchema
{
    [JsonPropertyName("type")] public string Type { get; set; } = "object";
    [JsonPropertyName("properties")] public Dictionary<string, MCPPropertySchema> Properties { get; set; } = new();
    [JsonPropertyName("required")] public List<string>? Required { get; set; }
}

public class MCPPropertySchema
{
    [JsonPropertyName("type")] public string Type { get; set; } = "string";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

public class MCPToolResult
{
    [JsonPropertyName("content")] public List<MCPContent> Content { get; set; } = new();
    [JsonPropertyName("isError")] public bool? IsError { get; set; }
}

public class MCPContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
}

// ==================== Tool Listing ====================

public class MCPToolsListResult
{
    [JsonPropertyName("tools")] public List<MCPToolInfo> Tools { get; set; } = new();
}

// ==================== Helpers ====================

public static class MCPHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static MCPToolResult TextResult(string text) => new()
    {
        Content = new List<MCPContent> { new() { Type = "text", Text = text } }
    };

    public static MCPToolResult ErrorResult(string message) => new()
    {
        Content = new List<MCPContent> { new() { Type = "text", Text = message } },
        IsError = true
    };

    public static JsonRpcResponse SuccessResponse(JsonElement? id, object result) => new()
    {
        Id = id,
        Result = result
    };

    public static JsonRpcResponse ErrorResponse(JsonElement? id, int code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message }
    };
}
