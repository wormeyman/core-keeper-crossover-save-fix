// Replays the steps Core Keeper uses to save a file, in an empty folder, and
// after each step asks four ways whether each file exists:
//   attr    GetFileAttributesExW, which File.Exists uses on Windows
//   find    FindFirstFileW on the exact name
//   list    FindFirstFileW on "<folder>\*", then look for the name
//   exists  File.Exists
// On Windows all four always agree, and every step succeeds. A line marked
// MISMATCH or FAILED is the kind of problem the mod works around.
// Exit code: 0 if all checks agreed and all steps worked, 1 if not, 2 on bad usage.
// Usage: WineFileCheck.exe <folder>   (created if missing; must be empty)
//        WineFileCheck.exe --probe <path>...   (only asks; creates and changes nothing)
using System.Runtime.InteropServices;
using System.Text;

var problems = 0;
var root = "";

if (args.Length >= 2 && args[0] == "--probe")
{
    Console.WriteLine($"wine:   {Native.WineVersion() ?? "not running under Wine"}");
    foreach (var path in args.Skip(1))
    {
        // Paths are passed through exactly as given, slashes and all.
        Check("probe", path);
    }
    return problems == 0 ? 0 : 1;
}
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: WineFileCheck <empty folder> | --probe <path>...");
    return 2;
}
root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
if (Directory.EnumerateFileSystemEntries(root).Any())
{
    Console.Error.WriteLine($"{root} is not empty");
    return 2;
}

Console.WriteLine($"wine:   {Native.WineVersion() ?? "not running under Wine"}");
Console.WriteLine($"folder: {root}");
string P(string name) => Path.Combine(root, name);

Section("never existed (control)");
Check("start", P("never.dat"));
// The game's paths mix separators, like <save folder>\<id>/worlds/9.world.gzip,
// and its save folder name has a space ("Core Keeper").
Directory.CreateDirectory(P("Core Keeper"));
Check("space in folder", P(Path.Combine("Core Keeper", "never.dat.pugtmp")));
Check("forward slash", root + "/never.dat.pugtmp");
Check("space + slash", P("Core Keeper") + "/never.dat.pugtmp");

Section("create, then DeleteFileW");
var a = P("a.dat");
File.WriteAllBytes(a, [1]);
Check("after create", a);
Act("DeleteFileW(a.dat)", Native.DeleteFileW(a));
Check("right after delete", a);
foreach (var ms in new[] { 10, 100, 1000 })
{
    Thread.Sleep(ms);
    Check($"after {ms} ms more", a);
}

Section("MoveFileExW onto a missing file");
var b = P("b.dat");
File.WriteAllBytes(b + ".pugtmp", [1]);
Act("MoveFileExW(b.dat.pugtmp, b.dat)", Native.MoveFileExW(b + ".pugtmp", b, 0));
Check("after move", b, b + ".pugtmp");

Section("ReplaceFileW over an existing file");
var c = P("c.dat");
File.WriteAllBytes(c, [1]);
File.WriteAllBytes(c + ".pugtmp", [2, 2]);
Act("ReplaceFileW(c.dat, c.dat.pugtmp, c.dat.pugbackup)",
    Native.ReplaceFileW(c, c + ".pugtmp", c + ".pugbackup", 0, IntPtr.Zero, IntPtr.Zero));
Check("after replace", c, c + ".pugtmp", c + ".pugbackup");

Section("the game's save routine, saving a new file 3 times");
var save = P(Path.Combine("saves", "1.json"));
Directory.CreateDirectory(Path.GetDirectoryName(save)!);
for (var round = 1; round <= 3; round++)
{
    GameWrite(save, Encoding.UTF8.GetBytes($"save {round}"));
    Check($"after save {round}", save, save + ".pugbackup", save + ".pugtmp");
    Console.WriteLine($"  1.json holds: {(File.Exists(save) ? Quote(File.ReadAllText(save)) : "(missing)")}");
}
Thread.Sleep(1000);
Check("1 s after last save", save, save + ".pugbackup", save + ".pugtmp");

Section("the same, with a game-style path: space in a folder name, then a forward slash");
var gameSave = P("Core Keeper") + "/saves/1.json";
Directory.CreateDirectory(P(Path.Combine("Core Keeper", "saves")));
for (var round = 1; round <= 3; round++)
{
    GameWrite(gameSave, Encoding.UTF8.GetBytes($"save {round}"));
    Check($"after save {round}", gameSave, gameSave + ".pugbackup", gameSave + ".pugtmp");
}

// Unity's Mono does not call GetLastError after a P/Invoke. Its JIT reads the
// error straight from the thread's TEB through the gs register, at gs:[0x68]
// (mono/mini/mini-amd64.c, emit_get_last_error). Do the same, on this thread
// and a new one, and compare with the TEB itself and with GetLastError.
Section("the error code read the way Unity's Mono reads it (gs:[0x68])");
LastErrorCheck("main thread");
var worker = new Thread(() => LastErrorCheck("new thread"));
worker.Start();
worker.Join();

Console.WriteLine();
Console.WriteLine(problems == 0 ? "RESULT: all checks agreed, all steps worked" : $"RESULT: {problems} problem(s)");
return problems == 0 ? 0 : 1;

// Same steps, in the same order, as Pug.Platform.StandaloneFilesystem.Write
// (see "How the game saves a file" in README.md).
void GameWrite(string file, byte[] data)
{
    var tmp = file + ".pugtmp";
    var backup = file + ".pugbackup";
    var backupSaid = File.Exists(backup);
    Console.WriteLine($"  File.Exists(.pugbackup) = {backupSaid}");
    if (backupSaid)
    {
        Try("File.Delete(.pugbackup)", () => File.Delete(backup));
    }
    Try("File.WriteAllBytes(.pugtmp)", () => File.WriteAllBytes(tmp, data));
    var fileSaid = File.Exists(file);
    Console.WriteLine($"  File.Exists(file) = {fileSaid}");
    if (fileSaid)
    {
        Try("File.Replace(.pugtmp, file, .pugbackup)", () => File.Replace(tmp, file, backup));
    }
    else
    {
        Try("File.Move(.pugtmp, file)", () => File.Move(tmp, file));
    }
}

void LastErrorCheck(string label)
{
    var teb = Native.TebAddress();
    Native.GetFileAttributesExNoSave(P("never.dat"), 0, out _);
    var viaGs = Native.ReadGsLastError();
    var inTeb = Marshal.ReadInt32(teb, 0x68);
    var viaApi = Native.GetLastError();
    var gsTeb = Native.ReadGsSelf();
    var agree = viaGs == inTeb && inTeb == viaApi;
    Console.WriteLine($"  {label,-12} missing file: gs:[0x68]={viaGs} teb+0x68={inTeb} GetLastError()={viaApi}" +
        (agree ? "" : "  <-- MISMATCH"));
    Console.WriteLine($"  {label,-12} teb=0x{teb:x} gs:[0x30]=0x{gsTeb:x}{(gsTeb == teb ? "" : " (gs does not point at the TEB)")}");
    if (!agree) problems++;
}

void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"== {title}");
}

void Act(string what, bool ok)
{
    var error = Marshal.GetLastPInvokeError();
    Console.WriteLine(ok ? $"  {what}: ok" : $"  {what}: FAILED, error {error}");
    if (!ok) problems++;
}

void Try(string what, Action action)
{
    try
    {
        action();
        Console.WriteLine($"  {what}: ok");
    }
    catch (Exception e)
    {
        Console.WriteLine($"  {what}: FAILED, {e.GetType().Name} 0x{e.HResult:x8}: {e.Message}");
        problems++;
    }
}

void Check(string label, params string[] paths)
{
    foreach (var path in paths)
    {
        var attr = Native.Attr(path);
        var find = Native.Find(path);
        var list = Native.Listed(path);
        var exists = File.Exists(path) ? "yes" : "no";
        var agree = Word(attr) == Word(find) && Word(find) == Word(list) && Word(list) == exists;
        var name = root == "" ? path : Path.GetRelativePath(root, path);
        Console.WriteLine($"  {label,-20} {name,-22} attr={attr,-9} find={find,-9} list={list,-9} exists={exists}" +
            (agree ? "" : "  <-- MISMATCH"));
        if (!agree) problems++;
    }
}

static string Word(string answer) => answer.StartsWith("yes") ? "yes" : "no";
static string Quote(string s) => $"\"{s}\"";

static class Native
{
    const uint InvalidAttributes = 0xFFFFFFFF;
    static readonly IntPtr InvalidHandle = new(-1);

    // "yes(<size>b)" or "no(<error>)".
    public static string Attr(string path) =>
        GetFileAttributesExW(path, 0, out var data) && data.Attributes != InvalidAttributes
            ? $"yes({((ulong)data.SizeHigh << 32) | data.SizeLow}b)"
            : $"no({Marshal.GetLastPInvokeError()})";

    public static string Find(string path)
    {
        var handle = FindFirstFileW(path, out var data);
        if (handle == InvalidHandle)
        {
            return $"no({Marshal.GetLastPInvokeError()})";
        }
        FindClose(handle);
        return $"yes({((ulong)data.SizeHigh << 32) | data.SizeLow}b)";
    }

    public static string Listed(string path)
    {
        var name = Path.GetFileName(path);
        var handle = FindFirstFileW(Path.Combine(Path.GetDirectoryName(path)!, "*"), out var data);
        if (handle == InvalidHandle)
        {
            return $"no({Marshal.GetLastPInvokeError()})";
        }
        try
        {
            do
            {
                if (string.Equals(data.FileName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return $"yes({((ulong)data.SizeHigh << 32) | data.SizeLow}b)";
                }
            } while (FindNextFileW(handle, out data));
            return "no";
        }
        finally
        {
            FindClose(handle);
        }
    }

    // Tiny machine-code stubs, so the reads happen exactly as JIT code does them.
    static readonly IntPtr gsLastError = Stub(0x65, 0x8B, 0x04, 0x25, 0x68, 0x00, 0x00, 0x00, 0xC3);    // mov eax, gs:[0x68]; ret
    static readonly IntPtr gsSelf = Stub(0x65, 0x48, 0x8B, 0x04, 0x25, 0x30, 0x00, 0x00, 0x00, 0xC3); // mov rax, gs:[0x30]; ret

    public static unsafe int ReadGsLastError() => ((delegate* unmanaged<int>)gsLastError)();
    public static unsafe IntPtr ReadGsSelf() => ((delegate* unmanaged<IntPtr>)gsSelf)();

    static IntPtr Stub(params byte[] code)
    {
        var memory = VirtualAlloc(IntPtr.Zero, (UIntPtr)4096, 0x3000 /* commit + reserve */, 0x40 /* rwx */);
        Marshal.Copy(code, 0, memory, code.Length);
        return memory;
    }

    // The TEB address from the kernel side, without going through gs.
    public static IntPtr TebAddress()
    {
        var info = new ThreadBasicInformation();
        var status = NtQueryInformationThread(new IntPtr(-2) /* current thread */, 0, ref info,
            Marshal.SizeOf<ThreadBasicInformation>(), out _);
        if (status != 0) throw new InvalidOperationException($"NtQueryInformationThread failed: 0x{status:x8}");
        return info.TebBaseAddress;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ThreadBasicInformation
    {
        public int ExitStatus;
        public IntPtr TebBaseAddress;
        public IntPtr UniqueProcess, UniqueThread, AffinityMask;
        public int Priority, BasePriority;
    }

    // "wine-<version> on <host> <release>", or null when not under Wine.
    public static unsafe string? WineVersion()
    {
        var ntdll = GetModuleHandleW("ntdll.dll");
        var get = GetProcAddress(ntdll, "wine_get_version");
        if (get == IntPtr.Zero)
        {
            return null;
        }
        var version = "wine-" + Marshal.PtrToStringAnsi(((delegate* unmanaged<IntPtr>)get)());
        var host = GetProcAddress(ntdll, "wine_get_host_version");
        if (host != IntPtr.Zero)
        {
            IntPtr sysname, release;
            ((delegate* unmanaged<IntPtr*, IntPtr*, void>)host)(&sysname, &release);
            version += $" on {Marshal.PtrToStringAnsi(sysname)} {Marshal.PtrToStringAnsi(release)}";
        }
        return version;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FileAttributeData
    {
        public uint Attributes;
        public ulong Created, Accessed, Written;
        public uint SizeHigh, SizeLow;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    public struct FindData
    {
        public uint Attributes;
        public ulong Created, Accessed, Written;
        public uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetFileAttributesExW(string name, int level, out FileAttributeData data);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr FindFirstFileW(string name, out FindData data);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextFileW(IntPtr handle, out FindData data);
    [DllImport("kernel32")]
    static extern bool FindClose(IntPtr handle);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool DeleteFileW(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool MoveFileExW(string from, string to, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ReplaceFileW(string replaced, string replacement, string? backup, uint flags,
        IntPtr exclude, IntPtr reserved);
    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetFileAttributesExW")]
    public static extern bool GetFileAttributesExNoSave(string name, int level, out FileAttributeData data);
    [DllImport("kernel32")]
    public static extern int GetLastError();
    [DllImport("kernel32")]
    static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("ntdll")]
    static extern int NtQueryInformationThread(IntPtr thread, int infoClass, ref ThreadBasicInformation info,
        int length, out int returned);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
}
