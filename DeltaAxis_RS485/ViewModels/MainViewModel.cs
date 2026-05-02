using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DeltaAxis_RS485.Models;

namespace DeltaAxis_RS485.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private bool _disposed;

    public ObservableCollection<AxisViewModel> Axes { get; } = [];

    private AxisViewModel? _selectedAxis;
    public AxisViewModel? SelectedAxis
    {
        get => _selectedAxis;
        set => SetField(ref _selectedAxis, value);
    }

    public ICommand ConnectAllCommand { get; }
    public ICommand DisconnectAllCommand { get; }

    public MainViewModel()
    {
        // ============================
        // 軸組態（實務上可從 JSON 讀取）
        // ============================
        var configs = new List<AxisConfig>
        {
            new() { Name = "Y軸", PortName = "COM12", SlaveId = 1 },
            new() { Name = "X軸", PortName = "COM13", SlaveId = 1 },
            new() { Name = "ZL軸", PortName = "COM14", SlaveId = 1 },
            new() { Name = "ZR軸", PortName = "COM15", SlaveId = 1 },
        };

        foreach (var cfg in configs)
            Axes.Add(new AxisViewModel(cfg));

        if (Axes.Count > 0)
            SelectedAxis = Axes[0];

        ConnectAllCommand = new RelayCommand(() =>
        {
            foreach (var ax in Axes)
                if (ax.ConnectCommand.CanExecute(null))
                    ax.ConnectCommand.Execute(null);
        });

        DisconnectAllCommand = new RelayCommand(() =>
        {
            foreach (var ax in Axes)
                if (ax.DisconnectCommand.CanExecute(null))
                    ax.DisconnectCommand.Execute(null);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var ax in Axes) ax.Dispose();
        GC.SuppressFinalize(this);
    }
}