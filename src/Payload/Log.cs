using System;
using System.IO;

namespace HFTrainer
{
    internal static class Log
    {
        private static readonly object Sync = new object();
        public static string Dir
        {
            get
            {
                try { return Environment.CurrentDirectory; } catch { return "."; }
            }
        }
        public static string FilePath => Path.Combine(Dir, "HFTrainer.log");

        public static void Write(string msg)
        {
            try
            {
                lock (Sync)
                    File.AppendAllText(FilePath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
