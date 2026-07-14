using Summit.Models;
using Summit.Services.Interfaces;

namespace Summit.Services;

public class UserService : IUserService
{
    public User? CurrentUser { get; private set; }

    public event EventHandler<User?>? CurrentUserChanged;

    public void SetCurrentUser(User? user)
    {
        CurrentUser = user;
        CurrentUserChanged?.Invoke(this, user);
    }

    public async Task UpdateBioAsync(string bio)
    {
        if (CurrentUser == null) return;
        CurrentUser.Bio = bio;
        await App.UserRepository.UpdateAsync(CurrentUser);
    }

    public async Task UpdateRoleAsync(string role)
    {
        if (CurrentUser == null) return;
        CurrentUser.PrimaryRole = role;
        await App.UserRepository.UpdateAsync(CurrentUser);
    }
}
