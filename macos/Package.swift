// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "GhostGPTMac", platforms: [.macOS("15.0")], products: [.executable(name: "GhostGPT", targets: ["GhostGPTMac"])], targets: [.executableTarget(name: "GhostGPTMac"), .testTarget(name: "GhostGPTMacTests", dependencies: ["GhostGPTMac"])])
