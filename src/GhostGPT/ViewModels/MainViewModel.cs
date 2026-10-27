namespace GhostGPT.ViewModels;
public partial class MainViewModel : Observable
{
    private string status = "Готов к работе · настройте подключение";
    public string Status { get => status; set => Set(ref status, value); }
    private double windowOpacity = 0.97;
    public double WindowOpacity { get => windowOpacity; set => Set(ref windowOpacity, value); }
}
