namespace ListFXFlow;

/// Acknowledges native API submissions, not an application's visual response.
/// Epochs prevent delayed feedback from acknowledging a newer ownership session.
internal sealed class TouchpadFeedbackCounter(string backend) {
    private long? epoch;
    private long frames,events,last;
    internal Message? Applied(Message message,int nativeSubmissions,long now) {
        if(message.GestureEpoch is not {} next)return null;
        if(message.Phase=="begin") { epoch=next;frames=events=0;last=long.MinValue/2; }
        else if(next!=epoch)return null;
        frames=Math.Min(frames+1,1_000_000_000);events=Math.Min(events+Math.Max(nativeSubmissions,0),1_000_000_000);
        if(message.Phase=="change" && now-last<250)return null;
        last=now;
        return new("touchpadFeedback") {GestureEpoch=epoch,Frames=frames,Events=events,Phase=events>0 ? "submitted":"received",Backend=backend};
    }
}
