using Microsoft.Extensions.Logging;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
    private readonly ILogger<LoginPage> _logger;

    public LoginPage(LoginViewModel viewModel, ILogger<LoginPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private async void OnLoginSucceeded()
    {
        try
        {
            await Shell.Current.GoToAsync("//home");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to home page after login");
        }
    }
}
