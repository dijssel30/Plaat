namespace Plaat.Contract.Models;

public sealed record TasksStatusDto(
    bool ScanBusy,
    string? ScanCurrentFolder,
    bool ConvertBusy,
    IReadOnlyCollection<string> ConvertCurrentItems,
    bool LoudnessAnalysisBusy,
    int LoudnessAnalysisProcessed,
    int LoudnessAnalysisTotal);
