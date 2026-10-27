using Plaat.Contract;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views.TrackActions;

public partial class TrackDetailsPage : ContentPage
{
    private readonly TrackDetailsViewModel _viewModel;

    public TrackDetailsPage(TrackDetailsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    private void OnSetDefaultClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TrackLocationDto location })
        {
            _viewModel.SetDefaultCommand.Execute(location);
        }
    }
}
