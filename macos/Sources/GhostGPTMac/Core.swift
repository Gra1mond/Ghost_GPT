import Foundation
import Security
import CryptoKit

struct AppError: LocalizedError { let message: String; var errorDescription: String? { message } }
struct Message: Codable, Identifiable { var id = UUID(); var role: String; var text: String }
struct Chat: Codable, Identifiable { var id = UUID(); var title = "Новый чат"; var folder = ""; var messages: [Message] = [] }
struct Preferences: Codable {
    var endpoint = "https://api.wellflow.dev/v1", model = "gpt-5.6-sol", visionModel = ""
    var audioEndpoint = "", audioModel = "", microphone = ""
    var opacity = 0.9, sidebarWidth = 250.0
    var folders: [String] = []
    var hotkeys: [UInt32] = [49, 36, 1, 4, 125] // Space, Return, S, H, Down; Ctrl+Shift
}

enum Vault {
    static let service = "dev.ghostgpt.mac"
    static func get(_ account: String) throws -> Data? {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
            kSecAttrAccount as String: account, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw AppError(message: "Не удалось прочитать Связку ключей (\(status)).") }
        return result as? Data
    }
    static func put(_ data: Data, account: String) throws {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        if data.isEmpty {
            let result = SecItemDelete(query as CFDictionary)
            guard result == errSecSuccess || result == errSecItemNotFound else { throw AppError(message: "Не удалось удалить ключ.") }; return
        }
        let result = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if result == errSecItemNotFound {
            var values = query; values[kSecValueData as String] = data
            values[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            guard SecItemAdd(values as CFDictionary, nil) == errSecSuccess else { throw AppError(message: "Не удалось сохранить ключ.") }
        } else if result != errSecSuccess { throw AppError(message: "Не удалось обновить ключ.") }
    }
}
struct LocalStore {
    let directory: URL
    init(directory: URL? = nil) throws {
        self.directory = directory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("GhostGPT")
        try FileManager.default.createDirectory(at: self.directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    }
    func read<T: Decodable>(_ name: String, as: T.Type) throws -> T? {
        let url = directory.appendingPathComponent(name)
        guard FileManager.default.fileExists(atPath: url.path) else { return nil }
        return try JSONDecoder().decode(T.self, from: Data(contentsOf: url))
    }
    func write<T: Encodable>(_ value: T, to name: String) throws { try JSONEncoder().encode(value).write(to: directory.appendingPathComponent(name), options: .atomic) }
    func historyKey() throws -> SymmetricKey {
        if let data = try Vault.get("history-key") { return SymmetricKey(data: data) }
        // Never generate a replacement key for an existing archive.
        if FileManager.default.fileExists(atPath: directory.appendingPathComponent("chats.enc").path) { throw AppError(message: "В Связке ключей отсутствует ключ истории. Архив сохранён без изменений.") }
        let key = SymmetricKey(size: .bits256)
        try Vault.put(key.withUnsafeBytes { Data($0) }, account: "history-key"); return key
    }
    func loadChats() throws -> [Chat] {
        let url = directory.appendingPathComponent("chats.enc")
        guard FileManager.default.fileExists(atPath: url.path) else { return [] }
        let data = try AES.GCM.open(AES.GCM.SealedBox(combined: Data(contentsOf: url)), using: historyKey())
        return try JSONDecoder().decode([Chat].self, from: data)
    }
    func saveChats(_ chats: [Chat]) throws {
        let data = try AES.GCM.seal(JSONEncoder().encode(chats), using: historyKey()).combined!
        try data.write(to: directory.appendingPathComponent("chats.enc"), options: .atomic)
    }
    func loadInstructions() throws -> String {
        let url = directory.appendingPathComponent("INTERVIEW.md")
        if !FileManager.default.fileExists(atPath: url.path) {
            guard let bundled = Bundle.main.url(forResource: "INTERVIEW", withExtension: "md") else { throw AppError(message: "В сборке отсутствует INTERVIEW.md.") }
            try FileManager.default.copyItem(at: bundled, to: url)
        }
        return try String(contentsOf: url, encoding: .utf8)
    }
}
struct SSEChunk {
    var text = ""; var finished = false
    static func parse(_ line: String) throws -> SSEChunk {
        guard line.hasPrefix("data:") else { return SSEChunk() }
        let content = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
        if content == "[DONE]" { return SSEChunk(finished: true) }
        if content.isEmpty { return SSEChunk() }
        guard let root = try JSONSerialization.jsonObject(with: Data(content.utf8)) as? [String: Any] else { throw AppError(message: "Неверный формат ответа API.") }
        if root["error"] != nil { throw AppError(message: "Провайдер прервал ответ с ошибкой.") }
        let choice = (root["choices"] as? [[String: Any]])?.first
        return SSEChunk(text: (choice?["delta"] as? [String: Any])?["content"] as? String ?? "", finished: choice?["finish_reason"] as? String != nil)
    }
}
enum API {
    static func url(_ endpoint: String, _ route: String) throws -> URL {
        guard let u = URL(string: endpoint.trimmingCharacters(in: .whitespacesAndNewlines)),
              u.scheme == "https" || (u.scheme == "http" && ["localhost", "127.0.0.1", "::1"].contains(u.host ?? "")),
              u.host != nil, u.user == nil, u.password == nil, u.query == nil, u.fragment == nil else { throw AppError(message: "Введите HTTPS-адрес API с /v1.") }
        return u.appendingPathComponent(route)
    }
    static func check(_ response: URLResponse) throws {
        guard let http = response as? HTTPURLResponse else { throw AppError(message: "Ответ не является HTTP.") }
        guard (200..<300).contains(http.statusCode) else {
            let detail = [401: "Неверный API-ключ", 403: "Нет доступа к модели", 429: "Лимит запросов или баланса", 400: "Проверьте модель и поддержку изображений", 404: "Не найдена модель или адрес API"][http.statusCode] ?? "Ошибка провайдера"
            throw AppError(message: "\(detail) (\(http.statusCode)).")
        }
    }
    static func request(endpoint: String, key: String, route: String) throws -> URLRequest {
        let target = try url(endpoint, route)
        guard !key.isEmpty, !key.unicodeScalars.contains(where: CharacterSet.controlCharacters.contains) else { throw AppError(message: "Введите корректный API-ключ в настройках.") }
        if key.hasPrefix("wf_"), target.host != "api.wellflow.dev" { throw AppError(message: "Ключ Wellflow нельзя отправить другому провайдеру.") }
        var request = URLRequest(url: target, timeoutInterval: 180)
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization"); return request
    }
    static func bounded(_ messages: [Message], budget: Int = 24000) -> [Message] {
        var count = 0, result: [Message] = []
        for message in messages.reversed() {
            if count + message.text.count > budget { break }
            count += message.text.count; result.insert(message, at: 0)
        }
        while result.first?.role == "assistant" { result.removeFirst() }
        return result
    }
    static func stream(endpoint: String, key: String, model: String, messages: [Message], image: Data? = nil,
                       receive: @escaping @MainActor (String) -> Void) async throws {
        guard !model.isEmpty else { throw AppError(message: "Укажите модель.") }
        var request = try request(endpoint: endpoint, key: key, route: "chat/completions")
        var turns: [[String: Any]] = messages.map { ["role": $0.role, "content": $0.text] }
        if let image, let last = messages.last, !turns.isEmpty {
            turns[turns.count-1]["content"] = [["type": "text", "text": last.text], ["type": "image_url", "image_url": ["url": "data:image/png;base64," + image.base64EncodedString()]]]
        }
        request.httpMethod = "POST"; request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: ["model": model, "messages": turns, "stream": true])
        // Isolated session with no cookies, credentials cache or redirect of bearer headers.
        let session = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        let (bytes, response) = try await session.bytes(for: request); try check(response)
        var finished = false, buffer = "", total = 0, lastPaint = Date()
        do {
        for try await line in bytes.lines {
            try Task.checkCancellation()
            let chunk = try SSEChunk.parse(line); finished = finished || chunk.finished
            total += chunk.text.count
            guard total <= 100000 else { throw AppError(message: "Ответ превысил лимит 100 000 символов.") }
            buffer += chunk.text
            if Date().timeIntervalSince(lastPaint) > 0.08 || chunk.finished {
                if !buffer.isEmpty { await receive(buffer); buffer = "" }; lastPaint = Date()
            }
            if chunk.finished { break }
        }
        } catch {
            if !buffer.isEmpty { await receive(buffer) }
            throw error
        }
        if !buffer.isEmpty { await receive(buffer) }
        guard finished else { throw AppError(message: "Соединение оборвалось до завершения ответа.") }
        guard total > 0 else { throw AppError(message: "Модель вернула пустой ответ.") }
    }
    static func models(endpoint: String, key: String) async throws -> [String] {
        let request = try request(endpoint: endpoint, key: key, route: "models")
        let session = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        let (data, response) = try await session.data(for: request); try check(response)
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any], let rows = root["data"] as? [[String: Any]] else { throw AppError(message: "Провайдер не вернул список моделей. Введите ID вручную.") }
        return Array(Set(rows.compactMap { $0["id"] as? String })).sorted()
    }
    static func transcribe(_ data: Data, endpoint: String, key: String, model: String) async throws -> String {
        guard !model.isEmpty else { throw AppError(message: "Укажите модель транскрибации.") }
        var request = try request(endpoint: endpoint, key: key, route: "audio/transcriptions")
        let boundary = UUID().uuidString
        request.httpMethod = "POST"; request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        var body = Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"model\"\r\n\r\n\(model)\r\n--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\n".utf8)
        body.append(data); body.append(Data("\r\n--\(boundary)--\r\n".utf8)); request.httpBody = body
        let session = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil); defer { session.invalidateAndCancel() }
        let (responseData, response) = try await session.data(for: request); try check(response)
        guard let result = try JSONSerialization.jsonObject(with: responseData) as? [String: Any], let text = result["text"] as? String else { throw AppError(message: "Неверный ответ API транскрибации.") }
        return text
    }
}
final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}
