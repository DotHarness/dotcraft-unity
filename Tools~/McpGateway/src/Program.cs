using System.Text;

namespace DotCraft.Unity.McpGateway;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        var json = args.Contains("--json");
        try
        {
            var command = CommandLine.Parse(args);
            if (command.Name == "help")
            {
                Console.WriteLine(CommandLine.Help);
                return 0;
            }
            if (command.Name == "mcp")
                return await McpHost.RunAsync(command.ResolveProjectRoot());

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                return await CliRunner.RunAsync(command, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return CliRunner.WriteError(json, "Cancelled",
                    "The client stopped waiting. Unity work already started may still have executed; do not replay automatically.", 130);
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return CliRunner.WriteError(json, "InvalidArguments", ex.Message, 2);
        }
    }
}
