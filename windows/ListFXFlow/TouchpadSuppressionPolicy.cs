namespace ListFXFlow;

/// Local protection follows pointer ownership, independent of experimental
/// forwarding preferences or the receiving Mac's gesture capabilities.
internal static class TouchpadSuppressionPolicy {
    internal static bool Wanted(bool sharing,string active)=>sharing && active=="mac";
    internal static bool DropRawWheel(bool sharing,string active,bool owned,nint device)=>Wanted(sharing,active) && owned && device==0;
}
