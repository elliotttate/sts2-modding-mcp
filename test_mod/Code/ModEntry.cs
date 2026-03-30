using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
    public static Harmony? GetHarmony() => _harmony;
    private static TcpListener? _listener;
    private static Thread? _serverThread;
    private static volatile bool _shutdownRequested;
    private static readonly string LogPath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "MCPTest", "mcptest.log");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            WriteLog("=== MCPTest v2.0 Initializing ===");

            // Capture main thread SynchronizationContext (MUST be done here, on the main thread)
            MainThreadDispatcher.Capture();

            ExceptionMonitor.Initialize();
            GameLogCapture.Initialize();
            EventTracker.Record("mod_init", "MCPTest initializing");

            // Register assembly version redirect early so hot-reloaded mods can
            // resolve dependencies with mismatched versions (e.g., mod built against
            // BaseLib 0.2.1.0 but game has BaseLib 0.1.0.0).
            // Use BOTH AppDomain.AssemblyResolve (fires for version mismatches in
            // the default ALC) and ALC.Resolving (fires for missing assemblies).
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var requestedName = new System.Reflection.AssemblyName(args.Name);
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == requestedName.Name);
            };
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
                AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name.Name);

            // Register all custom relics
            try
            {
                ModHelper.AddModelToPool<SharedRelicPool, McpTestRelic>();
                ModHelper.AddModelToPool<SharedRelicPool, BloodPact>();
                ModHelper.AddModelToPool<SharedRelicPool, GoldShield>();
                ModHelper.AddModelToPool<SharedRelicPool, ThornArmor>();
                ModHelper.AddModelToPool<SharedRelicPool, WarCry>();
                ModHelper.AddModelToPool<SharedRelicPool, VampiricBlade>();
                ModHelper.AddModelToPool<SharedRelicPool, SpellEcho>();
                ModHelper.AddModelToPool<SharedRelicPool, BerserkerRage>();
                ModHelper.AddModelToPool<SharedRelicPool, CounterStrike>();
                ModHelper.AddModelToPool<SharedRelicPool, WeakeningAura>();
                ModHelper.AddModelToPool<SharedRelicPool, HealingTouch>();
                WriteLog("Registered 11 custom relics in SharedRelicPool.");
            }
            catch (Exception ex2)
            {
                WriteLog($"Pool registration: {ex2.Message}");
            }

            _harmony = new Harmony("com.elliotttate.mcptest");
            _harmony.PatchAll();
            WriteLog("Harmony patches applied.");

            // Prevent Godot from throttling when window loses focus.
            // By default Godot 4 sleeps heavily when unfocused, blocking bridge transitions.
            try
            {
                // Disable low processor mode so the game keeps running at full speed in background
                Godot.OS.LowProcessorUsageMode = false;
                // Set unfocused FPS to something reasonable (default is often 0 or very low)
                Godot.Engine.MaxFps = 0; // uncapped
                WriteLog("Configured background processing (LowProcessorUsageMode=false).");
            }
            catch (Exception bgEx)
            {
                WriteLog($"Background processing setup: {bgEx.Message}");
            }

            StartBridgeServer();
            WriteLog("Bridge server started on port 21337.");

            Log.Warn("[MCPTest] v2.0 loaded! Bridge on port 21337.");
            WriteLog("=== MCPTest v2.0 Loaded ===");
            EventTracker.Record("mod_loaded", "MCPTest v2.0 loaded, bridge on port 21337");
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
        catch (Exception ex)
        {
            // Last-resort fallback: write to Godot's output so disk issues don't go silent
            GD.PrintErr($"[MCPTest] WriteLog failed ({ex.GetType().Name}): {message}");
        }
    }

    public static string GetLogPath()
        => LogPath;

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

            while (!_shutdownRequested)
            {
                // Use polling so the loop can check _shutdownRequested
                if (!_listener.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }
                var client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }
        catch (SocketException) when (_shutdownRequested)
        {
            // Expected during shutdown — listener was stopped
        }
        catch (Exception ex)
        {
            if (!_shutdownRequested)
                WriteLog($"Server error: {ex.Message}");
        }
        finally
        {
            WriteLog("TCP listener stopped.");
        }
    }

    /// <summary>
    /// Gracefully stop the bridge server. Safe to call multiple times.
    /// </summary>
    public static void Shutdown()
    {
        if (_shutdownRequested) return;
        _shutdownRequested = true;

        try { _listener?.Stop(); _listener?.Dispose(); } catch { }
        WriteLog("Bridge server shutdown requested.");
    }

    private static void HandleClient(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 60_000; // 60s timeout prevents hung client threads
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
            WriteLog($"Client error: {ex.Message}");
        }
    }
}
