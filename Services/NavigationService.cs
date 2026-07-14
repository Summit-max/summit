using Summit.ViewModels;

namespace Summit.Services;

public class NavigationService
{
    private BaseViewModel? _currentView;
    private readonly Stack<BaseViewModel> _history = new();

    public BaseViewModel? CurrentView
    {
        get => _currentView;
        private set
        {
            _currentView = value;
            CurrentViewChanged?.Invoke(this, value);
        }
    }

    public bool CanGoBack => _history.Count > 0;

    public event EventHandler<BaseViewModel?>? CurrentViewChanged;

    public void NavigateTo(BaseViewModel viewModel)
    {
        if (_currentView != null) _history.Push(_currentView);
        CurrentView = viewModel;
    }

    public void NavigateTo<T>() where T : BaseViewModel, new() => NavigateTo(new T());

    public void GoBack()
    {
        if (_history.Count == 0) return;
        CurrentView = _history.Pop();
    }
}
