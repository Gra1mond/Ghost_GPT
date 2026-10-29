import AppKit
import SwiftUI
import ScreenCaptureKit
import CoreImage

// CI can construct the Ventura path even when its host is newer.
enum MacCompatibility {
    static var forceLegacy: Bool { ProcessInfo.processInfo.environment["GHOSTGPT_FORCE_MACOS13"] == "1" }
}
struct ManualScrollObserver: ViewModifier {
    let scrolled: () -> Void
    @ViewBuilder func body(content: Content) -> some View {
        if #available(macOS 15, *), !MacCompatibility.forceLegacy {
            content.onScrollPhaseChange { _, phase in if phase == .interacting { scrolled() } }
        } else {
            content.background(ScrollWheelObserver(scrolled: scrolled))
        }
    }
}
private struct ScrollWheelObserver: NSViewRepresentable {
    let scrolled: () -> Void
    final class PassiveView: NSView {
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }
    final class Coordinator {
        var monitor: Any?
        func remove() { if let monitor { NSEvent.removeMonitor(monitor) }; monitor = nil }
        deinit { remove() }
    }
    func makeCoordinator() -> Coordinator { Coordinator() }
    func makeNSView(context: Context) -> NSView {
        let view = PassiveView()
        context.coordinator.monitor = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak view] event in
            if let view, let window = view.window, event.window === window,
               view.bounds.contains(view.convert(event.locationInWindow, from: nil)) { scrolled() }
            return event
        }
        return view
    }
    func updateNSView(_ nsView: NSView, context: Context) { }
    static func dismantleNSView(_ nsView: NSView, coordinator: Coordinator) { coordinator.remove() }
}

// macOS 13 has SCStream but not SCScreenshotManager. Grab one complete frame,
// keep it in memory, and always stop the stream on success, timeout or cancellation.
final class LegacyScreenshot: NSObject, SCStreamOutput, SCStreamDelegate {
    private var continuation: AsyncThrowingStream<CGImage, Error>.Continuation?
    private var delivered = false
    private let queue = DispatchQueue(label: "dev.ghostgpt.screenshot")
    static func capture(filter: SCContentFilter, configuration: SCStreamConfiguration) async throws -> CGImage {
        let receiver = LegacyScreenshot()
        let frames = AsyncThrowingStream<CGImage, Error>(bufferingPolicy: .bufferingNewest(1)) { receiver.continuation = $0 }
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 30)
        configuration.pixelFormat = kCVPixelFormatType_32BGRA
        configuration.capturesAudio = false
        let stream = SCStream(filter: filter, configuration: configuration, delegate: receiver)
        try stream.addStreamOutput(receiver, type: .screen, sampleHandlerQueue: receiver.queue)
        do {
            try await stream.startCapture()
            let image = try await withThrowingTaskGroup(of: CGImage.self) { group in
                group.addTask {
                    for try await image in frames { return image }
                    try Task.checkCancellation()
                    throw AppError(message: "Не удалось получить кадр экрана.")
                }
                group.addTask {
                    try await Task.sleep(nanoseconds: 10_000_000_000)
                    throw AppError(message: "Истекло время ожидания снимка. Проверьте разрешение записи экрана.")
                }
                defer { group.cancelAll() }
                return try await group.next()!
            }
            receiver.continuation?.finish()
            try? await stream.stopCapture()
            return image
        } catch {
            receiver.continuation?.finish()
            try? await stream.stopCapture()
            throw error
        }
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, !delivered, sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let status = attachments.first?[.status] as? Int, status == SCFrameStatus.complete.rawValue,
              let pixels = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        let source = CIImage(cvPixelBuffer: pixels)
        guard let image = CIContext().createCGImage(source, from: source.extent) else { return }
        delivered = true; continuation?.yield(image); continuation?.finish()
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) { continuation?.finish(throwing: AppError(message: "Захват экрана прерван. Проверьте разрешение записи экрана.")) }
}
