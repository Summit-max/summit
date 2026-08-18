namespace Summit.Models;

public class AppConfig
{
    public string Id { get; set; } = "singleton";
    public bool TestActive { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}

public class AppStatus
{
    public bool Active { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}
