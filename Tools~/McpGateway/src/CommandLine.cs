namespace DotCraft.Unity.McpGateway;

internal sealed class CommandLine
{
    public const string Help = """
        Usage: dotcraft-unity <command> [options]

          mcp --project-root <path>               Run the stdio MCP server
          status                                 Inspect discovery and TCP reachability
          tools list                             List cached tool definitions
          tools describe <name>                  Show a cached tool schema
          call <name> [--arguments <json> | --arguments-file <file|->]
          exec (--code <code> | --path <script> | --stdin)
               [--args <json> | --args-file <file|->] [--mode editor|playmode]
          version                                Print build metadata

        CLI options: --project-root <path>, --json
        CLI commands discover the nearest Assets + ProjectSettings directory when
        --project-root is omitted. JSON file paths are relative to the shell's
        current directory; script paths are relative to the Unity project.
        '-' reads JSON from stdin. Only one input may consume stdin.
        Exit codes: 0 success, 1 tool/connection failure, 2 input error, 130 cancelled.
        Calls are never retried. Cancelling does not undo Unity changes.
        """;

    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    public string Name { get; private set; } = "help";
    public string? ToolName { get; private set; }
    public bool Json => _options.ContainsKey("--json");
    public string? Get(string option) => _options.GetValueOrDefault(option);
    public bool Has(string option) => _options.ContainsKey(option);

    public static CommandLine Parse(string[] args)
    {
        var command = new CommandLine();
        if (args.Length == 0 || args is ["--help"] or ["-h"] or ["help"])
            return command;
        command.Name = args[0];
        var index = 1;
        if (command.Name == "tools")
        {
            if (index >= args.Length || args[index] is not ("list" or "describe"))
                throw new ArgumentException("Expected 'tools list' or 'tools describe <name>'.");
            command.Name += " " + args[index++];
        }
        string[] allowed = command.Name switch
        {
            "mcp" => ["--project-root"],
            "version" => ["--json"],
            "status" or "tools list" or "tools describe" => ["--project-root", "--json"],
            "call" => ["--project-root", "--json", "--arguments", "--arguments-file"],
            "exec" => ["--project-root", "--json", "--code", "--path", "--stdin", "--args", "--args-file", "--mode"],
            _ => throw new ArgumentException($"Unknown command '{command.Name}'. Run dotcraft-unity --help.")
        };
        if (index < args.Length && args[index] is "--help" or "-h")
        {
            command.Name = "help";
            return command;
        }
        if (command.Name is "call" or "tools describe")
        {
            if (index >= args.Length || args[index].StartsWith('-') || string.IsNullOrWhiteSpace(args[index]))
                throw new ArgumentException("A tool name is required.");
            command.ToolName = args[index++];
        }
        while (index < args.Length)
        {
            var option = args[index++];
            if (!allowed.Contains(option))
                throw new ArgumentException($"Unknown option '{option}' for {command.Name}.");
            if (command.Has(option))
                throw new ArgumentException($"Duplicate option '{option}'.");
            if (option is "--json" or "--stdin")
                command._options.Add(option, "true");
            else
            {
                if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException($"A value is required for {option}.");
                command._options.Add(option, args[index++]);
            }
        }
        command.Exclusive("--arguments", "--arguments-file");
        command.Exclusive("--args", "--args-file");
        if (command.Name == "mcp" && !command.Has("--project-root"))
            throw new ArgumentException("mcp requires --project-root <path>.");
        if (command.Name == "exec")
        {
            if (new[] { "--code", "--path", "--stdin" }.Count(command.Has) != 1)
                throw new ArgumentException("exec requires exactly one of --code, --path, or --stdin.");
            if (command.Has("--stdin") && command.Get("--args-file") == "-")
                throw new ArgumentException("Code and args cannot both consume stdin.");
            if (command.Get("--mode") is { } mode && mode is not ("editor" or "playmode"))
                throw new ArgumentException("--mode must be editor or playmode.");
        }
        return command;
    }

    public string ResolveProjectRoot()
    {
        if (Get("--project-root") is { } path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                throw new ArgumentException($"Project root does not exist: {fullPath}");
            return fullPath;
        }
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets"))
                && Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                return directory.FullName;
        }
        throw new ArgumentException("No Unity project found above the current directory. Pass --project-root <path>.");
    }

    private void Exclusive(string first, string second)
    {
        if (Has(first) && Has(second))
            throw new ArgumentException($"Use {first} or {second}, not both.");
    }
}
