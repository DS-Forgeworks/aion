namespace Aion.Core.Models;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "operator";
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
