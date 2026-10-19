import Foundation

/// A missed release must not permanently close the local screen seam. Only
/// remove cached holds; never invent presses from injected/global key state.
enum InputHoldReconciler {
    static func reconcile(keys: inout Set<Int>, buttons: inout Set<Int>,
                          keyDown: (Int) -> Bool, buttonDown: (Int) -> Bool) {
        keys = keys.filter(keyDown)
        buttons = buttons.filter(buttonDown)
    }
}
