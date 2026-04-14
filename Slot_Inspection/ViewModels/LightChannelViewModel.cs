using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Synpower.Lighting.Application;
using Synpower.Lighting.Domain;

namespace Synpower.Lighting.Presentation
{
    // 不依賴外部套件，手寫 INotifyPropertyChanged
    public class LightChannelViewModel : INotifyPropertyChanged
    {
        public LightChannelId UiId { get; }
        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private int _value; // 0..100
        public int Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = Math.Clamp(value, 0, 100);
                OnPropertyChanged();
                ScheduleSend(); // debounce 後送指令
            }
        }

        private readonly LightService _svc;
        private CancellationTokenSource _cts;

        public LightChannelViewModel(LightChannelId id, string displayName, LightService svc)
        {
            UiId = id; _name = displayName; _svc = svc;
        }

        private void ScheduleSend()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = DebouncedSendAsync(_cts.Token);
        }

        private async Task DebouncedSendAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(60, token); // 60ms debounce
                await _svc.SetByUiAsync(UiId, Value, token);
            }
            catch (OperationCanceledException) { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
     