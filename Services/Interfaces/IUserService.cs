using Summit.Models;

namespace Summit.Services.Interfaces;

public interface IUserService
{
    User? CurrentUser { get; }
    event EventHandler<User?> CurrentUserChanged;
    void SetCurrentUser(User? user);
    Task UpdateBioAsync(string bio);
    Task UpdateRoleAsync(string role);
}
