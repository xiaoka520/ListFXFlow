/// Cross-platform gesture forwarding is retained for development, but cannot be
/// enabled by a saved preference while native Mac reception is unavailable.
public enum TouchpadAvailability {
    public static let forwardingEnabled=false
    public static let displayFeatures=["clipboard-v1","caps-v1","game-mode-v1"]
}
