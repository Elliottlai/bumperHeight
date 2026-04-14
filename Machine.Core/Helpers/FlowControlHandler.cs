using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core.Helpers
{


    public class FlowControlHandler
    {
        public int ActionStep { get; private set; } = 0;

        public int Step { get; private set; } = 0;

        int StartStep = 0;

        public Stopwatch Timer = new Stopwatch();

        public string Description;

        public Dictionary<int, string> DescriptionFormat { get; private set; } = new Dictionary<int, string>();

        private Stopwatch StepTimer = new Stopwatch();

        public int NextActionStep { get; private set; } = 0;

        int NextStep;

        ConcurrentQueue<bool> AllowNextAction = new ConcurrentQueue<bool>();

        string FCName = "";

        // Updated FilePath to use Assembly-based path resolution  
        string FilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "FlowDescription");

        public FlowControlHandler(string Name, int nStartStep = 0)
        {
            FCName = Name;
            StartStep = nStartStep;

            if (!Directory.Exists(FilePath))
                Directory.CreateDirectory(FilePath);

            if (File.Exists(Path.Combine(FilePath, Name)))
            {
                DescriptionFormat = JsonHelper.Load<Dictionary<int, string>>(Path.Combine(FilePath, Name));
                if (DescriptionFormat.Count == 0)
                    File.Delete(Path.Combine(FilePath, Name));
            }
        }

        private void StepReset()
        {
            Step = StartStep;
        }

        public void ReStart()
        {
            Step = StartStep;
            ActionStep = 0;

            NextActionStep = -1;
            NextStep = -1;
            while (AllowNextAction.TryDequeue(out bool ok)) ;
        }

        public void SetDescription(string defaultFormat, params object[] v)
        {
            string F = defaultFormat;
            if (DescriptionFormat.ContainsKey(Step))
                F = DescriptionFormat[Step];
            else
                DescriptionFormat.Add(Step, defaultFormat);

            Description = string.Format(F, v);
        }

        public void ActionCheck()
        {
            if (NextActionStep != -1 && AllowNextAction.Count > 0)
            {
                AllowNextAction.TryDequeue(out bool ok);
                ActionStep = NextActionStep;
                NextActionStep = -1;
                StepReset();
            }
        }

        public void ActionAllow()
        {
            AllowNextAction.Enqueue(true);
        }

        public void ActionClaim(int Step = -1)
        {
            ActionStep = -1;
            NextActionStep = (int)Step;
        }

        public bool ActionWaiting() => ActionStep != -1 || (AllowNextAction.Count > 0);

        public void StepClaim(int step)
        {
            StepTimer.Restart();
            NextStep = step;
        }

        public void StepCheck()
        {
            if (NextStep != -1)
            {
                Step = NextStep;
                NextStep = -1;
                Description = "*";
            }
        }

        public double StepTime()
        {
            return StepTimer.ElapsedMilliseconds / 1000.0;
        }

        ~FlowControlHandler()
        {
            DescriptionFormat?.ToJsonFile(Path.Combine(FilePath, FCName));
        }
    }
}
