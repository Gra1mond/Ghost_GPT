# Windows 0.2.0 acceptance specification

- Constant arrow is enabled by default inside the main window and on resize edges, with a setting to restore normal cursors. Input, selection, copying, dragging and resizing still work. Other windows and system dialogs are unaffected.
- Resume page imports PDF text layers, DOCX and TXT locally. Reject files over 10 MiB, extracted text over 50,000 characters, missing text and damaged documents. No OCR. Show extracted text; replace/delete; store only extracted text encrypted with Windows DPAPI. Resume use defaults off. When enabled, both chat and conversation receive at most 12,000 characters as untrusted user context, never system instructions.
- Question page contains 200 unique original Russian questions, fifty each Go, SQL, Kubernetes, Kafka, with short answers and primary-source links. Search and filter locally. Only the selected question is inserted into composer, requiring explicit send.
- Ship a self-contained Windows x64 ZIP after automated checks, code review and smoke checks. List unverified conferencing apps and live-provider limits.
