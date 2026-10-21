using System.IO;

namespace ListFXFlow;

/// Decoded HID fields, before OS gesture classification. ContactCount includes
/// valid contact records (including lifts), not just fingers currently down.
internal sealed record PtpHidContact(uint Id,bool Tip,bool Confidence,double X,double Y);
internal sealed record PtpHidFragment(ushort ScanTime,int ContactCount,PtpHidContact[] Contacts);
internal sealed class PtpHidFrames {
    private readonly Dictionary<uint,int> identities=[];
    private readonly List<PtpHidContact> pending=[];
    private int expected;
    private ushort? scan;
    private ulong? sequence;
    internal void Reset() { identities.Clear();pending.Clear();expected=0;scan=null;sequence=null; }
    internal TouchpadContact[]? Push(ulong reportSequence,PtpHidFragment fragment) {
        if(reportSequence==0 || sequence is {} old && reportSequence!=old+1) { Reset();throw new InvalidDataException("原始触点报告丢失或顺序异常"); }
        sequence=reportSequence;
        if(fragment.ContactCount is <0 or >5 || fragment.Contacts.Length>5)throw Invalid("无效的原始触点数量");
        if(fragment.ContactCount>0) {
            if(expected>0)throw Invalid("上一帧原始触点不完整");
            expected=fragment.ContactCount;scan=fragment.ScanTime;pending.Clear();
        } else if(expected==0) {
            if(fragment.Contacts.Any(c=>c.Tip))throw Invalid("原始触点缺少帧头");
            identities.Clear();return [];
        }
        if(scan!=fragment.ScanTime)throw Invalid("混合触点帧的扫描时间不一致");
        int take=Math.Min(expected-pending.Count,fragment.Contacts.Length);
        if(take==0)throw Invalid("原始触点帧没有有效记录");
        for(int i=0;i<take;i++) {
            var p=fragment.Contacts[i];
            if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is <0 or >1 || p.Y is <0 or >1 || pending.Any(c=>c.Id==p.Id))throw Invalid("原始触点坐标或编号无效");
            pending.Add(p);
        }
        if(pending.Count<expected)return null;
        var live=pending.Where(c=>c.Tip && c.Confidence).ToArray();
        foreach(uint id in identities.Keys.Where(id=>!live.Any(c=>c.Id==id)).ToArray())identities.Remove(id);
        foreach(var p in live)if(!identities.ContainsKey(p.Id))identities[p.Id]=Enumerable.Range(0,5).First(i=>!identities.Values.Contains(i));
        var frame=live.Select(p=>new TouchpadContact(identities[p.Id],p.X,p.Y)).OrderBy(p=>p.Id).ToArray();
        pending.Clear();expected=0;scan=null;return frame;
    }
    private InvalidDataException Invalid(string message) { Reset();return new(message); }
}
