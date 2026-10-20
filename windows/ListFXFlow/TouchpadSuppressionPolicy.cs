namespace ListFXFlow;

/// Local protection follows pointer ownership, independent of experimental
/// forwarding preferences or the receiving Mac's gesture capabilities.
internal static class TouchpadSuppressionPolicy {
    internal static bool Wanted(bool sharing,string active)=>sharing && active=="mac";
    // Suppressing global three/four-finger gestures does not require opting the
    // foreground window into two-finger pointers, which disables default scrolling.
    internal static bool CaptureTwoFingerPointers(bool suppressOnly)=>!suppressOnly;
}
