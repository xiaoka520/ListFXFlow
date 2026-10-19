import AppKit
import CryptoKit
import Darwin
import FlowCore
import Combine

enum LoginSupport {
    static let root="/Library/Application Support/ListFXFlow/LoginSupport"
    static let configPath=root+"/Private/connection.json"
    static var installed:Bool { FileManager.default.fileExists(atPath:root+"/installed") }
    static var exportURL:URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/ListFXFlow/login-export.json")
    }
    static func prepare(side:Side,offset:Double) throws {
        guard let peer=IdentityStore.read("peer") else { throw NSError(domain:"ListFXFlow",code:1,userInfo:[NSLocalizedDescriptionKey:"请先连接并确认远端设备，再安装登录前共享组件。"]) }
        var config=LoginConfiguration(identity:try IdentityStore.key().rawRepresentation,peer:peer,side:side,offset:offset)
        config.preferredAddress=UserDefaults.standard.string(forKey:"lastBridgeAddress")
        config.desktopUID=geteuid()
        try config.validate()
        let fm=FileManager.default
        try fm.createDirectory(at:exportURL.deletingLastPathComponent(),withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        try fm.setAttributes([.posixPermissions:0o700],ofItemAtPath:exportURL.deletingLastPathComponent().path)
        // Set the permissions before writing any identity bytes.
        guard fm.createFile(atPath:exportURL.path,contents:nil,attributes:[.posixPermissions:0o600]) else { throw FlowMacError.identity }
        try JSONEncoder().encode(config).write(to:exportURL,options:.atomic)
        try fm.setAttributes([.posixPermissions:0o600],ofItemAtPath:exportURL.path)
        guard let pkg=Bundle.main.url(forResource:"ListFXFlow-LoginSupport",withExtension:"pkg"),NSWorkspace.shared.open(pkg) else { throw NSError(domain:"ListFXFlow",code:2,userInfo:[NSLocalizedDescriptionKey:"未找到登录前共享组件安装包。请使用完整构建。"]) }
    }
    static func remove() throws {
        guard let pkg=Bundle.main.url(forResource:"ListFXFlow-RemoveLoginSupport",withExtension:"pkg"),NSWorkspace.shared.open(pkg) else { throw CocoaError(.fileNoSuchFile) }
    }
    static func provision(owner:uid_t) throws {
        guard geteuid()==0,owner>=500,let user=getpwuid(owner) else { throw FlowMacError.permission }
        let source=URL(fileURLWithPath:String(cString:user.pointee.pw_dir)).appendingPathComponent("Library/Application Support/ListFXFlow/login-export.json")
        let fm=FileManager.default,attributes=try fm.attributesOfItem(atPath:source.path)
        guard attributes[.type] as? FileAttributeType == .typeRegular,
              (attributes[.ownerAccountID] as? NSNumber)?.uint32Value==owner,
              ((attributes[.posixPermissions] as? NSNumber)?.intValue ?? 0)&0o077==0,
              (attributes[.size] as? NSNumber)?.intValue ?? 99999 < 16384 else { throw FlowMacError.identity }
        let bytes=try Data(contentsOf:source),config=try JSONDecoder().decode(LoginConfiguration.self,from:bytes)
        try config.validate()
        guard config.desktopUID == nil || config.desktopUID==owner else{throw FlowMacError.permission}
        try fm.createDirectory(atPath:root+"/Private",withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        try bytes.write(to:URL(fileURLWithPath:configPath),options:.atomic)
        try fm.setAttributes([.ownerAccountID:0,.groupOwnerAccountID:0,.posixPermissions:0o600],ofItemAtPath:configPath)
        try fm.removeItem(at:source)
        fm.createFile(atPath:root+"/installed",contents:Data(),attributes:[.posixPermissions:0o644])
    }
    static func load() throws -> LoginConfiguration {
        guard geteuid()==0 else { throw FlowMacError.permission }
        let attributes=try FileManager.default.attributesOfItem(atPath:configPath)
        guard attributes[.type] as? FileAttributeType == .typeRegular,
              (attributes[.ownerAccountID] as? NSNumber)?.intValue==0,
              ((attributes[.posixPermissions] as? NSNumber)?.intValue ?? 0)&0o077==0 else { throw FlowMacError.identity }
        let config=try JSONDecoder().decode(LoginConfiguration.self,from:Data(contentsOf:URL(fileURLWithPath:configPath)))
        try config.validate();return config
    }
}

