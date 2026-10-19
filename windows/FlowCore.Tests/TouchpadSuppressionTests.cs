using ListFXFlow;

internal static class TouchpadSuppressionTests {
    internal static void Run(Action<bool,string> check) {
        check(TouchpadSuppressionPolicy.Wanted(true,"mac"),"Mac ownership automatically requests local suppression without a peer toggle");
        check(!TouchpadSuppressionPolicy.Wanted(true,"windows") && !TouchpadSuppressionPolicy.Wanted(false,"mac"),"returning to Windows or stopping sharing releases local suppression");
        check(TouchpadSuppressionPolicy.DropRawWheel(true,"mac",true,0),"precision-touchpad compatibility wheel is discarded while native suppression owns input");
        check(!TouchpadSuppressionPolicy.DropRawWheel(true,"mac",true,123),"a physical mouse wheel remains usable while touchpad gestures are suppressed");
        check(!TouchpadSuppressionPolicy.DropRawWheel(true,"windows",true,0) && !TouchpadSuppressionPolicy.DropRawWheel(false,"mac",true,0),"local or disconnected touchpad wheel remains unaffected");
        check(!TouchpadSuppressionPolicy.DropRawWheel(true,"mac",false,0),"a failed native acquire is not silently treated as successful suppression");
    }
}
