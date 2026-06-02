using System.Text.Json;
using Aion.Core.Models;

namespace Aion.Core.Configuration;

public class ConfigManager
{
    private readonly string _configPath;
    private AppConfig? _cached;

    public ConfigManager(string configPath = "~/.aion/aion-config.json")
    {
        _configPath = configPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public AppConfig Load()
    {
        if (_cached != null) return _cached;

        if (!File.Exists(_configPath))
        {
            _cached = new AppConfig();
            Save(_cached);
            return _cached;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _cached = JsonSerializer.Deserialize<AppConfig>(json, opts) ?? new AppConfig();
            return _cached;
        }
        catch
        {
            _cached = new AppConfig();
            return _cached;
        }
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath + ".tmp", json);

        if (File.Exists(_configPath))
            File.Copy(_configPath, _configPath + ".bak", true);

        File.Move(_configPath + ".tmp", _configPath, true);
        _cached = config;
    }

    public bool ConfigExists()
    {
        return File.Exists(_configPath);
    }

    public void Validate(AppConfig config)
    {
        if (config.Llm.Provider == "openai" && string.IsNullOrEmpty(config.Llm.ApiKey))
            throw new InvalidOperationException("OpenAI provider requires an API key");

        if (config.Version < 1)
            throw new InvalidOperationException("Config version must be >= 1");
    }
}
