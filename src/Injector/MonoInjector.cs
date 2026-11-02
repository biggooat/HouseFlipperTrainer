using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HFTrainer.Injector
{
    /// <summary>
    /// Внедрение управляемой сборки в Unity/Mono процесс через mono-API (x64).
    /// Последовательность: mono_get_root_domain -> mono_image_open_from_data -> mono_assembly_load_from_full
    /// -> mono_assembly_get_image -> mono_class_from_name -> mono_class_get_method_from_name -> mono_runtime_invoke.
    /// </summary>
    internal sealed class MonoInjector : IDisposable
    {
        private readonly IntPtr _h;
        private readonly Dictionary<string, IntPtr> _exports = new Dictionary<string, IntPtr>();
        private readonly List<IntPtr> _allocs = new List<IntPtr>();
        private IntPtr _rootDomain;
        public string MonoModuleName { get; private set; }
        public Action<string> Log = _ => { };

        private static readonly string[] Needed =
        {
            "mono_get_root_domain", "mono_thread_attach", "mono_image_open_from_data",
            "mono_assembly_load_from_full", "mono_assembly_get_image", "mono_class_from_name",
            "mono_class_get_method_from_name", "mono_runtime_invoke"
        };

        public MonoInjector(Process p)
        {
            _h = Native.OpenProcess(Native.PROCESS_ALL_ACCESS, false, p.Id);
            if (_h == IntPtr.Zero)
                throw new Win32Exception("OpenProcess не удался (запустите трейнер от администратора)");

            if (Native.IsWow64Process(_h, out bool wow) && wow)
                throw new InvalidOperationException("Процесс 32-битный, трейнер поддерживает только x64.");

            IntPtr monoBase = FindMonoModule();
            if (monoBase == IntPtr.Zero)
                throw new InvalidOperationException("В процессе не найден mono*.dll — это точно Unity/Mono игра и она уже загрузилась?");

            ReadExports(monoBase);
            foreach (var n in Needed)
                if (!_exports.ContainsKey(n))
                    throw new InvalidOperationException("В " + MonoModuleName + " нет экспорта " + n);
        }

        // ---------- поиск модуля ----------
        private IntPtr FindMonoModule()
        {
            var mods = new IntPtr[2048];
            if (!Native.EnumProcessModulesEx(_h, mods, mods.Length * IntPtr.Size, out int needed, Native.LIST_MODULES_ALL))
                throw new Win32Exception("EnumProcessModulesEx");
            int count = Math.Min(mods.Length, needed / IntPtr.Size);
            var sb = new StringBuilder(260);
            for (int i = 0; i < count; i++)
            {
                sb.Clear();
                Native.GetModuleBaseName(_h, mods[i], sb, sb.Capacity);
                string name = sb.ToString();
                if (name.StartsWith("mono", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    MonoModuleName = name;
                    return mods[i];
                }
            }
            return IntPtr.Zero;
        }

        // ---------- разбор таблицы экспорта PE в чужом процессе ----------
        private void ReadExports(IntPtr b)
        {
            int eLfanew = ReadInt(b + 0x3C);
            IntPtr nt = b + eLfanew;
            ushort magic = (ushort)ReadShort(nt + 0x18);
            int ddOffset = magic == 0x20B ? 0x88 : 0x78; // PE32+ : PE32
            int expRva = ReadInt(nt + ddOffset);
            if (expRva == 0) throw new InvalidOperationException("У mono-модуля нет таблицы экспорта");
            IntPtr exp = b + expRva;
            int numNames = ReadInt(exp + 0x18);
            IntPtr funcs = b + ReadInt(exp + 0x1C);
            IntPtr names = b + ReadInt(exp + 0x20);
            IntPtr ords = b + ReadInt(exp + 0x24);
            for (int i = 0; i < numNames; i++)
            {
                string name = ReadCString(b + ReadInt(names + i * 4));
                ushort ord = (ushort)ReadShort(ords + i * 2);
                int fRva = ReadInt(funcs + ord * 4);
                _exports[name] = b + fRva;
            }
        }

        // ---------- внедрение ----------
        public void Inject(byte[] assembly, string ns, string className, string methodName)
        {
            _rootDomain = Call("mono_get_root_domain", false);
            if (_rootDomain == IntPtr.Zero) throw new InvalidOperationException("mono_get_root_domain вернул NULL");
            Log("root domain: 0x" + _rootDomain.ToString("X"));

            IntPtr rawAsm = Alloc(assembly);
            IntPtr status = Alloc(new byte[8]);
            IntPtr image = Call("mono_image_open_from_data", true, rawAsm, (IntPtr)assembly.Length, (IntPtr)1, status);
            CheckStatus(status, "mono_image_open_from_data");
            if (image == IntPtr.Zero) throw new InvalidOperationException("mono_image_open_from_data вернул NULL");

            IntPtr asmName = Alloc(Encoding.UTF8.GetBytes("HFTrainer.Payload\0"));
            IntPtr asm = Call("mono_assembly_load_from_full", true, image, asmName, status, IntPtr.Zero);
            CheckStatus(status, "mono_assembly_load_from_full");
            if (asm == IntPtr.Zero) throw new InvalidOperationException("mono_assembly_load_from_full вернул NULL");

            IntPtr img = Call("mono_assembly_get_image", true, asm);
            IntPtr klass = Call("mono_class_from_name", true, img,
                Alloc(Encoding.UTF8.GetBytes(ns + "\0")), Alloc(Encoding.UTF8.GetBytes(className + "\0")));
            if (klass == IntPtr.Zero) throw new InvalidOperationException("Класс " + ns + "." + className + " не найден");

            IntPtr method = Call("mono_class_get_method_from_name", true, klass,
                Alloc(Encoding.UTF8.GetBytes(methodName + "\0")), IntPtr.Zero);
            if (method == IntPtr.Zero) throw new InvalidOperationException("Метод " + methodName + " не найден");

            IntPtr exc = Alloc(new byte[8]);
            Call("mono_runtime_invoke", true, method, IntPtr.Zero, IntPtr.Zero, exc);
            long excPtr = ReadLong(exc);
            if (excPtr != 0) throw new InvalidOperationException("Метод Load бросил исключение — см. HFTrainer.log в папке игры.");
            Log("Load() выполнен успешно");
        }

        private void CheckStatus(IntPtr status, string fn)
        {
            int s = ReadInt(status);
            if (s != 0) throw new InvalidOperationException(fn + ": статус " + s + " (0=OK, 1=ошибка, 2=не найдено, 3=неверная сборка)");
        }

        /// <summary>Вызов функции mono в отдельном удалённом потоке (x64 shellcode).</summary>
        private IntPtr Call(string export, bool attach, params IntPtr[] args)
        {
            if (args.Length > 4) throw new ArgumentException("max 4 args");
            IntPtr fn = _exports[export];
            IntPtr ret = Alloc(new byte[8]);

            var c = new List<byte>();
            c.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });                     // sub rsp,28h
            if (attach)
            {
                c.AddRange(new byte[] { 0x48, 0xB9 }); c.AddRange(BitConverter.GetBytes(_rootDomain.ToInt64())); // mov rcx, domain
                c.AddRange(new byte[] { 0x48, 0xB8 }); c.AddRange(BitConverter.GetBytes(_exports["mono_thread_attach"].ToInt64()));
                c.AddRange(new byte[] { 0xFF, 0xD0 });                             // call rax
            }
            byte[][] movs = { new byte[] { 0x48, 0xB9 }, new byte[] { 0x48, 0xBA }, new byte[] { 0x49, 0xB8 }, new byte[] { 0x49, 0xB9 } };
            for (int i = 0; i < args.Length; i++)
            {
                c.AddRange(movs[i]); c.AddRange(BitConverter.GetBytes(args[i].ToInt64()));
            }
            c.AddRange(new byte[] { 0x48, 0xB8 }); c.AddRange(BitConverter.GetBytes(fn.ToInt64()));   // mov rax, fn
            c.AddRange(new byte[] { 0xFF, 0xD0 });                                                     // call rax
            c.AddRange(new byte[] { 0x48, 0xA3 }); c.AddRange(BitConverter.GetBytes(ret.ToInt64()));  // mov [ret], rax
            c.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });                                         // add rsp,28h
            c.Add(0xC3);                                                                               // ret

            IntPtr code = Alloc(c.ToArray(), Native.PAGE_EXECUTE_READWRITE);
            IntPtr th = Native.CreateRemoteThread(_h, IntPtr.Zero, UIntPtr.Zero, code, IntPtr.Zero, 0, out _);
            if (th == IntPtr.Zero) throw new Win32Exception("CreateRemoteThread (" + export + ")");
            Native.WaitForSingleObject(th, 30000);
            Native.CloseHandle(th);
            return (IntPtr)ReadLong(ret);
        }

        // ---------- память ----------
        private IntPtr Alloc(byte[] data, uint protect = Native.PAGE_READWRITE)
        {
            IntPtr p = Native.VirtualAllocEx(_h, IntPtr.Zero, (UIntPtr)(uint)Math.Max(data.Length, 8),
                Native.MEM_COMMIT | Native.MEM_RESERVE, protect);
            if (p == IntPtr.Zero) throw new Win32Exception("VirtualAllocEx");
            _allocs.Add(p);
            if (!Native.WriteProcessMemory(_h, p, data, (IntPtr)data.Length, out _)) throw new Win32Exception("WriteProcessMemory");
            return p;
        }

        private byte[] Read(IntPtr addr, int n)
        {
            var buf = new byte[n];
            if (!Native.ReadProcessMemory(_h, addr, buf, (IntPtr)n, out _)) throw new Win32Exception("ReadProcessMemory 0x" + addr.ToString("X"));
            return buf;
        }
        private int ReadInt(IntPtr a) => BitConverter.ToInt32(Read(a, 4), 0);
        private short ReadShort(IntPtr a) => BitConverter.ToInt16(Read(a, 2), 0);
        private long ReadLong(IntPtr a) => BitConverter.ToInt64(Read(a, 8), 0);
        private string ReadCString(IntPtr a)
        {
            var ms = new MemoryStream();
            for (int off = 0; off < 512; off += 64)
            {
                byte[] chunk;
                try { chunk = Read(a + off, 64); } catch { break; }
                int z = Array.IndexOf(chunk, (byte)0);
                if (z >= 0) { ms.Write(chunk, 0, z); break; }
                ms.Write(chunk, 0, chunk.Length);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        public void Dispose()
        {
            // Сборку (image) mono копирует (need_copy=1), поэтому временные буферы можно освободить.
            foreach (var p in _allocs) Native.VirtualFreeEx(_h, p, UIntPtr.Zero, Native.MEM_RELEASE);
            _allocs.Clear();
            Native.CloseHandle(_h);
        }
    }
}
