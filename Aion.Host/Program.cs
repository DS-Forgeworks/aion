using Aion.Core;
using Aion.Core.Auth;
using Aion.Core.Configuration;
using Aion.Core.Interfaces;
using Aion.Core.Memory;
using Aion.Core.Migrations;
using Aion.Core.Middleware;
using Aion.Core.Repair;
using Aion.Core.Safety;
using Aion.Core.Services;
using Aion.Core.Tools;
using Aion.Core.Planning;
using Aion.Core.Tools.Builtin;
using Aion.Core.Mcp;
using Aion.Core.Mesh;

var builder = WebApplication.CreateBuilder(args);

// Config
var configDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion");
Directory.CreateDirectory(configDir);
var dbPath = Path.Combine(configDir, "aion.db");

var configManager = new ConfigManager(Path.Combine(configDir, "aion-config.json"));
var appConfig = configManager.Load();

// Database
var migrationEngine = new MigrationEngine($"Data Source={dbPath}");
migrationEngine.EnsureSchema();

// Core services
var logger = new AionLogger();
var sanitizer = new ContentSanitizer();
var repairer = new JsonRepairPipeline();
var scorer = new ConfidenceScorer();
var safety = new CapabilityGate();
var rateLimiter = new RateLimiter();
var auth = new AuthService($"Data Source={dbPath}");

// Configure rate limits
rateLimiter.Configure("tool_calls", 10, 30000);
rateLimiter.Configure("llm_requests", 30, 60000, 5);
rateLimiter.Configure("mesh_messages", 20, 10000);

// Memory
var memoryStore = new SqliteMemoryStore($"Data Source={dbPath}");
var planStore = new SqlitePlanStore($"Data Source={dbPath}");
var convStore = new SqliteConversationStore($"Data Source={dbPath}");

// Tools
var toolRegistry = new ToolRegistry();
toolRegistry.Register(new WebFetchTool());
toolRegistry.Register(new CalculatorTool());
toolRegistry.Register(new NowTool());
toolRegistry.Register(new ShellTool());
toolRegistry.Register(new SandboxTool());
toolRegistry.Register(new WebSearchTool());
toolRegistry.Register(new ReadFileTool());
toolRegistry.Register(new WriteFileTool());
toolRegistry.Register(new RememberTool());
toolRegistry.Register(new RecallTool());
toolRegistry.Register(new ScheduleTool());
toolRegistry.RegisterAlias("search_web", "web_fetch");
toolRegistry.RegisterAlias("calculate", "calculator");
toolRegistry.RegisterAlias("time", "now");
toolRegistry.RegisterAlias("exec", "shell_command");
toolRegistry.RegisterAlias("sh", "shell_command");
toolRegistry.RegisterAlias("code", "sandbox");
toolRegistry.RegisterAlias("read", "read_file");
toolRegistry.RegisterAlias("write", "write_file");
toolRegistry.RegisterAlias("search", "web_search");
toolRegistry.RegisterAlias("remind", "schedule");

// Common LLM hallucinated tool names
toolRegistry.RegisterAlias("file_read", "read_file");
toolRegistry.RegisterAlias("readfile", "read_file");
toolRegistry.RegisterAlias("file", "read_file");
toolRegistry.RegisterAlias("open", "read_file");
toolRegistry.RegisterAlias("writefile", "write_file");
toolRegistry.RegisterAlias("file_write", "write_file");
toolRegistry.RegisterAlias("save", "write_file");
toolRegistry.RegisterAlias("websearch", "web_search");
toolRegistry.RegisterAlias("ddg", "web_search");
toolRegistry.RegisterAlias("fetch", "web_fetch");
toolRegistry.RegisterAlias("get", "web_fetch");
toolRegistry.RegisterAlias("calc", "calculator");
toolRegistry.RegisterAlias("math", "calculator");
toolRegistry.RegisterAlias("datetime", "now");
toolRegistry.RegisterAlias("date", "now");
toolRegistry.RegisterAlias("current_time", "now");
toolRegistry.RegisterAlias("memorize", "remember");
toolRegistry.RegisterAlias("store", "remember");
toolRegistry.RegisterAlias("forget", "recall");
toolRegistry.RegisterAlias("timer", "schedule");
toolRegistry.RegisterAlias("delay", "schedule");
toolRegistry.RegisterAlias("bash", "shell_command");
toolRegistry.RegisterAlias("terminal", "shell_command");
toolRegistry.RegisterAlias("command", "shell_command");
toolRegistry.RegisterAlias("run", "shell_command");

// Sandbox (needed by DynamicTool for agent-created tools)
var sandbox = new SandboxTool();

// Register sandbox availability for agent SDK
builder.Services.AddSingleton<ISandboxExecutor>(new SandboxToolAdapter(sandbox));

// LLM
var llmService = new LlmService(appConfig, sanitizer);
var promptBuilder = new PromptBuilder();

// Load soul (identity/values/voice) and protocol (JSON format rules)
var baseDir = AppContext.BaseDirectory;
var soulPaths = new[] {
    Path.Combine(baseDir, "..", "..", "..", "..", "AION_SOUL.md"),
    Path.Combine(baseDir, "AION_SOUL.md"),
    Path.Combine(configDir, "AION_SOUL.md")
};
foreach (var p in soulPaths) { promptBuilder.LoadSoul(p); if (File.Exists(p)) break; }

var protocolPaths = new[] {
    Path.Combine(baseDir, "..", "..", "..", "..", "AION_PROTOCOL.md"),
    Path.Combine(baseDir, "AION_PROTOCOL.md"),
    Path.Combine(configDir, "AION_PROTOCOL.md")
};
foreach (var p in protocolPaths) { promptBuilder.LoadProtocol(p); if (File.Exists(p)) break; }

// Plan extractor
var planExtractor = new PlanExtractor(repairer, logger);

// Agent loop
var agentLoop = new AgentLoop(
    llmService, promptBuilder, toolRegistry, planExtractor,
    scorer, safety, memoryStore, planStore, sanitizer, logger);

// MCP Manager (external tool servers)
var mcpLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<McpManager>();
var mcpManager = new McpManager(mcpLogger);

// Task Scheduler (recurring background agent jobs)
var taskScheduler = new AionTaskScheduler();

// Mesh
var meshHub = new MeshHub(logger);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddSingleton<IAionLogger>(logger);
builder.Services.AddSingleton(repairer as IJsonRepairer);
builder.Services.AddSingleton(scorer as IConfidenceScorer);
builder.Services.AddSingleton(safety as ISafetyGate);
builder.Services.AddSingleton(rateLimiter as IRateLimiter);
builder.Services.AddSingleton(memoryStore as IMemoryStore);
builder.Services.AddSingleton(planStore as IPlanStore);
builder.Services.AddSingleton(convStore as IConversationStore);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(toolRegistry);
builder.Services.AddSingleton(mcpManager);
builder.Services.AddSingleton(taskScheduler);
builder.Services.AddSingleton(llmService);
builder.Services.AddSingleton(promptBuilder);
builder.Services.AddSingleton(agentLoop);
builder.Services.AddSingleton(appConfig);

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSignalR();

var app = builder.Build();
app.UseCors();
app.UseAionAuth();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<MeshHub>("/hub/mesh");
app.MapHub<DashboardHub>("/hub/dashboard");

// SPA fallback: any unknown route serves index.html so React can handle it
app.MapFallbackToFile("index.html");

app.Urls.Add("http://0.0.0.0:6969");
if (appConfig.Mesh.Enabled)
    app.Urls.Add($"http://0.0.0.0:{appConfig.Mesh.Port}");

logger.Info("Startup", $"AION server starting on ports 6969{(appConfig.Mesh.Enabled ? $" + {appConfig.Mesh.Port}" : "")}", data: new { config = appConfig.Llm.Provider, model = appConfig.Llm.Model });
app.Run();
