// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ListFXFlow", platforms: [.macOS(.v14)],
    products: [.executable(name: "ListFXFlow", targets: ["FlowMac"])],
    targets: [
        .target(name: "FlowHID", linkerSettings: [.linkedFramework("IOKit"),.linkedFramework("CoreGraphics")]),
        .target(name: "FlowCore", resources: [.process("Resources")]),
        .executableTarget(name: "FlowMac", dependencies: ["FlowCore", "FlowHID"]),
        .testTarget(name: "FlowCoreTests", dependencies: ["FlowCore", "FlowHID", "FlowMac"])
    ])
