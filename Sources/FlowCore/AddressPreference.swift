import Foundation

public enum AddressPreference {
    public static func ordered(_ available:[String],preferred:String?)->[String] {
        guard let preferred,available.contains(preferred) else{return available}
        return [preferred]+available.filter { $0 != preferred }
    }
}
