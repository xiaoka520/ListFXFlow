using ListFXFlow;

internal static class TouchpadSuppressionTests {
    internal static void Run(Action<bool,string> check) {
        check(TouchpadSuppressionPolicy.Wanted(true,"mac"),"Mac ownership automatically requests local suppression without a peer toggle");
        check(!TouchpadSuppressionPolicy.Wanted(true,"windows") && !TouchpadSuppressionPolicy.Wanted(false,"mac"),"returning to Windows or stopping sharing releases local suppression");
        check(!TouchpadSuppressionPolicy.CaptureTwoFingerPointers(true),"global-gesture suppression leaves native Windows two-finger recognition enabled");
        check(TouchpadSuppressionPolicy.CaptureTwoFingerPointers(false),"experimental contact capture retains its explicit pointer registration");
    }
}
