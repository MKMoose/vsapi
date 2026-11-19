using System;

namespace Vintagestory.API.Client;

public class ServerCellEntry : SavegameCellEntry
{
    public bool IsDirty;

    public ProbeState LastProbeState;
    public int ProbingProgress;

    public string? Name;
    public string? NetworkVersion;
    public string? ServerVersion;
    public int     Ping = -1;
    public int     PlayerCountCurrent;
    public int     PlayerCountMax;

    [Obsolete("Don't use this, will be ignored by the cell renderer.", true)]
    public new string? DetailText;
    [Obsolete("Don't use this, will be ignored by the cell renderer.", true)]
    public new CairoFont? TitleFont;
    [Obsolete("Don't use this, will be ignored by the cell renderer.", true)]
    public new CairoFont? DetailTextFont;

    public enum ProbeState
    {
        Unknown = default,
        InitialProbe,
        Errored,
        Success,
    }
}
