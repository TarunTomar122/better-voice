// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "BetterVoice",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "BetterVoice", targets: ["BetterVoice"]),
        .library(name: "BetterVoiceCore", targets: ["BetterVoiceCore"])
    ],
    dependencies: [
        .package(
            url: "https://github.com/FluidInference/FluidAudio.git",
            revision: "6428e29186573c6d33c598e25d460e6690bc0ee1"
        ),
        .package(
            url: "https://github.com/huggingface/swift-transformers.git",
            exact: "1.3.3"
        ),
        .package(
            url: "https://github.com/microsoft/onnxruntime-swift-package-manager.git",
            exact: "1.24.2"
        )
    ],
    targets: [
        .target(name: "BetterVoiceCore"),
        .executableTarget(
            name: "BetterVoice",
            dependencies: [
                "BetterVoiceCore",
                .product(name: "FluidAudio", package: "FluidAudio"),
                .product(name: "Tokenizers", package: "swift-transformers"),
                .product(name: "onnxruntime", package: "onnxruntime-swift-package-manager")
            ]
        ),
        .executableTarget(
            name: "ModelProbe",
            dependencies: [.product(name: "FluidAudio", package: "FluidAudio")]
        ),
        .testTarget(name: "BetterVoiceCoreTests", dependencies: ["BetterVoiceCore"])
    ]
)
