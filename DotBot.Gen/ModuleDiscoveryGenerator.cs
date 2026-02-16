using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotBot.Gen;

/// <summary>
/// Source generator that discovers DotBot modules marked with [DotBotModule] attribute
/// and generates partial method implementation for ModuleRegistry.
/// </summary>
[Generator]
public sealed class ModuleDiscoveryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all classes with [DotBotModule] attribute
        var moduleClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "DotBot.Modules.DotBotModuleAttribute",
                predicate: static (node, _) => IsPotentialModuleClass(node),
                transform: static (context, _) => GetModuleClassInfo(context))
            .Where(static m => m is not null)
            .Collect();

        // Find all classes with [HostFactory] attribute
        var hostFactoryClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "DotBot.Modules.HostFactoryAttribute",
                predicate: static (node, _) => IsPotentialHostFactoryClass(node),
                transform: static (context, _) => GetHostFactoryClassInfo(context))
            .Where(static f => f is not null)
            .Collect();

        // Combine modules and host factories
        var combined = moduleClasses.Combine(hostFactoryClasses);

        // Generate the source
        context.RegisterSourceOutput(combined, static (context, data) =>
        {
            GenerateModuleRegistrationProvider(context, data);
            GenerateModuleProperties(context, data.Left);
        });
    }

    private static bool IsPotentialModuleClass(SyntaxNode node)
    {
        return node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl
            && classDecl.AttributeLists.Count > 0;
    }

    private static bool IsPotentialHostFactoryClass(SyntaxNode node)
    {
        return node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl
            && classDecl.AttributeLists.Count > 0;
    }

    private static ModuleInfo? GetModuleClassInfo(GeneratorAttributeSyntaxContext context)
    {
        var symbol = context.TargetSymbol as INamedTypeSymbol;

        if (symbol == null)
            return null;

        // Find the DotBotModule attribute
        AttributeData? moduleAttr = null;
        foreach (var attr in context.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() == "DotBot.Modules.DotBotModuleAttribute")
            {
                moduleAttr = attr;
                break;
            }
        }

        if (moduleAttr == null)
            return null;

        // Extract name from constructor argument
        string moduleName = "unknown";
        if (moduleAttr.ConstructorArguments.Length > 0)
        {
            moduleName = moduleAttr.ConstructorArguments[0].Value?.ToString() ?? "unknown";
        }

        // Extract priority and description from named arguments
        int priority = 0;
        string? description = null;

        foreach (var namedArg in moduleAttr.NamedArguments)
        {
            if (namedArg.Key == "Priority")
            {
                priority = namedArg.Value.Value as int? ?? 0;
            }
            else if (namedArg.Key == "Description")
            {
                description = namedArg.Value.Value?.ToString();
            }
        }

        return new ModuleInfo(
            ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClassNamespace: symbol.ContainingNamespace.ToDisplayString(),
            ClassNameOnly: symbol.Name,
            ModuleName: moduleName,
            Priority: priority,
            Description: description);
    }

    private static HostFactoryInfo? GetHostFactoryClassInfo(GeneratorAttributeSyntaxContext context)
    {
        var symbol = context.TargetSymbol as INamedTypeSymbol;

        if (symbol == null)
            return null;

        // Find the HostFactory attribute
        AttributeData? factoryAttr = null;
        foreach (var attr in context.Attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() == "DotBot.Modules.HostFactoryAttribute")
            {
                factoryAttr = attr;
                break;
            }
        }

        if (factoryAttr == null)
            return null;

        // Extract module name from constructor argument
        string moduleName = "unknown";
        if (factoryAttr.ConstructorArguments.Length > 0)
        {
            moduleName = factoryAttr.ConstructorArguments[0].Value?.ToString() ?? "unknown";
        }

        return new HostFactoryInfo(
            ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ModuleName: moduleName);
    }

    private static void GenerateModuleRegistrationProvider(
        SourceProductionContext context,
        (ImmutableArray<ModuleInfo?> Left, ImmutableArray<HostFactoryInfo?> Right) data)
    {
        var modules = data.Left.Where(m => m != null).Select(m => m!).ToList();
        var hostFactories = data.Right.Where(f => f != null).Select(f => f!).ToList();

        if (modules.Count == 0)
        {
            // No modules found - don't generate any implementation
            // The partial method call will be removed by compiler, triggering reflection fallback
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("""
// <auto-generated>
//     This code was generated by DotBot.Gen.
//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// </auto-generated>
#nullable enable

using DotBot.Abstractions;

namespace DotBot.Modules.Registry;

public sealed partial class ModuleRegistry
{
    partial void RegisterSourceGeneratedModules()
    {
""");

        // Sort modules by name for stable output
        foreach (var module in modules.OrderBy(m => m.ModuleName))
        {
            // Find matching host factory
            var factory = hostFactories.FirstOrDefault(f => f.ModuleName == module.ModuleName);

            if (factory != null)
            {
                sb.AppendLine($"        AddModule(new {module.ClassName}(), new {factory.ClassName}());");
            }
            else
            {
                sb.AppendLine($"        AddModule(new {module.ClassName}(), null);");
            }
        }

        sb.AppendLine("""
    }
}
""");

        context.AddSource("ModuleRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateModuleProperties(
        SourceProductionContext context,
        ImmutableArray<ModuleInfo?> modules)
    {
        foreach (var module in modules.Where(m => m != null).Select(m => m!))
        {
            var sb = new StringBuilder();
            sb.AppendLine($$"""
// <auto-generated>
//     This code was generated by DotBot.Gen.
//     Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// </auto-generated>
#nullable enable

namespace {{module.ClassNamespace}};

partial class {{module.ClassNameOnly}}
{
    /// <summary>
    /// Gets the unique name of the module.
    /// </summary>
    public override string Name => "{{module.ModuleName}}";

    /// <summary>
    /// Gets the priority of the module for host selection.
    /// </summary>
    public override int Priority => {{module.Priority}};
}
""");

            var fileName = $"{module.ClassNameOnly}.ModuleProperties.g.cs";
            context.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    private sealed class ModuleInfo
    {
        public string ClassName { get; }
        public string ClassNamespace { get; }
        public string ClassNameOnly { get; }
        public string ModuleName { get; }
        public int Priority { get; }
        public string? Description { get; }

        public ModuleInfo(string ClassName, string ClassNamespace, string ClassNameOnly, string ModuleName, int Priority, string? Description)
        {
            this.ClassName = ClassName;
            this.ClassNamespace = ClassNamespace;
            this.ClassNameOnly = ClassNameOnly;
            this.ModuleName = ModuleName;
            this.Priority = Priority;
            this.Description = Description;
        }
    }

    private sealed class HostFactoryInfo
    {
        public string ClassName { get; }
        public string ModuleName { get; }

        public HostFactoryInfo(string ClassName, string ModuleName)
        {
            this.ClassName = ClassName;
            this.ModuleName = ModuleName;
        }
    }
}
