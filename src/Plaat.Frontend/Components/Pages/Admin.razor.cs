using Microsoft.AspNetCore.Components;

namespace Plaat.Frontend.Components.Pages;

public partial class Admin : ComponentBase
{
    private enum AdminTab
    {
        Tasks,
        Logs,
        CorruptTracks,
        ManageUsers,
        MonitoredFolders
    }

    private AdminTab _activeTab = AdminTab.Tasks;

    private void SetTab(AdminTab tab)
    {
        _activeTab = tab;
    }
}
