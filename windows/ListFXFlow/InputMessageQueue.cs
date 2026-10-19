namespace ListFXFlow;

/// Absolute pointer samples may replace their immediate predecessor. Every button,
/// key, layout, and focus transition is an ordering barrier and is never discarded.
public sealed class InputMessageQueue(int capacity=512)
{
    private readonly object gate=new();
    private readonly LinkedList<Message> messages=[];
    public bool TryEnqueue(Message message) {
        lock(gate) {
            if(message.Type=="pointer") {
                // A newer absolute position supersedes an older pointer sample even when
                // wheel messages were queued in between. Buttons, keys, Caps and layout
                // changes remain barriers so a click is never moved after the fact.
                var node=messages.Last;
                while(node!=null) {
                    var value=node.Value;
                    if(value.Type=="pointer") {
                        if(value.Active==message.Active) { node.Value=message; return true; }
                        break;
                    }
                    if(value.Type is "button" or "key" or "caps" or "reset" or "layout" or "display" or "touchpad" or "touchpadControl" or "gameMode" or "gameModeRequest" or "relativePointer") break;
                    node=node.Previous;
                }
            }
            if(message.Type=="relativePointer" && messages.Last is {Value.Type:"relativePointer"} motion &&
               Math.Abs(motion.Value.Dx!.Value+message.Dx!.Value)<=1000000 && Math.Abs(motion.Value.Dy!.Value+message.Dy!.Value)<=1000000) {
                motion.Value.Dx+=message.Dx;motion.Value.Dy+=message.Dy;return true;
            }
            if(message.Type=="scroll" && messages.Last is { Value.Type:"scroll" } tail && tail.Value.Dx!.Value*message.Dx!.Value>=0 && tail.Value.Dy!.Value*message.Dy!.Value>=0 && Math.Abs(tail.Value.Dx.Value+message.Dx.Value)<=10000 && Math.Abs(tail.Value.Dy.Value+message.Dy.Value)<=10000) {
                tail.Value.Dx+=message.Dx; tail.Value.Dy+=message.Dy; return true;
            }
            if(messages.Count>=capacity) return false;
            messages.AddLast(message); return true;
        }
    }
    public bool TryDequeue(out Message? message) {
        lock(gate) {
            if(messages.First==null) { message=null; return false; }
            message=messages.First.Value; messages.RemoveFirst(); return true;
        }
    }
    public bool HasPending { get { lock(gate) return messages.Count!=0; } }
    public void Clear() { lock(gate) messages.Clear(); }
}
