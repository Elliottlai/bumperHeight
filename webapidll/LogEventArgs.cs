using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApidll
{
    public class LogEventArgs : EventArgs
    {
        public event EventHandler<LogEventArgs> Logged;
        public DateTime Time { get; }
        public object Content { get; }

        public LogEventArgs(DateTime time, object content)
        {
            Time = time;
            Content = content;
        }

        public LogEventArgs(object content) : this(DateTime.Now, content)
        {
        }

        public override string ToString()
            => $"[{Time:yyyy/MM/dd HH:mm:ss}]{Content}";
    }
}
