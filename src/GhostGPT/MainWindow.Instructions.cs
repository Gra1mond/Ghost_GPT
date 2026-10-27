using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace GhostGPT;
public partial class MainWindow
{
    private readonly DispatcherTimer instructionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string savedInstructionText = "";
    private string InstructionPath => Path.Combine(AppContext.BaseDirectory, "INTERVIEW.md");
    private void InitializeInstructionEditor()
    {
        try { InstructionEditor.Text = savedInstructionText = File.ReadAllText(InstructionPath); InstructionSaveStatus.Text = "INTERVIEW.md · изменения сохраняются автоматически"; }
        catch { InstructionSaveStatus.Text = "Не удалось прочитать INTERVIEW.md. Проверьте файл рядом с приложением."; }
        InstructionEditor.TextChanged += (_, _) => { instructionSaveTimer.Stop(); instructionSaveTimer.Start(); InstructionSaveStatus.Text = "Есть несохранённые изменения…"; };
        instructionSaveTimer.Tick += (_, _) => SaveInstructions();
        Closing += (_, e) => { if (!SaveInstructions()) e.Cancel = true; };
    }
    private bool SaveInstructions()
    {
        instructionSaveTimer.Stop();
        if (InstructionEditor.Text == savedInstructionText) return true;
        try
        {
            var text = InstructionEditor.Text;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Инструкция не может быть пустой.");
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 128000) throw new InvalidOperationException("Максимальный размер — 128 КБ.");
            File.WriteAllText(InstructionPath + ".tmp", text);
            File.Move(InstructionPath + ".tmp", InstructionPath, true);
            savedInstructionText = text;
            InstructionSaveStatus.Text = "Сохранено · применяется к следующему ответу";
            return true;
        }
        catch (Exception ex)
        {
            InstructionSaveStatus.Text = ex is InvalidOperationException ? ex.Message : "Не удалось сохранить файл. Проверьте права записи и нажмите «Сохранить».";
            vm.Status = InstructionSaveStatus.Text;
            return false;
        }
    }
    private void ShowInstructions(object sender, RoutedEventArgs e) => Navigate(InstructionsPage);
    private void SaveInstructionsClick(object sender, RoutedEventArgs e) => SaveInstructions();
}
