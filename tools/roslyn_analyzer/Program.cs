using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ── CLI ──────────────────────────────────────────────────────────────────────

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RoslynAnalyzer <decompiled_dir> <output_json>");
    return 1;
}

var decompiledDir = args[0];
var outputPath = args[1];

if (!Directory.Exists(decompiledDir))
{
    Console.Error.WriteLine($"Directory not found: {decompiledDir}");
    return 1;
}

var sw = Stopwatch.StartNew();
var analyzer = new DecompiledAnalyzer(decompiledDir);
var index = analyzer.Analyze();
sw.Stop();

index.Stats["elapsed_ms"] = sw.ElapsedMilliseconds.ToString();

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
};

var json = JsonSerializer.Serialize(index, options);
File.WriteAllText(outputPath, json);

Console.WriteLine($"Indexed {index.Stats["total_classes"]} classes, " +
                  $"{index.Stats["total_methods"]} methods, " +
                  $"{index.Stats["total_enums"]} enums " +
                  $"in {sw.ElapsedMilliseconds}ms → {outputPath}");
return 0;

// ── Data Model ───────────────────────────────────────────────────────────────

record RoslynIndex(
    int Version,
    string GeneratedAt,
    Dictionary<string, ClassInfo> Classes,
    Dictionary<string, EnumInfo> Enums,
    Dictionary<string, object> Stats
);

record ClassInfo(
    string Name,
    string Namespace,
    string File,
    List<string> Modifiers,
    string? BaseClass,
    List<string> Interfaces,
    List<AttributeInfo> Attributes,
    List<string> ConstructorBaseArgs,
    List<PropertyData> Properties,
    List<MethodData> Methods,
    List<FieldData> Fields,
    List<string> TypeReferences
);

record EnumInfo(
    string Name,
    string Namespace,
    string File,
    List<string> Members
);

record AttributeInfo(string Name, List<string> Args);

record PropertyData(
    string Name,
    string Type,
    List<string> Modifiers,
    string? ExpressionValue
);

record MethodData(
    string Name,
    string ReturnType,
    List<string> Modifiers,
    List<ParameterData> Parameters,
    List<string> Invocations,
    int LineStart,
    int LineEnd
);

record ParameterData(string Type, string Name);

record FieldData(
    string Name,
    string Type,
    List<string> Modifiers,
    string? InitializerValue
);

// ── Analyzer ─────────────────────────────────────────────────────────────────

class DecompiledAnalyzer
{
    private readonly string _rootDir;

    public DecompiledAnalyzer(string rootDir) => _rootDir = rootDir;

    public RoslynIndex Analyze()
    {
        var classes = new Dictionary<string, ClassInfo>();
        var enums = new Dictionary<string, EnumInfo>();
        int totalMethods = 0;
        int parseErrors = 0;

        var csFiles = Directory.EnumerateFiles(_rootDir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        Console.Error.WriteLine($"Parsing {csFiles.Count} files...");

        foreach (var filePath in csFiles)
        {
            try
            {
                var source = File.ReadAllText(filePath);
                var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
                var root = tree.GetRoot();
                var relativePath = Path.GetRelativePath(_rootDir, filePath).Replace('\\', '/');

                // Extract namespace
                var ns = ExtractNamespace(root);

                // Process classes
                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var info = ExtractClass(classDecl, ns, relativePath);
                    if (info != null)
                    {
                        classes[info.Name] = info;
                        totalMethods += info.Methods.Count;
                    }
                }

                // Process enums
                foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                {
                    var info = ExtractEnum(enumDecl, ns, relativePath);
                    if (info != null)
                        enums[info.Name] = info;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Error parsing {filePath}: {ex.Message}");
                parseErrors++;
            }
        }

        return new RoslynIndex(
            Version: 2,
            GeneratedAt: DateTime.UtcNow.ToString("o"),
            Classes: classes,
            Enums: enums,
            Stats: new Dictionary<string, object>
            {
                ["total_files"] = csFiles.Count.ToString(),
                ["total_classes"] = classes.Count.ToString(),
                ["total_methods"] = totalMethods.ToString(),
                ["total_enums"] = enums.Count.ToString(),
                ["parse_errors"] = parseErrors.ToString(),
            }
        );
    }

    // ── Namespace ────────────────────────────────────────────────────────────

    private static string ExtractNamespace(SyntaxNode root)
    {
        // File-scoped namespace (C# 10+): namespace X.Y.Z;
        var fileScopedNs = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNs != null)
            return fileScopedNs.Name.ToString();

        // Block namespace: namespace X.Y.Z { ... }
        var blockNs = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        if (blockNs != null)
            return blockNs.Name.ToString();

        return "";
    }

    // ── Class ────────────────────────────────────────────────────────────────

    private ClassInfo? ExtractClass(ClassDeclarationSyntax classDecl, string ns, string relativePath)
    {
        var name = classDecl.Identifier.Text;
        if (string.IsNullOrEmpty(name))
            return null;

        var modifiers = classDecl.Modifiers.Select(m => m.Text).ToList();

        // Base class and interfaces
        string? baseClass = null;
        var interfaces = new List<string>();
        if (classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = GetSimpleTypeName(baseType.Type);
                // Heuristic: interfaces start with I followed by uppercase
                if (typeName.Length >= 2 && typeName[0] == 'I' && char.IsUpper(typeName[1]))
                    interfaces.Add(typeName);
                else if (baseClass == null)
                    baseClass = typeName;
                else
                    interfaces.Add(typeName);
            }
        }

        // Attributes
        var attributes = ExtractAttributes(classDecl.AttributeLists);

        // Constructor base args
        var ctorBaseArgs = new List<string>();
        var ctor = classDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor?.Initializer is { ArgumentList.Arguments: var initArgs })
        {
            ctorBaseArgs = initArgs.Select(a => a.Expression.ToString()).ToList();
        }

        // Properties
        var properties = new List<PropertyData>();
        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            properties.Add(ExtractProperty(prop));
        }

        // Methods
        var methods = new List<MethodData>();
        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            methods.Add(ExtractMethod(method));
        }

        // Fields
        var fields = new List<FieldData>();
        foreach (var fieldDecl in classDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in fieldDecl.Declaration.Variables)
            {
                fields.Add(new FieldData(
                    Name: variable.Identifier.Text,
                    Type: fieldDecl.Declaration.Type.ToString(),
                    Modifiers: fieldDecl.Modifiers.Select(m => m.Text).ToList(),
                    InitializerValue: variable.Initializer?.Value.ToString()
                ));
            }
        }

        // Type references — collect from all relevant syntax nodes
        var typeRefs = CollectTypeReferences(classDecl);

        return new ClassInfo(
            Name: name,
            Namespace: ns,
            File: relativePath,
            Modifiers: modifiers,
            BaseClass: baseClass,
            Interfaces: interfaces,
            Attributes: attributes,
            ConstructorBaseArgs: ctorBaseArgs,
            Properties: properties,
            Methods: methods,
            Fields: fields,
            TypeReferences: typeRefs
        );
    }

    // ── Property ─────────────────────────────────────────────────────────────

    private static PropertyData ExtractProperty(PropertyDeclarationSyntax prop)
    {
        string? exprValue = null;

        // Expression-bodied: Type Name => expr;
        if (prop.ExpressionBody != null)
        {
            exprValue = prop.ExpressionBody.Expression.ToString();
        }
        // Arrow in getter: Type Name { get => expr; }
        else if (prop.AccessorList?.Accessors is { } accessors)
        {
            var getter = accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody != null)
                exprValue = getter.ExpressionBody.Expression.ToString();
        }

        return new PropertyData(
            Name: prop.Identifier.Text,
            Type: prop.Type.ToString(),
            Modifiers: prop.Modifiers.Select(m => m.Text).ToList(),
            ExpressionValue: exprValue
        );
    }

    // ── Method ───────────────────────────────────────────────────────────────

    private static MethodData ExtractMethod(MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters
            .Select(p => new ParameterData(
                Type: p.Type?.ToString() ?? "?",
                Name: p.Identifier.Text
            ))
            .ToList();

        var invocations = new List<string>();
        var seen = new HashSet<string>();

        // Collect invocations from body or expression body
        SyntaxNode? searchNode = method.Body as SyntaxNode ?? method.ExpressionBody;
        if (searchNode != null)
        {
            foreach (var invocation in searchNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var callText = ExtractInvocationTarget(invocation);
                if (callText != null && seen.Add(callText))
                    invocations.Add(callText);
            }

            // Also capture object creations as type references in calls
            foreach (var creation in searchNode.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var typeName = GetSimpleTypeName(creation.Type);
                var entry = $"new {typeName}";
                if (seen.Add(entry))
                    invocations.Add(entry);
            }
        }

        var lineSpan = method.GetLocation().GetLineSpan();

        return new MethodData(
            Name: method.Identifier.Text,
            ReturnType: method.ReturnType.ToString(),
            Modifiers: method.Modifiers.Select(m => m.Text).ToList(),
            Parameters: parameters,
            Invocations: invocations,
            LineStart: lineSpan.StartLinePosition.Line + 1,
            LineEnd: lineSpan.EndLinePosition.Line + 1
        );
    }

    /// <summary>
    /// Extract the target of an invocation, e.g. "DamageCmd.Attack" from DamageCmd.Attack(...).
    /// For chained calls like a.B().C(), extracts "C" for the outer and "a.B" for the inner.
    /// </summary>
    private static string? ExtractInvocationTarget(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            // Type.Method() or obj.Method()
            MemberAccessExpressionSyntax memberAccess => ExtractMemberAccessTarget(memberAccess),
            // Standalone Method()
            IdentifierNameSyntax id => id.Identifier.Text,
            // Generic: Method<T>()
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => null,
        };
    }

    private static string ExtractMemberAccessTarget(MemberAccessExpressionSyntax memberAccess)
    {
        var memberName = memberAccess.Name switch
        {
            GenericNameSyntax g => g.Identifier.Text + "<" +
                string.Join(", ", g.TypeArgumentList.Arguments.Select(a => GetSimpleTypeName(a))) + ">",
            _ => memberAccess.Name.Identifier.Text,
        };

        // Try to get the receiver as a simple name (Type.Method or var.Method)
        return memberAccess.Expression switch
        {
            IdentifierNameSyntax receiver => $"{receiver.Identifier.Text}.{memberName}",
            // base.Method
            BaseExpressionSyntax => $"base.{memberName}",
            // this.Method
            ThisExpressionSyntax => $"this.{memberName}",
            // For chained calls (a().B), just return the method name
            _ => memberName,
        };
    }

    // ── Enum ─────────────────────────────────────────────────────────────────

    private EnumInfo? ExtractEnum(EnumDeclarationSyntax enumDecl, string ns, string relativePath)
    {
        var name = enumDecl.Identifier.Text;
        if (string.IsNullOrEmpty(name))
            return null;

        var members = enumDecl.Members.Select(m => m.Identifier.Text).ToList();

        return new EnumInfo(
            Name: name,
            Namespace: ns,
            File: relativePath,
            Members: members
        );
    }

    // ── Attributes ───────────────────────────────────────────────────────────

    private static List<AttributeInfo> ExtractAttributes(SyntaxList<AttributeListSyntax> attrLists)
    {
        var result = new List<AttributeInfo>();
        foreach (var list in attrLists)
        {
            foreach (var attr in list.Attributes)
            {
                var attrName = attr.Name.ToString();
                var attrArgs = attr.ArgumentList?.Arguments
                    .Select(a => a.ToString())
                    .ToList() ?? [];
                result.Add(new AttributeInfo(attrName, attrArgs));
            }
        }
        return result;
    }

    // ── Type References ──────────────────────────────────────────────────────

    private static List<string> CollectTypeReferences(ClassDeclarationSyntax classDecl)
    {
        var refs = new HashSet<string>();

        // Base list types
        if (classDecl.BaseList != null)
        {
            foreach (var bt in classDecl.BaseList.Types)
                CollectTypeNames(bt.Type, refs);
        }

        // Property types
        foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
            CollectTypeNames(prop.Type, refs);

        // Method return types and parameter types
        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            CollectTypeNames(method.ReturnType, refs);
            foreach (var param in method.ParameterList.Parameters)
                if (param.Type != null)
                    CollectTypeNames(param.Type, refs);
        }

        // Field types
        foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
            CollectTypeNames(field.Declaration.Type, refs);

        // Object creation expressions: new XType(...)
        foreach (var creation in classDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            CollectTypeNames(creation.Type, refs);

        // Generic type arguments: Apply<VulnerablePower>
        foreach (var generic in classDecl.DescendantNodes().OfType<GenericNameSyntax>())
        {
            foreach (var typeArg in generic.TypeArgumentList.Arguments)
                CollectTypeNames(typeArg, refs);
        }

        // typeof(X) expressions
        foreach (var typeofExpr in classDecl.DescendantNodes().OfType<TypeOfExpressionSyntax>())
            CollectTypeNames(typeofExpr.Type, refs);

        // Cast expressions: (X)value
        foreach (var cast in classDecl.DescendantNodes().OfType<CastExpressionSyntax>())
            CollectTypeNames(cast.Type, refs);

        // Remove C# built-in type names
        refs.ExceptWith(["string", "int", "bool", "void", "decimal", "double", "float",
            "long", "short", "byte", "uint", "ulong", "ushort", "sbyte", "char",
            "object", "dynamic", "var", "nint", "nuint"]);

        return refs.OrderBy(r => r).ToList();
    }

    private static void CollectTypeNames(TypeSyntax type, HashSet<string> refs)
    {
        switch (type)
        {
            case IdentifierNameSyntax id:
                refs.Add(id.Identifier.Text);
                break;
            case GenericNameSyntax generic:
                refs.Add(generic.Identifier.Text);
                foreach (var arg in generic.TypeArgumentList.Arguments)
                    CollectTypeNames(arg, refs);
                break;
            case QualifiedNameSyntax qualified:
                // e.g. global::Namespace.Type — take the rightmost name
                CollectTypeNames(qualified.Right, refs);
                break;
            case ArrayTypeSyntax array:
                CollectTypeNames(array.ElementType, refs);
                break;
            case NullableTypeSyntax nullable:
                CollectTypeNames(nullable.ElementType, refs);
                break;
            case TupleTypeSyntax tuple:
                foreach (var elem in tuple.Elements)
                    CollectTypeNames(elem.Type, refs);
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Get the simple (unqualified) type name, stripping generics and qualifications.</summary>
    private static string GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text +
                "<" + string.Join(", ", generic.TypeArgumentList.Arguments.Select(GetSimpleTypeName)) + ">",
            QualifiedNameSyntax q => GetSimpleTypeName(q.Right),
            NullableTypeSyntax n => GetSimpleTypeName(n.ElementType) + "?",
            ArrayTypeSyntax a => GetSimpleTypeName(a.ElementType) + "[]",
            _ => type.ToString(),
        };
    }
}
