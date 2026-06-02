using System.CommandLine;
using Aion.Core.Configuration;

namespace Aion.Core.CLI;

public class AionCli
{
    private readonly ConfigManager _config;

    public AionCli(ConfigManager config)
    {
        _config = config;
    }

    public async Task<int> RunAsync(string[] args)
    {
        // Use simpler CLI — parse raw args for common commands
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        switch (args[0].ToLower())
        {
            case "config":
                return HandleConfig(args.Skip(1).ToArray());
            case "setup":
                await RunSetup(args.Skip(1).ToArray());
                return 0;
            case "status":
                ShowStatus();
                return 0;
            case "hello":
                await Hello(args.Skip(1).ToArray());
                return 0;
            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintUsage();
                return 1;
        }
    }

    private int HandleConfig(string[] args)
    {
        if (args.Length == 0)
        {
            ShowConfig();
            return 0;
        }

        switch (args[0].ToLower())
        {
            case "show":
                ShowConfig();
                return 0;
            case "set":
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: aion config set <key> <value>");
                    return 1;
                }
                SetConfig(args[1], args[2]);
                return 0;
            default:
                Console.Error.WriteLine($"Unknown config subcommand: {args[0]}");
                return 1;
        }
    }

    private void ShowConfig()
    {
        var cfg = _config.Load();
        Console.WriteLine($"Provider:   {cfg.Llm.Provider}");
        Console.WriteLine($"Model:      {cfg.Llm.Model}");
        Console.WriteLine($"Endpoint:   {cfg.Llm.Endpoint ?? "(default)"}");
        var key = cfg.Llm.ApiKey;
        Console.WriteLine($"API Key:    {(string.IsNullOrEmpty(key) ? "(not set)" : "****" + key[^4..])}");
        Console.WriteLine($"Safe Mode:  {cfg.Safety.SafeMode}");
        Console.WriteLine($"Shell:      {cfg.Safety.ShellEnabled}");
        Console.WriteLine($"Workspace:  {cfg.Workspace ?? "(default)"}");
        Console.WriteLine($"Language:   {cfg.Language ?? "en"}");
    }

    private void SetConfig(string key, string value)
    {
        var cfg = _config.Load();
        switch (key.ToLower())
        {
            case "llm.provider": cfg.Llm.Provider = value; break;
            case "llm.model": cfg.Llm.Model = value; break;
            case "llm.endpoint": cfg.Llm.Endpoint = value; break;
            case "llm.apikey": cfg.Llm.ApiKey = value; break;
            case "safety.safemode": cfg.Safety.SafeMode = bool.Parse(value); break;
            case "safety.shell": cfg.Safety.ShellEnabled = bool.Parse(value); break;
            case "workspace": cfg.Workspace = value; break;
            case "language": cfg.Language = value; break;
            default: Console.Error.WriteLine($"Unknown key: {key}"); return;
        }
        _config.Save(cfg);
        Console.WriteLine($"Set {key} = {value}");
    }

    private void ShowStatus()
    {
        Console.WriteLine("AION Status");
        Console.WriteLine("===========");
        Console.WriteLine($"PID:               {Environment.ProcessId}");
        Console.WriteLine($"Platform:          {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine($"Config path:       ~/.aion/aion-config.json");
        Console.WriteLine($"Config exists:     {_config.ConfigExists()}");
        if (_config.ConfigExists())
        {
            var cfg = _config.Load();
            Console.WriteLine($"LLM Provider:      {cfg.Llm.Provider}");
            Console.WriteLine($"LLM Model:         {cfg.Llm.Model}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("AION - Agent Swarm Operating System");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  config [show]       Show current configuration");
        Console.WriteLine("  config set <k> <v>  Set a config value");
        Console.WriteLine("  setup               Run setup wizard");
        Console.WriteLine("  status              Show system status");
        Console.WriteLine("  hello [out.json]    Write identity JSON");
    }

    public static async Task Hello(string[] args)
    {
        var resultPath = "/tmp/aion-hello.json";
        if (args.Length > 0) resultPath = args[0];

        var hello = new
        {
            agent = "AION",
            version = "1.0.0",
            timestamp = DateTime.UtcNow.ToString("O"),
            pid = Environment.ProcessId,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        };

        await File.WriteAllTextAsync(resultPath,
            System.Text.Json.JsonSerializer.Serialize(hello, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"AION hello written to {resultPath}");
    }

    public static async Task RunSetup(string[] args)
    {
        Console.WriteLine("AION Setup Wizard");
        Console.WriteLine("=================\n");

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion");
        Directory.CreateDirectory(configDir);

        var existingConfig = Path.Combine(configDir, "aion-config.json");
        if (File.Exists(existingConfig))
        {
            Console.Write("Existing config found. Overwrite? (y/N): ");
            var input = Console.ReadLine()?.Trim().ToLower();
            if (input != "y" && input != "yes")
            {
                Console.WriteLine("Setup cancelled.");
                return;
            }
        }

        var config = new Aion.Core.Models.AppConfig();

        Console.WriteLine("LLM Configuration");
        Console.WriteLine("-----------------");
        Console.Write("Provider (ollama/openai/deepseek) [ollama]: ");
        var provider = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(provider)) config.Llm.Provider = provider;

        Console.Write("Model [qwen3:8b]: ");
        var model = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(model)) config.Llm.Model = model;

        if (config.Llm.Provider != "ollama")
        {
            Console.Write("API Key: ");
            var key = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(key)) config.Llm.ApiKey = key;
        }

        Console.Write("Endpoint (optional): ");
        var endpoint = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(endpoint)) config.Llm.Endpoint = endpoint;

        Console.WriteLine();
        Console.Write("Safe mode (true/false) [true]: ");
        config.Safety.SafeMode = Console.ReadLine()?.Trim().ToLower() != "false";

        Console.Write("Shell commands enabled (true/false) [false]: ");
        config.Safety.ShellEnabled = Console.ReadLine()?.Trim().ToLower() == "true";

        var configManager = new ConfigManager(Path.Combine(configDir, "aion-config.json"));
        configManager.Save(config);

        Console.WriteLine("\nConfiguration saved to ~/.aion/aion-config.json");
        Console.WriteLine("Setup complete! Run 'aion status' to verify.");
    }
}
