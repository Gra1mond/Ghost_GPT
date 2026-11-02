using System.IO;
using System.Windows;
using System.Windows.Controls;
using GhostGPT.Services;
namespace GhostGPT;
public partial class MainWindow
{
    private ResumeData? resume;
    private ResumeStore resumeStore = null!;
    private TextBox resumePreview = null!;
    private TextBlock resumeStatus = null!;
    private void ShowResume(object sender, RoutedEventArgs e) => Navigate(ResumePage);
    private void ShowQuestions(object sender, RoutedEventArgs e) => Navigate(QuestionsPage);
    private void RefreshResume()
    {
        resumePreview.Text = resume?.Text ?? "";
        resumeStatus.Text = resume is null ? "Резюме не загружено" : resume.Name + " · " + resume.Text.Length + " символов";
        vm.SupplementalContext = InterviewContext.Resume(resume, settings.UseResume);
    }
    private void InitializeInterviewTools()
    {
        resumeStore = new(settingsStore.DirectoryPath);
        var panel = new DockPanel(); ResumePage.Children.Add(panel);
        var top = new StackPanel { Margin = new(0,0,0,14) }; DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Резюме", FontSize = 22, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "PDF с текстом, DOCX или TXT · до 10 МБ / 50 000 символов. Разбор локальный, без OCR.", TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel(); top.Children.Add(actions);
        var load = new Button { Content = "Загрузить / заменить" }; actions.Children.Add(load);
        var delete = new Button { Content = "Удалить резюме" }; actions.Children.Add(delete);
        var use = new CheckBox { Content = "Использовать резюме в ответах (текст передаётся API)", IsChecked = settings.UseResume }; top.Children.Add(use);
        resumeStatus = new TextBlock { TextWrapping = TextWrapping.Wrap }; top.Children.Add(resumeStatus);
        resumePreview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(resumePreview);
        try { resume = resumeStore.Load(); } catch { vm.Status = "Не удалось расшифровать резюме. Можно заменить или удалить его."; }
        RefreshResume();
        use.Click += (_, _) => { settings.UseResume = use.IsChecked == true; RefreshResume(); SaveSettings(); };
        load.Click += async (_, _) => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Резюме|*.pdf;*.docx;*.txt", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            load.IsEnabled = delete.IsEnabled = false;
            try { var imported = await Task.Run(() => ResumeDocument.Import(dialog.FileName)); resumeStore.Save(imported); resume = imported; RefreshResume(); vm.Status = "Резюме сохранено с защитой Windows DPAPI."; }
            catch (InvalidDataException ex) { vm.Status = ex.Message; }
            catch { vm.Status = "Не удалось прочитать резюме. Проверьте формат, доступ и целостность файла."; }
            finally { load.IsEnabled = delete.IsEnabled = true; }
        };
        delete.Click += (_, _) => { try { resumeStore.Delete(); resume = null; settings.UseResume = false; use.IsChecked = false; RefreshResume(); SaveSettings(); } catch { vm.Status = "Не удалось удалить резюме."; } };
        InitializeQuestionCatalog();
    }
    private void InitializeQuestionCatalog()
    {
        var root = new DockPanel(); QuestionsPage.Children.Add(root);
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "База вопросов · 200", FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Авторские краткие ответы для подготовки. SQL — с примерами поведения PostgreSQL. В API уходит только выбранный вами вопрос.", TextWrapping = TextWrapping.Wrap });
        var filter = new ComboBox { ItemsSource = new[] { "Все", "Go", "SQL", "Kubernetes", "Kafka" }, SelectedIndex = 0, Margin = new(0,12,0,6) }; header.Children.Add(filter);
        header.Children.Add(new TextBlock { Text = "Поиск по вопросам и ответам", Margin = new(0,6,0,0), FontSize = 12 });
        var search = new TextBox { ToolTip = "Поиск по вопросам и ответам", Margin = new(0,0,0,8) }; header.Children.Add(search);
        var count = new TextBlock(); header.Children.Add(count);
        var cards = new StackPanel(); root.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        QuestionCatalog catalog;
        try { catalog = QuestionCatalog.Load(); } catch { count.Text = "Не удалось загрузить базу вопросов."; return; }
        void Refresh()
        {
            cards.Children.Clear(); var found = catalog.Search(filter.SelectedItem as string, search.Text); count.Text = $"Найдено: {found.Count}";
            foreach (var question in found)
            {
                var body = new StackPanel { Margin = new(14) };
                body.Children.Add(new TextBlock { Text = question.Topic + " · " + question.Id, FontSize = 12, Foreground = System.Windows.Media.Brushes.Gray });
                body.Children.Add(new TextBlock { Text = question.Question, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,8) });
                body.Children.Add(new TextBox { Text = question.Answer, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent });
                var actions = new WrapPanel(); body.Children.Add(actions);
                var source = new Button { Content = "Источник ↗", ToolTip = question.Source }; actions.Children.Add(source);
                source.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(question.Source) { UseShellExecute = true }); } catch { vm.Status = "Не удалось открыть источник."; } };
                var discuss = new Button { Content = "Разобрать в чате" }; actions.Children.Add(discuss);
                discuss.Click += (_, _) => { if (vm.Busy || conversationBusy) { vm.Status = "Сначала завершите текущий ответ."; return; } PromptBox.Text = QuestionCatalog.Prompt(question); Navigate(ChatPage); PromptBox.Focus(); };
                cards.Children.Add(new Border { Child = body, CornerRadius = new(12), BorderThickness = new(1), BorderBrush = System.Windows.Media.Brushes.DimGray, Margin = new(0,8,0,8) });
            }
        }
        filter.SelectionChanged += (_, _) => Refresh(); search.TextChanged += (_, _) => Refresh(); Refresh();
    }
}
