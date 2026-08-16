namespace Summit.Models;

public class AuthResult
{
    public User User { get; set; } = new();
    public string Token { get; set; } = string.Empty;
}
