namespace JacquardStudio;

/// <summary>Weft fields can be edited as a draft without changing the applied machine setup.</summary>
public sealed record WeftSettings(bool AbsoluteLayout = true, bool BoxEnabled = false, int BoxPlacement = 0, string BoxHooks = "", string BoxCodes = "", string Sequence = "", bool StartFromBottom = false, string TotalHooks = "", string DesignStart = "", string WeftStart = "0", string WeftCount = "0")
{
    public static WeftSettings FromMachine(MachineSetup m) => new(m.AbsoluteLayout, m.BoxEnabled, m.BoxPlacement, m.BoxHooks, m.BoxCodes, m.Sequence, m.StartFromBottom, m.TotalHooks, m.DesignStart, m.WeftStart, m.WeftCount);
    public MachineSetup ApplyTo(MachineSetup m) => m with { AbsoluteLayout = AbsoluteLayout, BoxEnabled = BoxEnabled, BoxPlacement = BoxPlacement, BoxHooks = BoxHooks, BoxCodes = BoxCodes, Sequence = Sequence, StartFromBottom = StartFromBottom, TotalHooks = TotalHooks, DesignStart = DesignStart, WeftStart = WeftStart, WeftCount = WeftCount };
}
