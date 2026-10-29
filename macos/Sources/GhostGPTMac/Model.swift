import SwiftUI
import AppKit
import AVFoundation
import ScreenCaptureKit

@MainActor final class Model: ObservableObject {
    @Published var preferences = Preferences()
    @Published var chats: [Chat] = []
    @Published var selected: UUID?
    @Published var page = "Чат"
    @Published var search = ""
    @Published var prompt = ""
    @Published var key = ""
    @Published var audioKey = ""
    @Published var instructions = ""
    @Published var status = "Готов к работе"
    @Published var busy = false
    @Published var recording = false
    @Published var startingAudio = false
    @Published var screenshot: Data?
    @Published var screenshotBusy = false
    @Published var compact = false
    @Published var sidebar = true
    @Published var onTop = true
    @Published var transcript: [String] = []
    @Published var summary = ""
    @Published var useMicrophone = true
    @Published var useSystem = true
    @Published var instruction = ""
    @Published var consent = false
    @Published var folderName = ""
    @Published var showFolderEditor = false
    @Published var models: [String] = []
    @Published var checkingConnection = false
    var store: LocalStore?
    var saveAllowed = true
    var generation: Task<Void, Never>?, audioWorker: Task<Void, Never>?, summarizer: Task<Void, Never>?, instructionSave: Task<Void, Never>?
    let audio = AudioCapture()
    var audioQueue = AudioBacklog(), sessionID = UUID(), captureSession = UUID()
    var window: NSWindow?, hotkeyUpdate: (() -> Void)?
    init() {
        do {
            let store = try LocalStore(); self.store = store
            preferences = try store.read("settings.json", as: Preferences.self) ?? Preferences()
            preferences.opacity = min(1, max(0.15, preferences.opacity))
            preferences.sidebarWidth = min(360, max(200, preferences.sidebarWidth))
            key = String(data: try Vault.get("chat-key") ?? Data(), encoding: .utf8) ?? ""
            audioKey = String(data: try Vault.get("audio-key") ?? Data(), encoding: .utf8) ?? ""
            chats = try store.loadChats(); selected = chats.first?.id
            instructions = try store.loadInstructions()
        } catch { saveAllowed = false; status = "Не удалось загрузить данные: \(error.localizedDescription). Исходные файлы сохранены." }
    }
    var messages: [Message] { chats.first(where: { $0.id == selected })?.messages ?? [] }
    func saveHistory() { guard saveAllowed else { return }; do { try store?.saveChats(chats) } catch { status = "Не удалось сохранить историю: \(error.localizedDescription)" } }
    func saveSettings() {
        do { try Vault.put(Data(key.utf8), account: "chat-key"); try Vault.put(Data(audioKey.utf8), account: "audio-key")
            try store?.write(preferences, to: "settings.json"); status = "Настройки сохранены"; hotkeyUpdate?() }
        catch { status = error.localizedDescription }
    }
    func loadModels() {
        guard !checkingConnection else { return }; checkingConnection = true
        Task {
            defer { checkingConnection = false }
            do { models = try await API.models(endpoint: preferences.endpoint, key: key); status = "Подключение проверено. Моделей: \(models.count)." }
            catch { status = error.localizedDescription }
        }
    }
    func saveInstructions() {
        instructionSave?.cancel()
        instructionSave = Task { [weak self] in
            do { try await Task.sleep(nanoseconds: 500_000_000); self?.flushInstructions() } catch { }
        }
    }
    func flushInstructions() {
        guard saveAllowed, let store else { return }
        do { try instructions.write(to: store.directory.appendingPathComponent("INTERVIEW.md"), atomically: true, encoding: .utf8) }
        catch { status = "Не удалось сохранить инструкции." }
    }
    func newChat(folder: String = "") {
        guard !busy else { return }
        let chat = Chat(folder: folder); chats.insert(chat, at: 0); selected = chat.id; page = "Чат"; prompt = ""; screenshot = nil; saveHistory()
    }
    func delete(_ chat: Chat) { guard !busy else { return }; chats.removeAll { $0.id == chat.id }; if selected == chat.id { selected = chats.first?.id }; saveHistory() }
    func send(conversation: Bool = false) {
        guard !busy else { return }
        let question = conversation ? "Помоги ответить на последний вопрос разговора.\n\(instruction)" : prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !question.isEmpty else { return }
        guard !instructions.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { status = "Заполните инструкции для ответа."; return }
        if conversation && transcript.isEmpty { status = "Сначала запишите разговор."; return }
        do { _ = try API.request(endpoint: preferences.endpoint, key: key, route: "chat/completions") } catch { status = error.localizedDescription; return }
        if selected == nil { newChat() }
        guard let index = chats.firstIndex(where: { $0.id == selected }) else { return }
        let id = chats[index].id, answer = Message(role: "assistant", text: "")
        let image = conversation ? nil : screenshot
        var context = API.bounded(chats[index].messages)
        if conversation { context = [Message(role: "user", text: "Резюме предыдущего разговора:\n\(summary)\nПоследние реплики:\n" + transcript.joined(separator: "\n"))] }
        let user = Message(role: "user", text: question)
        let turns = [Message(role: "system", text: instructions)] + context + [user]
        if chats[index].messages.isEmpty { chats[index].title = String(question.prefix(48)) }
        chats[index].messages += [user, answer]; chats[index].messages = Array(chats[index].messages.suffix(100))
        prompt = ""; screenshot = nil; busy = true; status = "Ожидаю ответ модели…"; page = "Чат"
        let endpoint = preferences.endpoint, apiKey = key, model = image != nil && !preferences.visionModel.isEmpty ? preferences.visionModel : preferences.model
        generation = Task { [weak self] in
            guard let self else { return }
            defer { self.busy = false; self.generation = nil; self.saveHistory() }
            do {
                try await API.stream(endpoint: endpoint, key: apiKey, model: model, messages: turns, image: image) { chunk in
                    guard let ci = self.chats.firstIndex(where: { $0.id == id }), let mi = self.chats[ci].messages.firstIndex(where: { $0.id == answer.id }) else { return }
                    self.chats[ci].messages[mi].text += chunk; self.status = "Ответ поступает…"
                }
                self.status = "Ответ готов"
            } catch { self.status = Task.isCancelled ? "Ответ остановлен" : error.localizedDescription
                if let ci = self.chats.firstIndex(where: { $0.id == id }), let mi = self.chats[ci].messages.firstIndex(where: { $0.id == answer.id }), self.chats[ci].messages[mi].text.isEmpty { self.chats[ci].messages[mi].text = self.status }
            }
        }
    }
    func startAudio() {
        guard !recording, !startingAudio else { return }
        guard consent else { status = "Подтвердите передачу аудио выбранному провайдеру."; return }
        do { _ = try API.request(endpoint: preferences.audioEndpoint, key: audioKey, route: "audio/transcriptions"); if preferences.audioModel.isEmpty { throw AppError(message: "Укажите модель распознавания речи.") } } catch { status = error.localizedDescription; return }
        startingAudio = true
        let session = sessionID
        audio.received = { [weak self] packet in Task { @MainActor in
            guard let self, self.recording, self.sessionID == session else { return }
            if !self.audioQueue.append(packet) { self.status = "Очередь заполнена: фрагмент аудио пропущен." }
            self.runAudioQueue(session: session)
        } }
        audio.failed = { [weak self] message in Task { @MainActor in self?.stopAudio(); self?.status = message } }
        Task {
            do {
                try await audio.start(mic: useMicrophone, system: useSystem, device: preferences.microphone)
                if sessionID != session { await audio.stop() } else { recording = true; status = "Идёт запись. Скрытие окна не останавливает запись." }
            } catch { status = error.localizedDescription }
            startingAudio = false
        }
    }
    func stopAudio() {
        sessionID = UUID(); recording = false; audioWorker?.cancel(); audioWorker = nil; audioQueue.clear(); summarizer?.cancel(); summarizer = nil
        Task { await audio.stop() }
    }
    func runAudioQueue(session: UUID) {
        guard audioWorker == nil else { return }
        let endpoint = preferences.audioEndpoint, token = audioKey, model = preferences.audioModel
        audioWorker = Task {
            defer { if sessionID == session { audioWorker = nil } }
            while sessionID == session, !Task.isCancelled, var packet = audioQueue.pop() {
                defer { packet.wav.resetBytes(in: 0..<packet.wav.count) }
                do {
                    let text = try await API.transcribe(packet.wav, endpoint: endpoint, key: token, model: model)
                    guard sessionID == session, !Task.isCancelled else { return }
                    if !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { transcript.append("\(packet.source): \(text)"); summarizeIfNeeded() }
                } catch { if sessionID == session { status = "Транскрибация: \(error.localizedDescription)" } }
            }
        }
    }
    func summarizeIfNeeded() {
        guard transcript.joined().count > 12000 else { return }
        if summarizer != nil {
            while transcript.joined().count > 24000 { transcript.removeFirst(); status = "Достигнут лимит разговора: ранняя реплика удалена." }; return
        }
        let count = max(1, transcript.count / 2), prefix = Array(transcript.prefix(count)), session = sessionID
        let turns = [Message(role: "system", text: "Кратко суммируй факты и вопросы разговора, до 2000 символов."), Message(role: "user", text: summary + "\n" + prefix.joined(separator: "\n"))]
        summarizer = Task {
            var result = ""
            defer { if sessionID == session { summarizer = nil } }
            do {
                try await API.stream(endpoint: preferences.endpoint, key: key, model: preferences.model, messages: turns) { result += $0 }
                guard sessionID == session, !Task.isCancelled else { return }
                summary = String(result.prefix(4000))
                if Array(transcript.prefix(count)) == prefix { transcript.removeFirst(count) }
            } catch { if sessionID == session { status = "Не удалось обновить резюме разговора."; while transcript.joined().count > 24000 { transcript.removeFirst() } } }
        }
    }
    func panic() { captureSession = UUID(); generation?.cancel(); stopAudio(); window?.orderOut(nil) }
    func foreground() { window?.level = onTop ? .floating : .normal; window?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    func behind() { onTop = false; window?.level = .normal; window?.orderBack(nil) }
    func capture(region: Bool) {
        guard !screenshotBusy else { return }
        screenshotBusy = true; let session = captureSession; window?.orderOut(nil)
        Task {
            defer { screenshotBusy = false; if captureSession == session { foreground() } }
            do {
                if !region {
                    let data = try await DesktopCapture.allScreens()
                    if captureSession == session { screenshot = data; status = "Снимок всех экранов прикреплён. Отправка — только вместе с сообщением." }
                    return
                }
                let data = try await Task.detached { () throws -> Data? in
                    let url = FileManager.default.temporaryDirectory.appendingPathComponent("GhostGPT-\(UUID()).png")
                    defer { try? FileManager.default.removeItem(at: url) }
                    let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                    process.arguments = region ? ["-x", "-i", "-s", "-t", "png", url.path] : ["-x", "-m", "-t", "png", url.path]
                    try process.run(); process.waitUntilExit()
                    guard process.terminationStatus == 0, FileManager.default.fileExists(atPath: url.path) else { return nil }
                    let original = try Data(contentsOf: url)
                    guard let image = NSImage(data: original) else { throw AppError(message: "Не удалось прочитать снимок.") }
                    let scale = min(1, 2048 / max(image.size.width, image.size.height)), size = NSSize(width: image.size.width * scale, height: image.size.height * scale)
                    let resized = NSImage(size: size); resized.lockFocus(); image.draw(in: NSRect(origin: .zero, size: size)); resized.unlockFocus()
                    guard let tiff = resized.tiffRepresentation, let result = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]), result.count < 10_000_000 else { throw AppError(message: "Изображение слишком большое.") }; return result
                }.value
                if captureSession == session { screenshot = data; status = data == nil ? "Снимок отменён или недоступен. Проверьте разрешение записи экрана." : "Снимок прикреплён. Он отправится только вместе с сообщением." }
            } catch { status = error.localizedDescription }
        }
    }
}

enum DesktopCapture {
    static func allScreens() async throws -> Data {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard !content.displays.isEmpty else { throw AppError(message: "Разрешите запись экрана в настройках macOS.") }
        let own = content.applications.filter { $0.processID == ProcessInfo.processInfo.processIdentifier }
        let union = content.displays.map { CGDisplayBounds($0.displayID) }.reduce(CGRect.null) { $0.union($1) }
        let scale = min(1, 2048 / max(union.width, union.height))
        let width = max(1, Int(union.width * scale)), height = max(1, Int(union.height * scale))
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { throw AppError(message: "Недостаточно памяти для снимка.") }
        for display in content.displays {
            let config = SCStreamConfiguration(); let bounds = CGDisplayBounds(display.displayID)
            config.width = max(1, Int(bounds.width * scale)); config.height = max(1, Int(bounds.height * scale)); config.showsCursor = false
            let filter = SCContentFilter(display: display, excludingApplications: own, exceptingWindows: [])
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
            context.draw(image, in: CGRect(x: (bounds.minX - union.minX) * scale, y: (union.maxY - bounds.maxY) * scale, width: bounds.width * scale, height: bounds.height * scale))
        }
        guard let image = context.makeImage(), let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]), png.count < 10_000_000 else { throw AppError(message: "Не удалось подготовить снимок.") }
        return png
    }
}
