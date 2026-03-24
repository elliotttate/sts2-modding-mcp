using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace GodotExplorer.MCP;

/// <summary>
/// TCP-based MCP server implementing JSON-RPC 2.0 over newline-delimited JSON.
/// Listens on localhost for Claude Code (or any MCP client) connections.
/// Tools execute on the Godot main thread via MainThreadDispatcher.
/// </summary>
public class MCPServer
{
    private TcpListener? _listener;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Guid, Task> _clients = new();
    private bool _running;

    public int Port => _port;
    public bool IsRunning => _running;

    public MCPServer(int port = 27020)
    {
        _port = port;
    }

    public void Start()
    {
        if (_running) return;

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            Task.Run(() => AcceptClientsAsync(_cts.Token));
            GD.Print($"[GodotExplorer] MCP server listening on localhost:{_port}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] MCP server failed to start: {ex.Message}");
        }
    }

    public void Stop()
    {
        _running = false;
        _cts.Cancel();
        _listener?.Stop();
        GD.Print("[GodotExplorer] MCP server stopped.");
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _running)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var clientId = Guid.NewGuid();
                GD.Print($"[GodotExplorer] MCP client connected: {clientId}");
                var task = Task.Run(() => HandleClientAsync(client, clientId, ct));
                _clients[clientId] = task;
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (_running)
                    GD.PrintErr($"[GodotExplorer] MCP accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, Guid clientId, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(ct); }
                    catch (OperationCanceledException) { break; }
                    catch (IOException) { break; }

                    if (line == null) break; // Client disconnected
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var response = ProcessMessage(line);
                    if (response != null)
                    {
                        string json = JsonSerializer.Serialize(response, MCPHelpers.JsonOptions);
                        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                        await stream.WriteAsync(data, ct);
                        await stream.FlushAsync(ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_running)
                GD.PrintErr($"[GodotExplorer] MCP client {clientId} error: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            GD.Print($"[GodotExplorer] MCP client disconnected: {clientId}");
        }
    }

    private JsonRpcResponse? ProcessMessage(string json)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(json, MCPHelpers.JsonOptions);
            if (request == null)
                return MCPHelpers.ErrorResponse(null, -32700, "Parse error");
        }
        catch (Exception ex)
        {
            return MCPHelpers.ErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        try
        {
            // Notifications (no id) don't get responses
            if (request.Id == null || request.Id.Value.ValueKind == JsonValueKind.Null)
            {
                // Handle known notifications silently
                return null;
            }

            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "initialized" => null,
                "notifications/initialized" => null,
                "tools/list" => HandleListTools(request),
                "tools/call" => HandleCallTool(request),
                "ping" => MCPHelpers.SuccessResponse(request.Id, new { }),
                _ => MCPHelpers.ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            return MCPHelpers.ErrorResponse(request.Id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        var result = new MCPInitializeResult
        {
            ServerInfo = new MCPServerInfo
            {
                Name = "GodotExplorer",
                Version = Core.ExplorerCore.Version
            }
        };
        return MCPHelpers.SuccessResponse(request.Id, result);
    }

    private JsonRpcResponse HandleListTools(JsonRpcRequest request)
    {
        var result = new MCPToolsListResult { Tools = MCPTools.GetToolList() };
        return MCPHelpers.SuccessResponse(request.Id, result);
    }

    private JsonRpcResponse HandleCallTool(JsonRpcRequest request)
    {
        string toolName = "";
        JsonElement? arguments = null;

        if (request.Params?.ValueKind == JsonValueKind.Object)
        {
            if (request.Params.Value.TryGetProperty("name", out var nameEl))
                toolName = nameEl.GetString() ?? "";
            if (request.Params.Value.TryGetProperty("arguments", out var argsEl))
                arguments = argsEl;
        }

        if (string.IsNullOrEmpty(toolName))
            return MCPHelpers.ErrorResponse(request.Id, -32602, "Missing tool name");

        var result = MCPTools.ExecuteTool(toolName, arguments);
        return MCPHelpers.SuccessResponse(request.Id, result);
    }
}
