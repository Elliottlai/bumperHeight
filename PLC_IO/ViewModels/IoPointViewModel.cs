using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PLC_IO.ViewModels;

/// <summary>
/// 單一 IO 點位的 ViewModel
/// </summary>
public sealed class IoPointViewModel : INotifyPropertyChanged
{
    private bool _status;
    private readonly bool _isOutput;

    /// <summary>顯示標籤（如 X0, X7, X10, Y0）</summary>
    public string Label { get; }

    /// <summary>在 bool[] 陣列中的實際索引（0-based）</summary>
    public int Index { get; }

    /// <summary>是否為輸出點（可寫入）</summary>
    public bool IsOutput => _isOutput;

    public bool Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public IoPointViewModel(string label, int index, bool isOutput)
    {
        Label = label;
        Index = index;
        _isOutput = isOutput;
    }

    /// <summary>
    /// 將陣列索引轉換為 FX PLC 八進制位址字串
    /// 例：index 0~7 → "0"~"7", index 8~15 → "10"~"17"
    /// </summary>
    public static string ToOctalAddress(string prefix, int index)
    {
        int octal = (index / 8) * 10 + (index % 8);
        return $"{prefix}{octal}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}