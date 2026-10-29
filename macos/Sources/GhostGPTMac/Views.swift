import SwiftUI
import AppKit
import AVFoundation

struct GhostMark: View {
    var active: Bool
    @Environment(\.accessibilityReduceMotion) var reduced
    @State private var flying = false
    var body: some View {
        ZStack {
            GhostShape().fill(LinearGradient(colors: [.white, Color(red: 0.67, green: 0.71, blue: 0.90)], startPoint: .topLeading, endPoint: .bottomTrailing))
            HStack(spacing: 6) { Ellipse().frame(width: 4, height: 7); Ellipse().frame(width: 4, height: 7) }.foregroundStyle(Color.black.opacity(0.8)).offset(y: -3)
            Ellipse().fill(.black.opacity(0.75)).frame(width: 5, height: 6).offset(y: 6)
        }.frame(width: 30, height: 34).rotationEffect(.degrees(active && flying && !reduced ? 6 : -3))
            .offset(y: active && flying && !reduced ? -3 : 0)
            .animation(active && !reduced ? .easeInOut(duration: 0.9).repeatForever(autoreverses: true) : nil, value: flying)
            .onAppear { flying = active }.onChange(of: active) { flying = $0 }
            .accessibilityLabel(active ? "Ghost отвечает" : "Ghost")
    }
}
struct GhostShape: Shape {
    func path(in rect: CGRect) -> Path {
        var p = Path(); let w = rect.width, h = rect.height
        p.move(to: CGPoint(x: 0.1*w, y: 0.9*h)); p.addCurve(to: CGPoint(x: 0.9*w, y: 0.9*h), control1: CGPoint(x: -0.12*w,y: -0.32*h), control2: CGPoint(x: 1.12*w,y: -0.32*h))
        p.addLine(to: CGPoint(x: 0.7*w,y: 0.78*h)); p.addQuadCurve(to: CGPoint(x: 0.5*w,y: h), control: CGPoint(x: 0.65*w,y: 1.1*h))
        p.addQuadCurve(to: CGPoint(x: 0.3*w,y: 0.78*h), control: CGPoint(x: 0.35*w,y: 1.1*h)); p.closeSubpath(); return p
    }
}
struct MarkdownAnswer: View {
    let text: String
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(Array(text.components(separatedBy: "```").enumerated()), id: \.offset) { index, block in
                if index % 2 == 1 {
                    let lines = block.components(separatedBy: "\n")
                    let code = lines.dropFirst().joined(separator: "\n")
                    VStack(alignment: .leading, spacing: 0) {
                        HStack { Text(lines.first?.isEmpty == false ? lines[0] : "Код").font(.caption.monospaced()); Spacer(); Button("Копировать") { copyText(code) }.buttonStyle(.plain) }.padding(10).background(.white.opacity(0.06))
                        ScrollView(.horizontal) { Text(code).font(.system(size: 13, design: .monospaced)).textSelection(.enabled).padding(14) }
                    }.background(.black.opacity(0.25)).clipShape(RoundedRectangle(cornerRadius: 12)).overlay(RoundedRectangle(cornerRadius: 12).stroke(.white.opacity(0.12)))
                } else {
                    ForEach(Array(block.components(separatedBy: "\n\n").enumerated()), id: \.offset) { _, paragraph in
                        let heading = paragraph.hasPrefix("#")
                        let content = heading ? paragraph.drop(while: { $0 == "#" || $0 == " " }).description : paragraph
                        Text((try? AttributedString(markdown: content, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace))) ?? AttributedString(content))
                            .font(heading ? .title3.weight(.semibold) : .system(size: 15)).lineSpacing(5).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
        }
    }
}
func copyText(_ text: String) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string) }
struct ChatBubble: View {
    let message: Message; let busy: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                if message.role == "assistant" { GhostMark(active: busy) } else { Image(systemName: "person.crop.circle.fill").font(.title2).foregroundStyle(.secondary) }
                Text(message.role == "user" ? "Вы" : "Ghost").fontWeight(.semibold)
                Spacer(); if !message.text.isEmpty { Button { copyText(message.text) } label: { Image(systemName: "doc.on.doc") }.buttonStyle(.plain).help("Копировать ответ") }
            }
            if message.text.isEmpty && busy { Text("Ожидаю ответ модели…").foregroundStyle(.secondary) } else { MarkdownAnswer(text: message.text) }
            if busy && !message.text.isEmpty { Text("Ответ поступает…").font(.caption).foregroundStyle(.secondary) }
        }.padding(20).frame(maxWidth: .infinity, alignment: .leading)
            .background(.white.opacity(message.role == "user" ? 0.09 : 0.035)).clipShape(RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(.white.opacity(0.08)))
            .padding(.leading, message.role == "user" ? 28 : 0)
    }
}
struct MainView: View {
    @ObservedObject var model: Model
    @State private var follow = true
    @State private var initialSidebarWidth: Double?
    var body: some View {
        HStack(spacing: 0) {
            if model.sidebar && !model.compact {
                sidebar.frame(width: model.preferences.sidebarWidth)
                Rectangle().fill(.white.opacity(0.12)).frame(width: 5).onHover { if $0 { NSCursor.resizeLeftRight.push() } else { NSCursor.pop() } }
                    .gesture(DragGesture().onChanged { if initialSidebarWidth == nil { initialSidebarWidth = model.preferences.sidebarWidth }; model.preferences.sidebarWidth = min(360, max(200, initialSidebarWidth! + $0.translation.width)) }.onEnded { _ in initialSidebarWidth = nil; model.saveSettings() })
            }
            VStack(spacing: 14) {
                header
                if model.page == "Настройки" { settings }
                else if model.page == "Инструкции" { instructions }
                else if model.page == "Разговор" { conversation }
                else { chat }
                Text(model.status).font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).lineLimit(3)
            }.padding(20).frame(minWidth: 430, maxWidth: .infinity, maxHeight: .infinity)
        }.background(Color(red: 0.08, green: 0.085, blue: 0.10).opacity(model.preferences.opacity)).preferredColorScheme(.dark)
            .onChange(of: model.selected) { _ in model.prompt = ""; model.screenshot = nil; follow = true }
            .onChange(of: model.sidebar) { _ in resizeMinimum() }
            .onChange(of: model.preferences.sidebarWidth) { _ in resizeMinimum() }
            .onChange(of: model.instructions) { _ in model.saveInstructions() }
            .onDisappear { model.flushInstructions() }
    }
    func resizeMinimum() { model.window?.contentMinSize = NSSize(width: model.sidebar && !model.compact ? model.preferences.sidebarWidth + 475 : 480, height: 420) }
    var sidebar: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack { Text("Ghost").font(.title2.weight(.semibold)); Spacer(); Button { model.sidebar = false } label: { Image(systemName: "sidebar.left") }.buttonStyle(.plain).help("Скрыть панель") }
            Button { model.newChat() } label: { Label("Новый чат", systemImage: "square.and.pencil").frame(maxWidth: .infinity, alignment: .leading) }.disabled(model.busy)
            TextField("Поиск чатов", text: $model.search).textFieldStyle(.roundedBorder)
            ScrollView {
                VStack(alignment: .leading, spacing: 8) {
                    Text("Чаты").foregroundStyle(.secondary).font(.caption.weight(.semibold))
                    chatRows(folder: "")
                    HStack { Text("Папки").foregroundStyle(.secondary).font(.caption.weight(.semibold)); Spacer(); Button { model.showFolderEditor.toggle() } label: { Image(systemName: "plus") }.buttonStyle(.plain) }
                    if model.showFolderEditor {
                        TextField("Название папки", text: $model.folderName).onSubmit { createFolder() }
                        HStack { Button("Создать", action: createFolder); Button("Отмена") { model.showFolderEditor = false; model.folderName = "" }.keyboardShortcut(.cancelAction) }
                    }
                    ForEach(model.preferences.folders, id: \.self) { folder in
                        DisclosureGroup { chatRows(folder: folder); Button("Новый чат в папке") { model.newChat(folder: folder) }.disabled(model.busy).font(.caption) } label: { Label(folder, systemImage: "folder") }
                    }
                }.padding(.vertical, 8)
            }
            Spacer(minLength: 0)
            ForEach([("Чат", "bubble.left"), ("Разговор", "waveform"), ("Инструкции", "doc.text"), ("Настройки", "slider.horizontal.3")], id: \.0) { name, icon in
                Button { model.page = name } label: { Label(name, systemImage: icon).frame(maxWidth: .infinity, alignment: .leading).padding(8).background(model.page == name ? .white.opacity(0.10) : .clear).clipShape(RoundedRectangle(cornerRadius: 9)) }.buttonStyle(.plain)
            }
            Text("История зашифрована на Mac").font(.caption2).foregroundStyle(.secondary)
        }.padding(18).background(.black.opacity(0.16))
    }
    func createFolder() { let name = model.folderName.trimmingCharacters(in: .whitespacesAndNewlines); guard !name.isEmpty, !model.preferences.folders.contains(name), name.count <= 60 else { return }; model.preferences.folders.append(name); model.folderName = ""; model.showFolderEditor = false; model.saveSettings() }
    func chatRows(folder: String) -> some View {
        ForEach(model.chats.filter { $0.folder == folder && (model.search.isEmpty || $0.title.localizedCaseInsensitiveContains(model.search) || $0.messages.contains { $0.text.localizedCaseInsensitiveContains(model.search) }) }) { chat in
            Button { model.selected = chat.id; model.page = "Чат" } label: { Text(chat.title).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading).padding(10).background(model.selected == chat.id ? .white.opacity(0.10) : .clear).clipShape(RoundedRectangle(cornerRadius: 10)) }
                .buttonStyle(.plain).disabled(model.busy).contextMenu {
                    Menu("Переместить") { ForEach([""] + model.preferences.folders, id: \.self) { name in Button(name.isEmpty ? "Без папки" : name) { if let i = model.chats.firstIndex(where: { $0.id == chat.id }) { model.chats[i].folder = name; model.saveHistory() } } } }
                    Button("Удалить диалог", role: .destructive) { model.delete(chat) }
                }
        }
    }
    var header: some View {
        HStack {
            if !model.sidebar || model.compact { Button { model.compact = false; model.sidebar = true } label: { Image(systemName: "sidebar.left") }.help("Показать панель") }
            VStack(alignment: .leading, spacing: 3) { Text(model.page).font(.headline); Text(model.preferences.model).font(.caption).foregroundStyle(.secondary) }
            Spacer()
            Button { model.onTop.toggle(); model.window?.level = model.onTop ? .floating : .normal } label: { Image(systemName: model.onTop ? "pin.fill" : "pin") }.help("Поверх других окон")
            Button { model.behind() } label: { Image(systemName: "square.3.layers.3d.bottom.filled") }.help("За другие окна")
            Button { model.compact.toggle(); resizeMinimum(); model.window?.setContentSize(NSSize(width: model.compact ? 520 : 1000, height: model.compact ? 480 : 780)) } label: { Image(systemName: "rectangle.compress.vertical") }.help("Компактный режим")
            Button { model.window?.toggleFullScreen(nil) } label: { Image(systemName: "arrow.up.left.and.arrow.down.right") }.help("Полный экран")
            Button { model.window?.orderOut(nil) } label: { Image(systemName: "minus") }.help("Скрыть — запись продолжится")
        }.buttonStyle(.plain)
    }
    var chat: some View {
        VStack(spacing: 12) {
            ScrollViewReader { proxy in
                ZStack(alignment: .bottomTrailing) {
                    ScrollView {
                        LazyVStack(spacing: 16) {
                            if model.messages.isEmpty { VStack(spacing: 20) { GhostMark(active: false).scaleEffect(1.7); Text("Чем могу помочь?").font(.title.weight(.semibold)); Text("Спросите, покажите задачу или начните разговор.").foregroundStyle(.secondary) }.padding(.vertical, 100) }
                            ForEach(model.compact ? Array(model.messages.suffix(1)) : model.messages) { message in ChatBubble(message: message, busy: model.busy && message.role == "assistant" && message.id == model.messages.last?.id) }
                            Color.clear.frame(height: 1).id("bottom")
                        }.padding(.vertical, 12)
                    }.modifier(ManualScrollObserver { follow = false })
                        .simultaneousGesture(DragGesture().onChanged { _ in follow = false })
                        .onChange(of: model.messages.last?.text) { _ in if follow { proxy.scrollTo("bottom", anchor: .bottom) } }
                        .onChange(of: model.messages.count) { _ in if follow { proxy.scrollTo("bottom", anchor: .bottom) } }
                    Button { follow = true; proxy.scrollTo("bottom", anchor: .bottom) } label: { Image(systemName: "arrow.down").padding(8) }.buttonStyle(.bordered).help("К последнему сообщению").padding(8)
                }
            }
            VStack(alignment: .leading, spacing: 10) {
                if let data = model.screenshot, let image = NSImage(data: data) { HStack { Image(nsImage: image).resizable().scaledToFit().frame(maxHeight: 100); Button("Убрать снимок") { model.screenshot = nil } } }
                TextField("Напишите сообщение…", text: $model.prompt, axis: .vertical).lineLimit(2...6).textFieldStyle(.plain).onSubmit { model.send() }
                HStack(spacing: 12) {
                    Button { model.capture(region: false) } label: { Label("Экран", systemImage: "camera") }.disabled(model.screenshotBusy || model.busy)
                    Button("Область") { model.capture(region: true) }.disabled(model.screenshotBusy || model.busy)
                    Slider(value: $model.preferences.opacity, in: 0.15...1).frame(width: 85).help("Плотность фона; текст остаётся непрозрачным")
                    Spacer()
                    Button { if model.busy { model.generation?.cancel() } else { follow = true; model.send() } } label: { Image(systemName: model.busy ? "stop.fill" : "arrow.up").frame(width: 24, height: 24) }.keyboardShortcut(.return, modifiers: .command).help(model.busy ? "Остановить" : "Отправить · ⌘Enter")
                }.buttonStyle(.borderless)
            }.padding(16).background(.white.opacity(0.07)).clipShape(RoundedRectangle(cornerRadius: 18)).overlay(RoundedRectangle(cornerRadius: 18).stroke(.white.opacity(0.18)))
        }
    }
    var instructions: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("INTERVIEW.md").font(.title2.weight(.semibold)); Text("Сохраняется автоматически. Применяется к следующим запросам.").foregroundStyle(.secondary)
            TextEditor(text: $model.instructions).font(.system(.body, design: .monospaced)).scrollContentBackground(.hidden).padding(12).background(.black.opacity(0.2)).clipShape(RoundedRectangle(cornerRadius: 12))
            Button("Сохранить сейчас") { model.flushInstructions() }
        }
    }
    var settings: some View {
        Form {
            Section("Подключение ИИ") {
                TextField("Адрес API", text: $model.preferences.endpoint); SecureField("API-ключ", text: $model.key); TextField("Модель", text: $model.preferences.model); TextField("Vision-модель (необязательно)", text: $model.preferences.visionModel)
                Button(model.checkingConnection ? "Проверка…" : "Проверить ключ и загрузить модели") { model.loadModels() }.disabled(model.checkingConnection)
                if !model.models.isEmpty { Picker("Доступные модели", selection: $model.preferences.model) { ForEach(Array(Set(model.models + [model.preferences.model])).sorted(), id: \.self) { Text($0).tag($0) } } }
                Button("Wellflow") { if model.preferences.endpoint != "https://api.wellflow.dev/v1" { model.key = "" }; model.preferences.endpoint = "https://api.wellflow.dev/v1"; model.preferences.model = "gpt-5.6-sol" }
            }
            Section("Распознавание речи — отдельный API") {
                TextField("Адрес API", text: $model.preferences.audioEndpoint); SecureField("API-ключ речи", text: $model.audioKey); TextField("Модель транскрибации", text: $model.preferences.audioModel)
                Text("Наличие чата у провайдера не означает поддержку речи.").font(.caption).foregroundStyle(.secondary)
            }
            Section("Окно") {
                Slider(value: $model.preferences.opacity, in: 0.15...1) { Text("Плотность фона") }
                Text("macOS не гарантирует исключение окна из записи экрана. Перед демонстрацией скройте окно и проверьте результат.").font(.caption)
            }
            Section("Глобальные клавиши · Ctrl+Shift") {
                ForEach(0..<5) { index in
                    Picker(["Вперёд / назад", "Ответ по разговору", "Снимок экрана", "Скрыть и остановить", "За другие окна"][index], selection: Binding(get: { model.preferences.hotkeys.count == 5 ? model.preferences.hotkeys[index] : [49,36,1,4,125][index] }, set: { if model.preferences.hotkeys.count != 5 { model.preferences.hotkeys = [49,36,1,4,125] }; model.preferences.hotkeys[index] = $0 })) {
                        ForEach(Array(zip([UInt32(49),36,1,4,125,0,2,3,5,6,7], ["Space","Enter","S","H","↓","A","D","F","G","Z","X"])), id: \.0) { code, name in Text(name).tag(code) }
                    }
                }
            }
            Button("Сохранить настройки") { model.saveSettings() }.buttonStyle(.borderedProminent)
        }.formStyle(.grouped).scrollContentBackground(.hidden)
    }
    var conversation: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack { Toggle("Микрофон", isOn: $model.useMicrophone); Toggle("Звук компьютера", isOn: $model.useSystem) }.disabled(model.recording || model.startingAudio)
            Picker("Микрофон", selection: $model.preferences.microphone) {
                Text("По умолчанию").tag("")
                ForEach(MicrophoneDevices.all, id: \.uniqueID) { device in Text(device.localizedName).tag(device.uniqueID) }
            }.disabled(model.recording || model.startingAudio)
            Text("Системный источник — звук приложений Mac. Отдельное устройство вывода не выбирается.").font(.caption).foregroundStyle(.secondary)
            Toggle("Аудиофрагменты будут отправляться моему провайдеру транскрибации. Участники разговора предупреждены.", isOn: $model.consent).font(.caption).disabled(model.recording)
            HStack {
                Button(model.recording ? "Остановить запись" : model.startingAudio ? "Подключение…" : model.stoppingAudio ? "Остановка…" : "Начать запись") { if model.recording { model.stopAudio() } else { model.startAudio() } }.disabled(model.startingAudio || model.stoppingAudio)
                Button("Новая сессия") { model.stopAudio(); model.transcript = []; model.summary = "" }
                Spacer(); if model.recording { Label("Запись", systemImage: "record.circle.fill").foregroundStyle(.red) }
            }
            ScrollView { LazyVStack(alignment: .leading, spacing: 10) { ForEach(Array(model.transcript.enumerated()), id: \.offset) { _, line in Text(line).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) } } }.padding(12).background(.black.opacity(0.15)).clipShape(RoundedRectangle(cornerRadius: 12))
            TextField("Инструкция для ответа по разговору", text: $model.instruction, axis: .vertical).lineLimit(2...4)
            Button("Ответить по разговору · Ctrl+Shift+Enter") { model.send(conversation: true) }.disabled(model.busy || model.transcript.isEmpty)
            Text("Скрытие окна не останавливает запись. Ctrl+Shift+H — скрыть и остановить. Резюме длинного разговора создаёт дополнительные API-запросы.").font(.caption).foregroundStyle(.secondary)
        }
    }
}
