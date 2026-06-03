using Aion.Core;
using Aion.Core.Configuration;
using Aion.Core.Interfaces;
using Aion.Core.Memory;
using Aion.Core.Migrations;
using Aion.Core.Repair;
using Aion.Core.Safety;
using Aion.Core.Services;
using Aion.Core.Tools;
using Aion.Core.Tools.Builtin;
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
toolRegistry.RegisterAlias("search_web", "web_fetch");
toolRegistry.RegisterAlias("calculate", "calculator");
toolRegistry.RegisterAlias("time", "now");
toolRegistry.RegisterAlias("exec", "shell_command");
toolRegistry.RegisterAlias("sh", "shell_command");
toolRegistry.RegisterAlias("code", "sandbox");

// Sandbox (needed by DynamicTool for agent-created tools)
var sandbox = new SandboxTool();

// Register sandbox availability for agent SDK
builder.Services.AddSingleton<ISandboxExecutor>(new SandboxToolAdapter(sandbox));

// LLM
var llmService = new LlmService(appConfig, sanitizer);
var promptBuilder = new PromptBuilder();

// Agent loop
var agentLoop = new AgentLoop(
    llmService, promptBuilder, toolRegistry, repairer,
    scorer, safety, memoryStore, planStore, sanitizer, logger);

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
builder.Services.AddSingleton(toolRegistry);
builder.Services.AddSingleton(llmService);
builder.Services.AddSingleton(promptBuilder);
builder.Services.AddSingleton(agentLoop);
builder.Services.AddSingleton(appConfig);

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<MeshHub>("/hub/mesh");
app.MapHub<DashboardHub>("/hub/dashboard");

app.Urls.Add("http://0.0.0.0:6969");
if (appConfig.Mesh.Enabled)
    app.Urls.Add($"http://0.0.0.0:{appConfig.Mesh.Port}");

logger.Info("Startup", $"AION server starting on ports 6969{(appConfig.Mesh.Enabled ? $" + {appConfig.Mesh.Port}" : "")}", data: new { config = appConfig.Llm.Provider, model = appConfig.Llm.Model });
app.Run();
