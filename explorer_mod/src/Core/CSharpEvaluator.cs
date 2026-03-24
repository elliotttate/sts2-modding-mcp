using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Godot;

namespace GodotExplorer.Core;

/// <summary>
/// C# REPL evaluator using Roslyn scripting. Loads Roslyn assemblies lazily
/// at runtime via reflection to avoid AssemblyLoadContext conflicts.
///
/// Instead of passing a globals object (which causes ALC cast errors),
/// we inject a preamble script that creates convenience variables using
/// only Godot/System types from the shared context.
/// </summary>
public class CSharpEvaluator
{
    private object? _state; // ScriptState<object>
    private object? _options; // ScriptOptions
    private readonly List<string> _history = new();
    private bool _roslynLoaded;
    private bool _preambleRun;

    // Reflected Roslyn types
    private Type? _csharpScriptType;
    private Type? _scriptOptionsType;

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// Preamble script that sets up convenience variables.
    /// Uses only Godot types (shared across all ALCs) to avoid cast issues.
    /// </summary>
    private const string Preamble = @"
var Tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
var Root = Tree.Root;
Godot.Node NodeAt(string path) => Root.GetNodeOrNull(path);
Godot.Collections.Array<Godot.Node> FindAll(string type) => Root.FindChildren(""*"", type, true, false);
Godot.Collections.Array<Godot.Node> Find(string pattern) => Root.FindChildren(""*"" + pattern + ""*"", """", true, false);
";

    public bool EnsureRoslynLoaded()
    {
        if (_roslynLoaded) return true;

        try
        {
            string? modDir = FindModDirectory();
            string? gameDataDir = FindGameDataDirectory();

            var searchDirs = new List<string>();
            if (modDir != null) searchDirs.Add(modDir);
            if (gameDataDir != null) searchDirs.Add(gameDataDir);

            // Register resolver for Roslyn's transitive dependencies
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                foreach (var dir in searchDirs)
                {
                    string path = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(path))
                        return ctx.LoadFromAssemblyPath(path);
                }
                return null;
            };

            // Load the scripting assembly
            Assembly? scriptingAsm = null;
            foreach (var dir in searchDirs)
            {
                string path = Path.Combine(dir, "Microsoft.CodeAnalysis.CSharp.Scripting.dll");
                if (File.Exists(path))
                {
                    scriptingAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    GD.Print($"[GodotExplorer] Loaded Roslyn from: {dir}");
                    break;
                }
            }

            if (scriptingAsm == null)
            {
                GD.PrintErr("[GodotExplorer] Microsoft.CodeAnalysis.CSharp.Scripting.dll not found!");
                GD.PrintErr("[GodotExplorer] Place Roslyn DLLs in the mod folder or game data directory.");
                return false;
            }

            _csharpScriptType = scriptingAsm.GetType("Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript");

            var scriptingBaseAsm = AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName("Microsoft.CodeAnalysis.Scripting"));
            _scriptOptionsType = scriptingBaseAsm.GetType("Microsoft.CodeAnalysis.Scripting.ScriptOptions");

            if (_csharpScriptType == null || _scriptOptionsType == null)
            {
                GD.PrintErr("[GodotExplorer] Failed to resolve Roslyn types.");
                return false;
            }

            _options = BuildScriptOptions();
            _roslynLoaded = true;
            GD.Print("[GodotExplorer] Roslyn C# scripting loaded successfully.");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] Failed to load Roslyn: {ex.Message}");
            return false;
        }
    }

    private object BuildScriptOptions()
    {
        // ScriptOptions.Default
        var defaultProp = _scriptOptionsType!.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
        var options = defaultProp!.GetValue(null)!;

        // .WithReferences — include all loaded assemblies from ALL load contexts
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .ToArray();
        var withRefs = _scriptOptionsType.GetMethods()
            .First(m => m.Name == "WithReferences" && m.GetParameters()[0].ParameterType == typeof(IEnumerable<Assembly>));
        options = withRefs.Invoke(options, new object[] { assemblies })!;

        // .WithImports
        string[] imports = {
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "Godot"
        };
        var withImports = _scriptOptionsType.GetMethods()
            .First(m => m.Name == "WithImports" && m.GetParameters()[0].ParameterType == typeof(IEnumerable<string>));
        options = withImports.Invoke(options, new object[] { imports })!;

        // .WithAllowUnsafe(true)
        var withUnsafe = _scriptOptionsType.GetMethod("WithAllowUnsafe");
        if (withUnsafe != null)
            options = withUnsafe.Invoke(options, new object[] { true })!;

        return options;
    }

    public async Task<string> EvaluateAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        if (!EnsureRoslynLoaded()) return "Error: Roslyn scripting not available. Deploy Microsoft.CodeAnalysis.*.dll files.";

        _history.Add(code);

        try
        {
            // Run the preamble first to set up convenience variables
            if (!_preambleRun)
            {
                _state = await RunScriptAsync(Preamble, null);
                _preambleRun = true;
            }

            // Run user code
            _state = await RunScriptAsync(code, _state);

            // Get ReturnValue
            var returnValue = _state!.GetType().GetProperty("ReturnValue")!.GetValue(_state);
            if (returnValue != null)
            {
                string result = FormatResult(returnValue);
                OutputReceived?.Invoke(result);
                return result;
            }

            return "(no return value)";
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            var inner = ex.InnerException;
            string error;

            if (inner.GetType().Name == "CompilationErrorException")
            {
                var diags = inner.GetType().GetProperty("Diagnostics")!.GetValue(inner);
                error = string.Join("\n", ((System.Collections.IEnumerable)diags!).Cast<object>().Select(d => d.ToString()));
            }
            else
            {
                error = $"{inner.GetType().Name}: {inner.Message}";
            }

            ErrorReceived?.Invoke(error);
            return $"Error: {error}";
        }
        catch (Exception ex)
        {
            string error = $"{ex.GetType().Name}: {ex.Message}";
            ErrorReceived?.Invoke(error);
            return $"Error: {error}";
        }
    }

    /// <summary>
    /// Run a script via reflection: CSharpScript.RunAsync or state.ContinueWithAsync
    /// No globals object — avoids ALC type identity conflicts.
    /// </summary>
    private async Task<object> RunScriptAsync(string code, object? previousState)
    {
        Task task;

        if (previousState == null)
        {
            // CSharpScript.RunAsync<object>(code, options)
            var runMethod = _csharpScriptType!.GetMethods()
                .Where(m => m.Name == "RunAsync" && m.IsGenericMethod)
                .First()
                .MakeGenericMethod(typeof(object));

            // Parameters: (string code, ScriptOptions options, object globals, Type globalsType, CancellationToken)
            task = (Task)runMethod.Invoke(null, new object?[] {
                code, _options, null, null, default(System.Threading.CancellationToken)
            })!;
        }
        else
        {
            // state.ContinueWithAsync<object>(code, options)
            var continueMethod = previousState.GetType().GetMethods()
                .Where(m => m.Name == "ContinueWithAsync" && m.IsGenericMethod)
                .First()
                .MakeGenericMethod(typeof(object));

            task = (Task)continueMethod.Invoke(previousState, new object?[] {
                code, _options, default(System.Threading.CancellationToken)
            })!;
        }

        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    /// <summary>
    /// Evaluate synchronously on the calling thread.
    /// Must be called from the main thread since scripts access Godot APIs.
    /// First evaluation may pause briefly for Roslyn compilation.
    /// </summary>
    public string EvaluateSync(string code)
    {
        try
        {
            return EvaluateAsync(code).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public void Reset()
    {
        _state = null;
        _preambleRun = false;
        if (_roslynLoaded)
            _options = BuildScriptOptions();
        OutputReceived?.Invoke("C# state reset.");
    }

    private static string FormatResult(object value)
    {
        if (value is null) return "(null)";
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var items = new List<string>();
            int count = 0;
            foreach (var item in enumerable)
            {
                items.Add(item?.ToString() ?? "(null)");
                count++;
                if (count >= 25) { items.Add($"... ({count}+ items)"); break; }
            }
            if (items.Count == 0) return "(empty collection)";
            return string.Join("\n", items);
        }
        return value.ToString() ?? "(null)";
    }

    private static string? FindModDirectory()
    {
        string? myPath = typeof(CSharpEvaluator).Assembly.Location;
        if (!string.IsNullOrEmpty(myPath))
            return Path.GetDirectoryName(myPath);
        return null;
    }

    private static string? FindGameDataDirectory()
    {
        string? exePath = Godot.OS.GetExecutablePath();
        if (string.IsNullOrEmpty(exePath)) return null;
        string? exeDir = Path.GetDirectoryName(exePath);
        if (exeDir == null) return null;
        string dataDir = Path.Combine(exeDir, "data_sts2_windows_x86_64");
        if (Directory.Exists(dataDir)) return dataDir;
        return exeDir;
    }
}
