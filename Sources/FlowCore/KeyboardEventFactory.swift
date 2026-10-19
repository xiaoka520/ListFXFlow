import CoreGraphics

public enum KeyboardEventFactory {
    public static func make(code: UInt16, down: Bool, repeated: Bool, flags: CGEventFlags,
                            source: CGEventSource? = nil) -> CGEvent? {
        guard let event=CGEvent(keyboardEventSource:source ?? CGEventSource(stateID:.combinedSessionState),virtualKey:code,keyDown:down) else{return nil}
        event.flags=applyingModifiers(flags,to:event.flags)
        event.setIntegerValueField(.keyboardEventAutorepeat,value:repeated ? 1 : 0)
        return event
    }
    public static func applyingModifiers(_ modifiers:CGEventFlags,to generated:CGEventFlags)->CGEventFlags {
        let managed:CGEventFlags=[.maskAlphaShift,.maskShift,.maskControl,.maskAlternate,.maskCommand]
        return generated.subtracting(managed).union(modifiers.intersection(managed))
    }
}
