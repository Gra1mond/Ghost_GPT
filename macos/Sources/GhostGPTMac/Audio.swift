import Foundation
import ScreenCaptureKit
import AVFoundation

struct AudioPacket { var source: String; var wav: Data }
struct AudioBacklog {
    private(set) var packets: [AudioPacket] = []
    mutating func append(_ packet: AudioPacket) -> Bool {
        guard packets.count < 4 else { return false }; packets.append(packet); return true
    }
    mutating func pop() -> AudioPacket? { packets.isEmpty ? nil : packets.removeFirst() }
    mutating func clear() { for i in packets.indices { packets[i].wav.resetBytes(in: 0..<packets[i].wav.count) }; packets.removeAll() }
}
enum Wave {
    static func encode(_ pcm: Data, rate: Int) -> Data {
        var data = Data()
        func text(_ s: String) { data.append(Data(s.utf8)) }
        func u32(_ v: UInt32) { var n = v.littleEndian; withUnsafeBytes(of: &n) { data.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { var n = v.littleEndian; withUnsafeBytes(of: &n) { data.append(contentsOf: $0) } }
        text("RIFF"); u32(UInt32(36 + pcm.count)); text("WAVEfmt "); u32(16); u16(1); u16(1)
        u32(UInt32(rate)); u32(UInt32(rate * 2)); u16(2); u16(16); text("data"); u32(UInt32(pcm.count)); data.append(pcm); return data
    }
}
final class AudioCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    private var stream: SCStream?
    private let queue = DispatchQueue(label: "dev.ghostgpt.audio")
    private var buffers: [String: Data] = [:]
    private var running = false
    var received: ((AudioPacket) -> Void)?
    var failed: ((String) -> Void)?
    func start(mic: Bool, system: Bool, device: String) async throws {
        guard mic || system else { throw AppError(message: "Р’С‹Р±РµСЂРёС‚Рµ РёСЃС‚РѕС‡РЅРёРє Р·РІСѓРєР°.") }
        if mic {
            let allowed = await AVCaptureDevice.requestAccess(for: .audio)
            guard allowed else { throw AppError(message: "Р Р°Р·СЂРµС€РёС‚Рµ РґРѕСЃС‚СѓРї Рє РјРёРєСЂРѕС„РѕРЅСѓ РІ РЅР°СЃС‚СЂРѕР№РєР°С… macOS.") }
        }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first else { throw AppError(message: "РќРµС‚ РґРѕСЃС‚СѓРїРЅРѕРіРѕ СЌРєСЂР°РЅР° РґР»СЏ Р·Р°С…РІР°С‚Р° Р·РІСѓРєР°.") }
        let own = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
        let filter = SCContentFilter(display: display, excludingApplications: own, exceptingWindows: [])
        let config = SCStreamConfiguration()
        config.width = 2; config.height = 2; config.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        config.capturesAudio = system; config.excludesCurrentProcessAudio = true
        config.sampleRate = 16000; config.channelCount = 1
        config.captureMicrophone = mic
        if !device.isEmpty { config.microphoneCaptureDeviceID = device }
        let capture = SCStream(filter: filter, configuration: config, delegate: self)
        if system { try capture.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue) }
        if mic { try capture.addStreamOutput(self, type: .microphone, sampleHandlerQueue: queue) }
        queue.sync { buffers.removeAll(); running = true }
        do { try await capture.startCapture(); stream = capture }
        catch { queue.sync { running = false; buffers.removeAll() }; throw error }
    }
    func stop() async {
        let capture = stream; stream = nil
        queue.sync { running = false; for key in Array(buffers.keys) { let count = buffers[key]?.count ?? 0; buffers[key]?.resetBytes(in: 0..<count) }; buffers.removeAll() }
        try? await capture?.stopCapture()
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) {
        queue.async { self.running = false; self.buffers.removeAll() }
        failed?("Р—Р°С…РІР°С‚ Р°СѓРґРёРѕ РѕСЃС‚Р°РЅРѕРІР»РµРЅ. РџСЂРѕРІРµСЂСЊС‚Рµ СѓСЃС‚СЂРѕР№СЃС‚РІР° Рё СЂР°Р·СЂРµС€РµРЅРёСЏ macOS.")
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sample: CMSampleBuffer, of type: SCStreamOutputType) {
        guard running, sample.isValid, type == .audio || type == .microphone,
              let description = sample.formatDescription,
              let format = CMAudioFormatDescriptionGetStreamBasicDescription(description)?.pointee else { return }
        let source = type == .microphone ? "РњРёРєСЂРѕС„РѕРЅ" : "Р—РІСѓРє РєРѕРјРїСЊСЋС‚РµСЂР°"
        var size = 0
        var block: CMBlockBuffer?
        CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sample, bufferListSizeNeededOut: &size, bufferListOut: nil, bufferListSize: 0, blockBufferAllocator: nil, blockBufferMemoryAllocator: nil, flags: 0, blockBufferOut: nil)
        guard size > 0 else { return }
        let raw = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { raw.deallocate() }
        let list = raw.bindMemory(to: AudioBufferList.self, capacity: 1)
        guard CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sample, bufferListSizeNeededOut: nil, bufferListOut: list, bufferListSize: size, blockBufferAllocator: nil, blockBufferMemoryAllocator: nil, flags: 0, blockBufferOut: &block) == noErr else { return }
        let audio = UnsafeMutableAudioBufferListPointer(list)
        let frames = CMSampleBufferGetNumSamples(sample)
        let rate = Int(format.mSampleRate)
        guard rate > 0, format.mFormatID == kAudioFormatLinearPCM else { return }
        var pcm = Data(capacity: frames * 2); var peak: Float = 0
        for frame in 0..<frames {
            var value: Float = 0; var channels = 0
            for buffer in audio {
                guard let pointer = buffer.mData else { continue }
                for channel in 0..<Int(buffer.mNumberChannels) {
                    let index = frame * Int(buffer.mNumberChannels) + channel
                    if format.mBitsPerChannel == 32 && format.mFormatFlags & kAudioFormatFlagIsFloat != 0 && (index+1)*4 <= buffer.mDataByteSize {
                        value += pointer.assumingMemoryBound(to: Float.self)[index]; channels += 1
                    } else if format.mBitsPerChannel == 16 && (index+1)*2 <= buffer.mDataByteSize {
                        value += Float(pointer.assumingMemoryBound(to: Int16.self)[index]) / 32768; channels += 1
                    }
                }
            }
            value = channels > 0 ? value / Float(channels) : 0
            if !value.isFinite { value = 0 }; peak = max(peak, abs(value))
            var integer = Int16(max(-1, min(1, value)) * 32767).littleEndian
            withUnsafeBytes(of: &integer) { pcm.append(contentsOf: $0) }
        }
        // Silence before speech is ignored; a six-second bounded segment follows speech.
        if peak < 0.008 && buffers[source, default: Data()].isEmpty { return }
        buffers[source, default: Data()].append(pcm)
        if buffers[source, default: Data()].count >= rate * 2 * 6 {
            let data = buffers.removeValue(forKey: source) ?? Data()
            received?(AudioPacket(source: source, wav: Wave.encode(data, rate: rate)))
        }
    }
}
