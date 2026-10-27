using System.Windows;
namespace GhostGPT;
public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon? trayIcon;
    private System.Drawing.Icon? ghostTrayIcon;
    private void InitializeTray()
    {
        Loaded += (_, _) =>
        {
            if (trayIcon is not null) return;
            try
            {
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Открыть Ghost GPT", null, (_, _) => Dispatcher.Invoke(BringForward));
                menu.Items.Add("Скрыть окно", null, (_, _) => Dispatcher.Invoke(Hide));
                menu.Items.Add("Остановить запись и скрыть", null, (_, _) => Dispatcher.Invoke(() => { OnPanic(); Hide(); }));
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Выйти", null, (_, _) => Dispatcher.Invoke(Close));
                ghostTrayIcon = new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory,"ghost.ico"));
                trayIcon = new System.Windows.Forms.NotifyIcon { Text = "Ghost GPT", Icon = ghostTrayIcon, ContextMenuStrip = menu, Visible = true };
                trayIcon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(BringForward); };
                ShowInTaskbar = false;
            }
            catch { ShowInTaskbar = true; vm.Status = "Не удалось создать значок в трее. Окно оставлено на панели задач."; }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && trayIcon is not null) Hide(); };
        Closed += (_, _) => { if (trayIcon is not null) { trayIcon.Visible = false; trayIcon.ContextMenuStrip?.Dispose(); trayIcon.Dispose(); trayIcon = null; } ghostTrayIcon?.Dispose(); };
    }
}
