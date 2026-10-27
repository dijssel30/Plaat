using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Plaat.Frontend.Components.Player;

public partial class SeekBar : ComponentBase
{
    [Inject]
    public required IJSRuntime JsRuntime { get; init; }

    [Parameter]
    public double Value { get; set; }

    [Parameter]
    public double Min { get; set; }

    [Parameter]
    public double Max { get; set; }

    [Parameter]
    public EventCallback<double> ValueChanged { get; set; }

    private bool _dragging;
    private ElementReference _barRef;

    private double ProgressPercent
    {
        get
        {
            return Max <= Min ? 0 : Math.Clamp((Value - Min) / (Max - Min), 0, 1);
        }
    }

    private async Task OnPointerDown(PointerEventArgs e)
    {
        _dragging = true;
        await UpdateValue(e);
    }

    private string GetProgressPercent()
    {
        return (ProgressPercent * 100).ToString("F2", CultureInfo.InvariantCulture);
    }

    private async Task OnPointerMove(PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        await UpdateValue(e);
    }

    private Task OnPointerUp(PointerEventArgs _)
    {
        _dragging = false;
        return Task.CompletedTask;
    }

    private async Task UpdateValue(PointerEventArgs e)
    {
        if (_barRef.Context is null)
        {
            return;
        }

        var rect = await JsRuntime.InvokeAsync<DomRect>("seekbar.getRect", _barRef);

        var relative = Math.Clamp((e.ClientX - rect.Left) / rect.Width, 0, 1);
        var newValue = Min + relative * (Max - Min);
        Value = newValue;

        await ValueChanged.InvokeAsync(newValue);

        await InvokeAsync(StateHasChanged);
    }

    private sealed class DomRect
    {
        public double Left { get; set; }
        public double Width { get; set; }
    }
}
