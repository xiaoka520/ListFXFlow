import Foundation
import CoreHID
import IOKit
import Security

/// Registration experiment, separate from the running sharing app. It sends no
/// input reports, and does not claim that a generic HID is an Apple trackpad.
enum TouchpadDescriptor {
    static let inputReportSize=34 // ID + five 6-byte fingers + time + count
    static let bytes:Data = {
        var b:[UInt8]=[0x05,0x0d,0x09,0x05,0xa1,0x01,0x85,0x01]
        for _ in 0..<5 {
            b += [0x09,0x22,0xa1,0x02,0xa4, // Finger / logical collection, save globals
                  0x09,0x47,0x09,0x42,0x15,0x00,0x25,0x01,0x75,0x01,0x95,0x02,0x81,0x02,
                  0x75,0x06,0x95,0x01,0x81,0x03, // six padding bits
                  0x09,0x51,0x25,0x04,0x75,0x08,0x81,0x02, // contact ID 0...4
                  0x05,0x01,0x09,0x30,0x26,0x10,0x27,0x35,0x00,0x46,0xb0,0x04,
                  0x55,0x0e,0x65,0x11,0x75,0x10,0x81,0x02, // X: 10000 units / 120 mm
                  0x09,0x31,0x26,0x70,0x17,0x46,0xd0,0x02,0x81,0x02, // Y: 6000 / 72 mm
                  0xb4,0xc0]
        }
        b += [0x09,0x56,0x15,0x00,0x26,0xff,0xff,0x35,0x00,0x46,0xff,0xff,
              0x55,0x0c,0x66,0x01,0x10,0x75,0x10,0x95,0x01,0x81,0x02, // scan time, 100 us
              0x09,0x54,0x25,0x05,0x45,0x05,0x55,0x00,0x65,0x00,0x75,0x08,0x81,0x02,
              0x85,0x02,0x09,0x55,0xb1,0x02, // feature report 2: maximum contact count
              0xc0]
        return Data(b)
    }()

    /// Walk HID short items, independently deriving report sizes and collection
    /// structure. This checks the descriptor layout, not OS gesture recognition.
    static func check(_ data:Data) throws {
        struct Globals { var id=0,size=0,count=0,page=0 }
        var g=Globals(),stack=[Globals](),bits=[String:Int]()
        var i=0,depth=0,fingers=0,usage=0,touchpads=0
        let b=[UInt8](data)
        while i<b.count {
            let prefix=b[i];i+=1
            guard prefix != 0xfe else { throw ProbeError.invalidDescriptor }
            let size=[0,1,2,4][Int(prefix&3)]
            guard i+size<=b.count else { throw ProbeError.invalidDescriptor }
            var value=0
            for n in 0..<size { value |= Int(b[i+n])<<(8*n) };i+=size
            let type=(prefix>>2)&3,tag=prefix>>4
            if type==1 {
                switch tag {
                case 0:g.page=value
                case 7:g.size=value
                case 8:g.id=value
                case 9:g.count=value
                case 10:stack.append(g)
                case 11:guard let old=stack.popLast() else { throw ProbeError.invalidDescriptor };g=old
                default:break
                }
            } else if type==2 && tag==0 { usage=value }
            else if type==0 {
                if tag==8 || tag==11 { bits["\(tag):\(g.id)",default:0]+=g.size*g.count }
                if tag==10 {
                    depth+=1
                    if g.page==0x0d && usage==0x22 && value==2 { fingers+=1 }
                    if g.page==0x0d && usage==5 && value==1 { touchpads+=1 }
                }
                if tag==12 { depth-=1;guard depth>=0 else { throw ProbeError.invalidDescriptor } }
                usage=0
            }
        }
        guard depth==0,stack.isEmpty,fingers==5,touchpads==1,
              bits==["8:1":(inputReportSize-1)*8,"11:2":8] else { throw ProbeError.invalidDescriptor }
    }
}

enum ProbeError:Error { case invalidDescriptor,unsupportedReport,bufferTooSmall }

@available(macOS 15, *)
private actor ProbeDelegate:HIDVirtualDeviceDelegate {
    func hidVirtualDevice(_ device:HIDVirtualDevice,receivedSetReportRequestOfType type:HIDReportType,id:HIDReportID?,data:Data) async throws {
        throw ProbeError.unsupportedReport // No output reports or writable features.
    }
    func hidVirtualDevice(_ device:HIDVirtualDevice,receivedGetReportRequestOfType type:HIDReportType,id:HIDReportID?,maxSize:Int) async throws -> Data {
        let report:Data
        if type == .feature && id?.rawValue==2 { report=Data([2,5]) }
        else if type == .input && id?.rawValue==1 {
            var empty=Data(repeating:0,count:TouchpadDescriptor.inputReportSize);empty[0]=1;report=empty
        } else { throw ProbeError.unsupportedReport }
        guard maxSize>=report.count else { throw ProbeError.bufferTooSmall }
        return report
    }
}

@main enum VirtualTouchpadProbe {
    static func main() async {
        do {
            try TouchpadDescriptor.check(TouchpadDescriptor.bytes)
            if CommandLine.arguments.contains("--self-test") {
                var truncated=TouchpadDescriptor.bytes;truncated.removeLast()
                guard (try? TouchpadDescriptor.check(truncated))==nil else { throw ProbeError.invalidDescriptor }
                var wrongID=TouchpadDescriptor.bytes;wrongID[7]=3
                guard (try? TouchpadDescriptor.check(wrongID))==nil else { throw ProbeError.invalidDescriptor }
                print("Descriptor checks passed: five fingers, 34-byte input, maximum-contact feature, malformed layouts rejected. No device registered.")
                return
            }
            print("ListFXFlow virtual Touch Pad registration experiment")
            print("OS: \(ProcessInfo.processInfo.operatingSystemVersionString)")
            print("Descriptor: \(TouchpadDescriptor.bytes.count) bytes; Digitizer 0x0D / Touch Pad 0x05; five fingers")
            print("Signing entitlement present: \(signingEntitlementPresent())")
            inspectUserClientAccess()
            guard #available(macOS 15, *) else { print("RESULT: CoreHID requires macOS 15 or later");exit(2) }
            await register()
        } catch {
            fputs("Descriptor check failed: \(error)\n",stderr);exit(1)
        }
    }

    private static func signingEntitlementPresent()->Bool {
        var dynamic:SecCode?,code:SecStaticCode?,info:CFDictionary?
        guard SecCodeCopySelf([],&dynamic)==errSecSuccess,let dynamic,
              SecCodeCopyStaticCode(dynamic,[],&code)==errSecSuccess,let code,
              SecCodeCopySigningInformation(code,SecCSFlags(rawValue:kSecCSSigningInformation),&info)==errSecSuccess,
              let dictionary=info as? [String:Any],let entitlements=dictionary[kSecCodeInfoEntitlementsDict as String] as? [String:Any] else { return false }
        return (entitlements["com.apple.developer.hid.virtual.device"] as? NSNumber)?.boolValue==true
    }

    private static func inspectUserClientAccess() {
        let service=IOServiceGetMatchingService(kIOMainPortDefault,IOServiceMatching("IOHIDResource"))
        guard service != 0 else { print("IOHIDResource: unavailable");return }
        defer { IOObjectRelease(service) }
        var connection:io_connect_t=0
        // Device-user-client type 0, as used by Apple's IOHIDUserDeviceCreate.
        // No descriptor or device input is submitted by this diagnostic call.
        let result=IOServiceOpen(service,mach_task_self_,0,&connection)
        print(String(format:"IOHIDResource device-user-client open: 0x%08x",UInt32(bitPattern:result)))
        if result != KERN_SUCCESS { print("User-client open failed before any report descriptor was submitted: \(String(cString:mach_error_string(result)))") }
        if connection != 0 { IOServiceClose(connection) }
    }

    @available(macOS 15, *) private static func register() async {
        let properties=HIDVirtualDevice.Properties(descriptor:TouchpadDescriptor.bytes,vendorID:0xffff,productID:0x4c46,
            product:"ListFXFlow Virtual Touchpad Probe",manufacturer:"ListFXFlow",uniqueID:UUID().uuidString)
        guard let device=HIDVirtualDevice(properties:properties) else {
            print("RESULT: CoreHID returned nil. No virtual touchpad was registered; no input reports sent.")
            exit(2)
        }
        await device.activate(delegate:ProbeDelegate())
        print("RESULT: virtual HID created; registry device ID: \(device.deviceReference.deviceID)")
        if let client=HIDDeviceClient(deviceReference:device.deviceReference) {
            print("Parsed primary usage: \(await client.primaryUsage); descriptor bytes: \(await client.descriptor.count)")
        }
        print("Holding registration for three seconds, then exiting to release it. No input reports are sent; native gesture recognition is UNVERIFIED.")
        try? await Task.sleep(for:.seconds(3))
        withExtendedLifetime(device) {}
    }
}
