/// Windows-to-Mac Quartz gestures are optional. Mac-to-Windows contact capture
/// stays disabled; a Quartz receiver is not a registered virtual touchpad.
public enum TouchpadAvailability {
    public static let forwardingEnabled=false
    public static let receivesWindowsGestures=true
    public static let displayFeatures=["clipboard-v1","caps-v1","game-mode-v1","touchpad-v1","touchpad-quartz-receive-v1","touchpad-feedback-v1"]
}
