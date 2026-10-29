import XCTest
import CryptoKit
@testable import GhostGPTMac

final class CoreTests: XCTestCase {
    func testEndpointValidation() throws {
        XCTAssertEqual(try API.url("https://api.wellflow.dev/v1/", "chat/completions").absoluteString, "https://api.wellflow.dev/v1/chat/completions")
        for address in ["http://example.com/v1", "https://user:pass@example.com/v1", "https://example.com/v1?key=secret", "file:///tmp/key"] { XCTAssertThrowsError(try API.url(address, "models")) }
        XCTAssertThrowsError(try API.request(endpoint: "https://api.openai.com/v1", key: "wf_test", route: "models"))
    }
    func testStreamingUnicodeAndCompletion() throws {
        let chunk = try SSEChunk.parse("data: {\"choices\":[{\"delta\":{\"content\":\"Привет 👻\"}}]}")
        XCTAssertEqual(chunk.text, "Привет 👻"); XCTAssertFalse(chunk.finished)
        XCTAssertTrue(try SSEChunk.parse("data: [DONE]").finished)
        XCTAssertTrue(try SSEChunk.parse("data: {\"choices\":[{\"finish_reason\":\"stop\"}]}").finished)
        XCTAssertThrowsError(try SSEChunk.parse("data: {\"error\":{\"message\":\"secret\"}}"))
    }
    func testContextDropsIncompletePair() {
        let messages = [Message(role: "user", text: "1234"), Message(role: "assistant", text: "5678"), Message(role: "user", text: "new")]
        XCTAssertEqual(API.bounded(messages, budget: 7).map(\.text), ["new"])
    }
    func testAudioQueueBoundAndDrain() {
        var queue = AudioBacklog()
        for _ in 0..<4 { XCTAssertTrue(queue.append(AudioPacket(source: "mic", wav: Data([1,2])))) }
        XCTAssertFalse(queue.append(AudioPacket(source: "system", wav: Data([3]))))
        XCTAssertEqual(queue.pop()?.source, "mic"); queue.clear(); XCTAssertNil(queue.pop())
    }
    func testWaveContract() {
        let wave = Wave.encode(Data([1,2,3,4]), rate: 16000)
        XCTAssertEqual(wave.count, 48); XCTAssertEqual(String(data: wave.prefix(4), encoding: .utf8), "RIFF")
        XCTAssertEqual(String(data: wave[8..<12], encoding: .utf8), "WAVE")
        XCTAssertEqual(Array(wave.suffix(4)), [1,2,3,4])
    }
    func testHistoryEncryptionAndTamper() throws {
        let key = SymmetricKey(size: .bits256), text = Data("private transcript".utf8)
        var encrypted = try AES.GCM.seal(text, using: key).combined!
        XCTAssertNil(encrypted.range(of: text)); XCTAssertEqual(try AES.GCM.open(AES.GCM.SealedBox(combined: encrypted), using: key), text)
        encrypted[encrypted.count-1] ^= 1
        XCTAssertThrowsError(try AES.GCM.open(AES.GCM.SealedBox(combined: encrypted), using: key))
    }
    func testSettingsRoundTrip() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        let store = try LocalStore(directory: folder)
        var settings = Preferences(); settings.folders = ["Собеседование"]; settings.opacity = 0.5
        try store.write(settings, to: "settings.json")
        XCTAssertEqual(try store.read("settings.json", as: Preferences.self)?.folders, settings.folders)
    }
    func testProviderErrorDoesNotExposeBody() {
        let response = HTTPURLResponse(url: URL(string: "https://example.com")!, statusCode: 429, httpVersion: nil, headerFields: nil)!
        XCTAssertThrowsError(try API.check(response)) { XCTAssertTrue($0.localizedDescription.contains("429")) }
    }
}
