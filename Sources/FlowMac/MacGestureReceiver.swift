import Foundation
import FlowCore

/// Owns recognition/output separately from mouse routing. Feedback distinguishes
/// receipt from submission; CGEvent posting does not prove OS/app acceptance.
final class MacGestureReceiver {
    private var recognizer=TouchpadRecognizer()
    private let submit:(NativeTouchpadGesture)->Bool
    private var epoch:Int64?
    private var lastSourceTime:Double?
    private var frames:Int64=0,events:Int64=0
    private var unavailable=false
    private var lastFeedback = -Double.greatestFiniteMagnitude
    init(submit:@escaping (NativeTouchpadGesture)->Bool) { self.submit=submit }
    func reset() {
        for gesture in recognizer.reset() { _=submit(gesture) }
        epoch=nil;lastSourceTime=nil;frames=0;events=0;unavailable=false;lastFeedback = -Double.greatestFiniteMagnitude
    }
    func receive(_ message:Message,at time:Double=ProcessInfo.processInfo.systemUptime)->Message? {
        guard message.type=="touchpad",(try? message.validate()) != nil else{return nil}
        if message.phase=="begin" {
            reset();epoch=message.gestureEpoch
        } else if message.gestureEpoch != epoch { return nil }
        if let sample=message.gestureTime {
            if let previous=lastSourceTime,sample<previous { reset();return nil }
            lastSourceTime=sample
        }
        frames=min(frames+1,1_000_000_000)
        for gesture in recognizer.receive(message,at:message.gestureTime ?? time) {
            if submit(gesture) { events=min(events+1,1_000_000_000) } else { unavailable=true }
        }
        guard let epoch else{return nil} // Older peers do not negotiate feedback.
        if message.phase=="change",!unavailable,time-lastFeedback<0.25 { return nil }
        lastFeedback=time
        var feedback=Message("touchpadFeedback");feedback.gestureEpoch=epoch;feedback.frames=frames;feedback.events=events;feedback.backend="quartz"
        feedback.phase=unavailable ? "unavailable":events>0 ? "submitted":"received"
        return feedback
    }
}
