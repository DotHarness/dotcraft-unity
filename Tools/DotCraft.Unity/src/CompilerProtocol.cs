using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotCraft.Unity.Cli;

internal static class CompilerProtocol
{
    public const int Version = GatewayConstants.ProtocolVersion;
}

internal sealed class CompilerRequest
{
    public int ProtocolVersion { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = "unity_execute_csharp";
    public List<string> References { get; set; } = [];
}

internal sealed class CompilerResponse
{
    public int ProtocolVersion { get; set; } = CompilerProtocol.Version;
    public bool Success { get; set; }
    public string? AssemblyBase64 { get; set; }
    public string? EntryType { get; set; }
    public List<CompilerDiagnostic> Diagnostics { get; set; } = [];
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

internal sealed class CompilerDiagnostic
{
    public string Id { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? Line { get; set; }
    public int? Column { get; set; }
}

internal static class CompilerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        CompilerResponse response;
        try
        {
            var json = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var request = JsonSerializer.Deserialize<CompilerRequest>(json, JsonOptions)
                ?? throw new JsonException("Compiler request is empty.");
            response = Compile(request);
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException exception)
        {
            response = Failure("InvalidArguments", exception.Message);
        }
        catch (Exception exception)
        {
            response = Failure("UnityCompilerFailed", exception.Message);
        }

        await Console.Out.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions).AsMemory(),
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    internal static CompilerResponse Compile(CompilerRequest request)
    {
        if (request.ProtocolVersion != CompilerProtocol.Version)
            return Failure("UnityCompilerProtocolMismatch", $"Unsupported compiler protocol {request.ProtocolVersion}.");
        if (string.IsNullOrWhiteSpace(request.Code))
            return Failure("InvalidArguments", "Compiler source must not be empty.");
        if (request.References is null || request.References.Count == 0)
            return Failure("InvalidArguments", "Compiler references must not be empty.");

        var references = request.References
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (references.Length == 0)
            return Failure("InvalidArguments", "Compiler references must not be empty.");

        var sourceLabel = string.IsNullOrWhiteSpace(request.SourceLabel)
            ? "unity_execute_csharp"
            : request.SourceLabel;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceLabel + "\n" + request.Code)))[..16];
        var className = "Snippet_" + hash;
        var entryType = $"DotCraft.Editor.Execution.Generated.{className}";
        var source = SnippetSourceBuilder.Build(
            className,
            request.Code,
            sourceLabel,
            runtimeNamespace: "DotCraft.Editor",
            generatedNamespace: "DotCraft.Editor.Execution.Generated");
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "DotCraftExecution_" + hash,
            [syntaxTree],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        var diagnostics = emitted.Diagnostics.Select(ToDiagnostic).ToList();
        if (!emitted.Success)
        {
            return new CompilerResponse
            {
                Success = false,
                ErrorCode = "CompilationFailed",
                ErrorMessage = "C# compilation failed.",
                Diagnostics = diagnostics
            };
        }

        return new CompilerResponse
        {
            Success = true,
            AssemblyBase64 = Convert.ToBase64String(stream.ToArray()),
            EntryType = entryType,
            Diagnostics = diagnostics
        };
    }

    private static CompilerDiagnostic ToDiagnostic(Diagnostic diagnostic)
    {
        int? line = null;
        int? column = null;
        if (diagnostic.Location is { IsInSource: true })
        {
            var span = diagnostic.Location.GetMappedLineSpan();
            if (span.IsValid)
            {
                line = span.StartLinePosition.Line + 1;
                column = span.StartLinePosition.Character + 1;
            }
        }
        return new CompilerDiagnostic
        {
            Id = diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(),
            Line = line,
            Column = column
        };
    }

    private static CompilerResponse Failure(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
