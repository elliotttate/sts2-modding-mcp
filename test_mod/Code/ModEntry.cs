using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MCPTest.Relics;

namespace MCPTest;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private static TcpListener? _listener;
    private static Thread? _serverThread;
    private static readonly string LogPath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "MCPTest", "mcptest.log");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            WriteLog("=== MCPTest Initializing ===");

            // Register custom content in pools (must happen before game init freezes pools)
            try
            {
                ModHelper.AddModelToPool<SharedRelicPool, McpTestRelic>();
                WriteLog("Registered McpTestRelic in SharedRelicPool.");
            }
            catch (Exception ex2)
            {
                WriteLog($"Pool registration (may be too late): {ex2.Message}");
            }

            _harmony = new Harmony("com.elliotttate.mcptest");
            _harmony.PatchAll();
            WriteLog("Harmony patches applied.");

            // Initialize main thread dispatcher for safe game state modification
            var sceneTree = Engine.GetMainLoop() as SceneTree;
            if (sceneTree != null)
            {
                MainThreadDispatcher.Initialize(sceneTree);
            }
            else
            {
                WriteLog("WARNING: SceneTree not available for dispatcher!");
            }

            StartBridgeServer();
            WriteLog("Bridge server started on port 21337.");

            Log.Warn("[MCPTest] Loaded successfully! Bridge on port 21337.");
            WriteLog("=== MCPTest Loaded ===");
        }
        catch (Exception ex)
        {
            Log.Error($"[MCPTest] Init failed: {ex}");
            WriteLog($"ERROR: {ex}");
        }
    }

    public static void WriteLog(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private static void StartBridgeServer()
    {
        _serverThread = new Thread(RunServer)
        {
            IsBackground = true,
            Name = "MCPTest-Bridge"
        };
        _serverThread.Start();
    }

    private static void RunServer()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, 21337);
            _listener.Start();
            WriteLog("TCP listener started.");

            while (true)
            {
                var client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Server error: {ex.Message}");
        }
    }

    private static void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var response = BridgeHandler.HandleRequest(line);
                    writer.WriteLine(response);
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Client handler error: {ex.Message}");
        }
    }
}
