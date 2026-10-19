import Foundation
import Security

/// Inspect this running executable's signature, never a bundled entitlement
/// plist. A declared entitlement still does not establish native MT support.
enum VirtualTouchpadCapability {
    static let entitlement="com.apple.developer.hid.virtual.device"
    static func description()->String {
        var code:SecCode?
        let selfStatus=SecCodeCopySelf([], &code)
        guard selfStatus==errSecSuccess,let code else { return "virtual-hid: signature inspection failed (\(selfStatus))" }
        var staticCode:SecStaticCode?
        let staticStatus=SecCodeCopyStaticCode(code,[],&staticCode)
        guard staticStatus==errSecSuccess,let staticCode else { return "virtual-hid: signature inspection failed (\(staticStatus))" }
        var information:CFDictionary?
        let status=SecCodeCopySigningInformation(staticCode,SecCSFlags(rawValue:kSecCSSigningInformation),&information)
        guard status==errSecSuccess,let dictionary=information as? [String:Any] else { return "virtual-hid: signature inspection failed (\(status))" }
        let entitlements=dictionary[kSecCodeInfoEntitlementsDict as String] as? [String:Any]
        guard (entitlements?[entitlement] as? NSNumber)?.boolValue==true else {
            return "virtual-hid: unavailable — running executable lacks \(entitlement). Privacy & Security approval cannot add a signing entitlement. Current receiver: Quartz events."
        }
        return "virtual-hid: signing entitlement present; device creation and macOS multitouch recognition remain unverified. Current receiver: Quartz events."
    }
}
