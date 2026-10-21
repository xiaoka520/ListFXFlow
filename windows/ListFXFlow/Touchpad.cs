namespace ListFXFlow;

public sealed record TouchpadContact(int Id,double X,double Y) {
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid=>Id is >=0 and <=65535 && double.IsFinite(X) && double.IsFinite(Y) && X is >=0 and <=1 && Y is >=0 and <=1;
}
internal sealed class TouchpadSender {
    internal bool Active { get;private set; }
    private bool waitForLift;
    private bool hasContacts;
    internal Message? Cancel() {
        waitForLift=hasContacts;
        if(!Active)return null;Active=false;
        return new("touchpad") { Phase="cancel",Contacts=[] };
    }
    internal Message? Sample(TouchpadContact[] contacts,bool allowed,int minimumContacts=2) {
        hasContacts=contacts.Length!=0;
        if(contacts.Length>5 || contacts.Any(c=>!c.Valid) || contacts.Select(c=>c.Id).Distinct().Count()!=contacts.Length)return Cancel();
        if(contacts.Length==0)waitForLift=false;
        if(!allowed)return Cancel();
        if(contacts.Length<minimumContacts) {
            if(!Active)return null;Active=false;waitForLift=contacts.Length!=0;
            return new("touchpad") { Phase="end",Contacts=[] };
        }
        if(waitForLift)return null;
        var m=new Message("touchpad") { Phase=Active ? "change":"begin",Contacts=contacts.OrderBy(c=>c.Id).ToArray() };Active=true;return m;
    }
}
