import AppKit
import SwiftUI
import Carbon

@main enum GhostApplication {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}
@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let model = Model()
    var window: NSWindow!
    var tray: NSStatusItem!
    var keys: [EventHotKeyRef] = []
    var handler: EventHandlerRef?
    func applicationDidFinishLaunching(_ notification: Notification) {
        if CommandLine.arguments.contains("--smoke-test") {
            let sample = Chat(title: "Асинхронность и параллелизм", messages: [Message(role: "user", text: "Чем асинхронный код отличается от параллельного?"), Message(role: "assistant", text: "Асинхронность позволяет **не блокировать ожидание**, а параллелизм — выполнять несколько вычислений одновременно.\n\n## Пример\n\n```swift\nlet result = try await fetchAnswer()\nprint(result)\n```\n\nОдин подход не исключает другой.")])
            model.chats = [sample]; model.selected = sample.id
        }
        window = NSWindow(contentRect: NSRect(x: 100, y: 100, width: 1000, height: 780), styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        window.title = "Ghost GPT"; window.titlebarAppearsTransparent = true; window.isOpaque = false; window.backgroundColor = .clear
        window.contentMinSize = NSSize(width: model.preferences.sidebarWidth + 475, height: 420); window.isMovableByWindowBackground = true; window.isReleasedWhenClosed = false
        window.collectionBehavior = [.fullScreenPrimary]; window.level = .floating; window.delegate = self
        window.contentView = NSHostingView(rootView: MainView(model: model))
        window.setFrameAutosaveName("GhostGPT.Main")
        if !NSScreen.screens.contains(where: { $0.visibleFrame.intersects(window.frame) }) { window.center() }
        if let screen = window.screen ?? NSScreen.main {
            var rect = window.frame; rect.size.width = min(rect.width, screen.visibleFrame.width); rect.size.height = min(rect.height, screen.visibleFrame.height)
            rect.origin.x = min(max(rect.origin.x, screen.visibleFrame.minX), screen.visibleFrame.maxX - rect.width)
            rect.origin.y = min(max(rect.origin.y, screen.visibleFrame.minY), screen.visibleFrame.maxY - rect.height); window.setFrame(rect, display: false)
        }
        model.window = window; model.hotkeyUpdate = { [weak self] in self?.registerKeys() }
        tray = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        tray.button?.image = NSImage(systemSymbolName: "bubble.left.and.text.bubble.right.fill", accessibilityDescription: "Ghost GPT")
        let menu = NSMenu()
        for (title, action) in [("Показать Ghost GPT", #selector(show)), ("Скрыть (запись продолжится)", #selector(hide)), ("Остановить всё и скрыть", #selector(panic)), ("Выйти", #selector(quit))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: ""); item.target = self; menu.addItem(item)
        }
        tray.menu = menu
        var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            let delegate = Unmanaged<AppDelegate>.fromOpaque(context).takeUnretainedValue()
            let value = id.id
            Task { @MainActor in delegate.hotkey(value) }
            return noErr
        }, 1, &event, Unmanaged.passUnretained(self).toOpaque(), &handler)
        registerKeys(); model.foreground()
        if CommandLine.arguments.contains("--smoke-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                if let position = CommandLine.arguments.firstIndex(of: "--snapshot"), CommandLine.arguments.indices.contains(position + 1), let view = self.window.contentView, let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                    view.cacheDisplay(in: view.bounds, to: bitmap)
                    if let data = bitmap.representation(using: .png, properties: [:]) { try? data.write(to: URL(fileURLWithPath: CommandLine.arguments[position + 1])) }
                }
                NSApp.terminate(nil)
            }
        }
    }
    func registerKeys() {
        for key in keys { UnregisterEventHotKey(key) }; keys.removeAll()
        let values = model.preferences.hotkeys.count == 5 ? model.preferences.hotkeys : [49,36,1,4,125]
        for (index, code) in values.enumerated() {
            var ref: EventHotKeyRef?
            let result = RegisterEventHotKey(code, UInt32(controlKey | shiftKey), EventHotKeyID(signature: 0x47485354, id: UInt32(index)), GetApplicationEventTarget(), 0, &ref)
            if result == noErr, let ref { keys.append(ref) } else { model.status = "Сочетание №\(index+1) занято. Измените его в настройках." }
        }
    }
    func hotkey(_ id: UInt32) {
        switch id {
        case 0: if window.isKeyWindow { model.behind() } else { model.onTop = true; model.foreground() }
        case 1: model.send(conversation: true)
        case 2: model.capture(region: false)
        case 3: model.panic()
        case 4: model.behind()
        default: break
        }
    }
    @objc func show() { model.foreground() }
    @objc func hide() { window.orderOut(nil) }
    @objc func panic() { model.panic() }
    @objc func quit() { NSApp.terminate(nil) }
    func windowShouldClose(_ sender: NSWindow) -> Bool { sender.orderOut(nil); return false }
    func applicationWillTerminate(_ notification: Notification) {
        model.generation?.cancel(); model.stopAudio()
        if CommandLine.arguments.contains("--smoke-test") { return }
        model.flushInstructions(); model.saveHistory()
        // No API key writes on exit; keys change only after Save settings.
        if model.saveAllowed { try? model.store?.write(model.preferences, to: "settings.json") }
        for key in keys { UnregisterEventHotKey(key) }; if let handler { RemoveEventHandler(handler) }
    }
}
