using System;
using System.Runtime.InteropServices;

namespace HFTrainer.Injector
{
    internal static class Native
    {
        public const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        public const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000;
        public const uint PAGE_READWRITE = 0x04, PAGE_EXECUTE_READWRITE = 0x40;
        public const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProc, IntPtr addr, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProc, IntPtr addr, UIntPtr size, uint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProc, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr hProc, IntPtr attr, UIntPtr stack, IntPtr start, IntPtr param, uint flags, out uint tid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr hProc, out bool wow64);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EnumProcessModulesEx(IntPtr hProc, [Out] IntPtr[] modules, int cb, out int needed, uint filter);

        [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetModuleBaseName(IntPtr hProc, IntPtr hMod, System.Text.StringBuilder name, int size);

        public const uint LIST_MODULES_ALL = 0x03;
    }
}
