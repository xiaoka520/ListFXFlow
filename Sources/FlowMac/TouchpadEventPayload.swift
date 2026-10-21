import AppKit
import FlowHID

/// Wire layout of the optional CGEvent IOHID data field. No memory casts or
/// assumed Swift struct packing; all integer sizes/endian orders are explicit.
enum TouchpadEventPayload {
    static func attach(_ payload:Data,to event:CGEvent)->CGEvent? {
        guard var bytes=event.data as Data?,bytes.prefix(4)==Data([0,0,0,2]),payload.count<=65535 else{return nil}
        bytes.appendBE(UInt16(payload.count));bytes.appendBE(UInt16(4205));bytes.append(payload)
        return CGEvent(withDataAllocator:nil,data:bytes as CFData)
    }
    static func digitizer(timestamp:UInt64)->Data? {
        FlowCopyTouchpadGesturePayload(timestamp) as Data?
    }
    // macOS 27 validates Dock gestures against a fluid-touch IOHID payload.
    // Layout researched by InstantSpaceSwitcher; see THIRD_PARTY_NOTICES.txt.
    static func dock(phase:UInt32,motion:UInt16,progress:Double,velocity:Double,timestamp:UInt64,positionX:Double=0,positionY:Double=0)->Data {
        let hasVelocity=velocity != 0 || phase==4
        var bytes=Data()
        bytes.appendLE(timestamp);bytes.appendLE(UInt64(0));bytes.appendLE(UInt32(0));bytes.appendLE(UInt32(0));bytes.appendLE(UInt32(hasVelocity ? 2:1))
        bytes.appendLE(UInt32(40));bytes.appendLE(UInt32(23));bytes.appendLE(phase<<24);bytes.appendLE(UInt32(0))
        bytes.appendLE(fixed(positionX));bytes.appendLE(fixed(positionY));bytes.appendLE(Int32(0));bytes.appendLE(UInt32(0))
        bytes.appendLE(motion);bytes.appendLE(UInt16(3));bytes.appendLE(fixed(progress))
        if hasVelocity {
            bytes.appendLE(UInt32(28));bytes.appendLE(UInt32(9));bytes.appendLE(UInt32(0));bytes.appendLE(UInt32(1))
            bytes.appendLE(fixed(motion==1 ? velocity:0));bytes.appendLE(fixed(motion==2 ? velocity:0));bytes.appendLE(Int32(0))
        }
        return bytes
    }
    private static func fixed(_ value:Double)->Int32 {
        guard value.isFinite else{return 0}
        let result=Int32(max(Double(Int32.min),min(Double(Int32.max),value*65536)))
        return result==0 && value != 0 ? (value>0 ? 1:-1):result
    }
}
private extension Data {
    mutating func appendLE<T:FixedWidthInteger>(_ number:T) { var value=number.littleEndian;Swift.withUnsafeBytes(of:&value){append(contentsOf:$0)} }
    mutating func appendBE<T:FixedWidthInteger>(_ number:T) { var value=number.bigEndian;Swift.withUnsafeBytes(of:&value){append(contentsOf:$0)} }
}
