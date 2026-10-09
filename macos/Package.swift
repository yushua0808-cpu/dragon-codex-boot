// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "DragonCodexBootMac",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "DragonCodexBootCore", targets: ["DragonCodexBootCore"]),
        .executable(name: "DragonCodexBootMac", targets: ["DragonCodexBootMac"]),
    ],
    targets: [
        .target(name: "DragonCodexBootCore", path: "Sources/DragonCodexBootCore"),
        .executableTarget(
            name: "DragonCodexBootMac",
            dependencies: ["DragonCodexBootCore"],
            path: "Sources/DragonCodexBootMac"
        ),
        .testTarget(
            name: "DragonCodexBootCoreTests",
            dependencies: ["DragonCodexBootCore"],
            path: "Tests/DragonCodexBootCoreTests"
        ),
    ]
)
