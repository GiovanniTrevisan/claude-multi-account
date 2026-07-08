// ClaudeWork.exe — inicia uma instancia isolada do Claude Desktop ("Claude Work")
// e lhe da identidade propria na barra de tarefas do Windows, SEM tocar em
// nenhum arquivo da instalacao oficial (que e um pacote MSIX protegido).
//
// Como funciona:
//  1. Inicia claude.exe com --user-data-dir/--disk-cache-dir proprios
//     (perfil, login, cookies, cache totalmente isolados).
//  2. Localiza a(s) janela(s) dessa instancia (identificada pelo perfil na
//     linha de comando) e carimba nelas, via API oficial do Shell:
//       - PKEY_AppUserModel_ID           -> grupo/pin proprio na barra
//       - PKEY_AppUserModel_RelaunchCommand / DisplayName / Icon -> pin correto
//       - WM_SETICON                     -> icone proprio na barra
//  3. Fica residente enquanto a instancia Work existir, recarimbando novas
//     janelas (o Electron pode recriar/abrir janelas), e sai quando ela fecha.
//
// O launcher e single-instance por perfil: clicar de novo apenas foca/reabre
// o Claude Work; o launcher residente continua cuidando da identidade.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class ClaudeWork
{
    // ---- Configuracao (pode ser sobrescrita por variaveis de ambiente) ----
    private static readonly string ProfileName =
        Environment.GetEnvironmentVariable("CLAUDE_WORK_PROFILE") ?? "Claude-Work";
    private static readonly string Aumid =
        Environment.GetEnvironmentVariable("CLAUDE_WORK_AUMID") ?? "ClaudeWorkLauncher.ClaudeWork";
    private static readonly string ProductName =
        Environment.GetEnvironmentVariable("CLAUDE_WORK_NAME") ?? "Claude Work";

    private static string LocalAppData
    { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
    private static string UserDataDir { get { return Path.Combine(LocalAppData, ProfileName); } }
    private static string CacheDir { get { return Path.Combine(LocalAppData, ProfileName + "-Cache"); } }

    private static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }
    private static string AppDir { get { return Path.GetDirectoryName(ExePath); } }
    private static string IconPath { get { return Path.Combine(AppDir, "claude-work.ico"); } }
    private static string ClaudePathCache { get { return Path.Combine(UserDataDir, ".claude-exe-path"); } }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(UserDataDir);
            Directory.CreateDirectory(CacheDir);
        }
        catch { /* ignore */ }

        // Opcional: apenas (re)criar atalhos e sair.
        if (Array.IndexOf(args, "--install-shortcuts") >= 0)
        {
            RegisterAumidForNotifications();
            CreateShortcut(StartMenuPath());
            CreateShortcut(DesktopPath());
            return 0;
        }

        string claudeExe = ResolveClaudeExe();
        if (claudeExe == null)
        {
            MessageBox(
                "Claude Desktop nao foi encontrado.\n\n" +
                    "Instale o Claude pela Microsoft Store e tente novamente.",
                ProductName);
            return 1;
        }

        RegisterAumidForNotifications();

        // Sempre (re)inicia/foca a instancia Work. Se ja estiver rodando, o
        // lock por-perfil do Electron apenas foca a janela existente.
        LaunchClaude(claudeExe);

        // Single-instance do proprio launcher (por perfil). Se ja ha um
        // launcher residente, so precisavamos disparar o foco acima -> sair.
        bool isOwner;
        using (var mutex = new Mutex(true, "ClaudeWorkLauncher_" + Sanitize(ProfileName), out isOwner))
        {
            if (!isOwner)
                return 0;

            RunStamperLoop();
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // Loop residente: carimba janelas da instancia Work ate ela encerrar.
    // ------------------------------------------------------------------
    private static void RunStamperLoop()
    {
        IntPtr hIconBig = LoadIconFromFile(IconPath, GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON));
        IntPtr hIconSmall = LoadIconFromFile(IconPath, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON));

        var stamped = new HashSet<IntPtr>();
        var idleChecks = 0;
        // Espera inicial ate a instancia aparecer (cold start pode demorar).
        var startupDeadline = DateTime.UtcNow.AddSeconds(45);
        bool everSeen = false;

        while (true)
        {
            var mainPids = GetWorkMainPids();
            if (mainPids.Count > 0)
                everSeen = true;

            if (everSeen && mainPids.Count == 0)
            {
                // A instancia Work encerrou -> encerra o launcher tambem.
                idleChecks++;
                if (idleChecks >= 3)
                    break;
            }
            else
            {
                idleChecks = 0;
            }

            if (!everSeen && DateTime.UtcNow > startupDeadline)
                break; // nunca subiu; desiste.

            var allPidsUsingProfile = GetPidsUsingProfile();
            foreach (var win in FindClaudeWindows())
            {
                if (!allPidsUsingProfile.Contains(win.Pid))
                    continue;

                bool firstTime = !stamped.Contains(win.Hwnd);
                StampWindow(win.Hwnd, hIconBig, hIconSmall, firstTime);
                stamped.Add(win.Hwnd);
            }

            Thread.Sleep(1000);
        }
    }

    // ------------------------------------------------------------------
    // Carimba uma janela: AUMID + propriedades de relaunch + icone.
    // ------------------------------------------------------------------
    private static void StampWindow(IntPtr hwnd, IntPtr hIconBig, IntPtr hIconSmall, bool setProps)
    {
        if (setProps)
        {
            IPropertyStore store;
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) == 0 && store != null)
            {
                try
                {
                    SetString(store, PKEY_AppUserModel_ID, Aumid);
                    SetString(store, PKEY_AppUserModel_RelaunchCommand, "\"" + ExePath + "\"");
                    SetString(store, PKEY_AppUserModel_RelaunchDisplayNameResource, ProductName);
                    if (File.Exists(IconPath))
                        SetString(store, PKEY_AppUserModel_RelaunchIconResource, IconPath + ",0");
                    store.Commit();
                }
                catch { /* ignore */ }
                finally { Marshal.ReleaseComObject(store); }
            }
        }

        // O icone e reaplicado sempre: o Electron pode reassumir o proprio
        // icone ao redesenhar; o loop residente corrige a cada segundo.
        if (hIconBig != IntPtr.Zero)
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hIconBig);
        if (hIconSmall != IntPtr.Zero)
            SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hIconSmall);
    }

    // ------------------------------------------------------------------
    // Descoberta de processos/janelas da instancia Work.
    // ------------------------------------------------------------------
    private struct WinInfo { public IntPtr Hwnd; public uint Pid; }

    private static List<WinInfo> FindClaudeWindows()
    {
        var list = new List<WinInfo>();
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString() != "Claude") return true; // janelas principais tem titulo "Claude"
            uint pid; GetWindowThreadProcessId(h, out pid);
            list.Add(new WinInfo { Hwnd = h, Pid = pid });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// PIDs (de qualquer processo claude.exe) cuja linha de comando referencia
    /// o nosso perfil. Usado para saber quais janelas carimbar.
    private static HashSet<uint> GetPidsUsingProfile()
    {
        var set = new HashSet<uint>();
        var needle = UserDataDir;
        foreach (var kv in QueryClaudeCommandLines())
        {
            if (kv.Value != null &&
                kv.Value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                set.Add(kv.Key);
        }
        return set;
    }

    /// PIDs dos processos PRINCIPAIS (sem --type=) do perfil Work — usados para
    /// detectar quando a instancia encerrou.
    private static List<uint> GetWorkMainPids()
    {
        var list = new List<uint>();
        foreach (var kv in QueryClaudeCommandLines())
        {
            var cl = kv.Value;
            if (cl == null) continue;
            if (cl.IndexOf(UserDataDir, StringComparison.OrdinalIgnoreCase) >= 0 &&
                cl.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) < 0)
                list.Add(kv.Key);
        }
        return list;
    }

    private static Dictionary<uint, string> QueryClaudeCommandLines()
    {
        var dict = new Dictionary<uint, string>();
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'claude.exe'"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    var pid = (uint)mo["ProcessId"];
                    dict[pid] = mo["CommandLine"] as string;
                }
            }
        }
        catch { /* WMI indisponivel: retorna o que tiver */ }
        return dict;
    }

    // ------------------------------------------------------------------
    // Inicializacao / localizacao do Claude.
    // ------------------------------------------------------------------
    private static void LaunchClaude(string claudeExe)
    {
        try
        {
            var psi = new ProcessStartInfo(claudeExe,
                "--user-data-dir=\"" + UserDataDir + "\" --disk-cache-dir=\"" + CacheDir + "\"")
            { UseShellExecute = false };
            Process.Start(psi);
        }
        catch (Exception e)
        {
            MessageBox("Falha ao iniciar o Claude:\n" + e.Message, ProductName);
        }
    }

    /// Resolve o caminho do claude.exe do pacote MSIX. Faz cache no perfil e
    /// re-resolve automaticamente se o caminho mudar (ex.: apos atualizacao do
    /// Claude, cuja versao entra no caminho de instalacao).
    private static string ResolveClaudeExe()
    {
        try
        {
            if (File.Exists(ClaudePathCache))
            {
                var cached = File.ReadAllText(ClaudePathCache).Trim();
                if (cached.Length > 0 && File.Exists(cached))
                    return cached;
            }
        }
        catch { }

        string resolved = ResolveClaudeExeViaPowerShell();
        if (resolved != null)
        {
            try { File.WriteAllText(ClaudePathCache, resolved); } catch { }
        }
        return resolved;
    }

    private static string ResolveClaudeExeViaPowerShell()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command " +
                "\"$p=Get-AppxPackage -Name Claude | Select-Object -First 1; " +
                "if($p){Join-Path $p.InstallLocation 'app\\claude.exe'}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(15000);
                if (outp.Length > 0 && File.Exists(outp))
                    return outp;
            }
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------
    // Registro do AUMID (notificacoes/toast) e atalhos.
    // ------------------------------------------------------------------
    private static void RegisterAumidForNotifications()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\AppUserModelId\" + Aumid))
            {
                key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
                if (File.Exists(IconPath))
                    key.SetValue("IconUri", IconPath, RegistryValueKind.String);
            }
        }
        catch { }
    }

    private static string StartMenuPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs", ProductName + ".lnk");
    }

    private static string DesktopPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProductName + ".lnk");
    }

    /// Cria um .lnk apontando para este launcher, com icone proprio e o AUMID
    /// gravado na property store do atalho (para o pin herdar a identidade).
    private static void CreateShortcut(string lnkPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(ExePath);
            link.SetWorkingDirectory(AppDir);
            link.SetDescription(ProductName);
            if (File.Exists(IconPath))
                link.SetIconLocation(IconPath, 0);

            var store = (IPropertyStore)link;
            SetString(store, PKEY_AppUserModel_ID, Aumid);
            store.Commit();

            ((IPersistFile)link).Save(lnkPath, true);
            Marshal.ReleaseComObject(link);
        }
        catch (Exception e)
        {
            MessageBox("Nao foi possivel criar o atalho:\n" + lnkPath + "\n" + e.Message, ProductName);
        }
    }

    // ------------------------------------------------------------------
    // Helpers de PROPVARIANT / icone.
    // ------------------------------------------------------------------
    private static void SetString(IPropertyStore store, PROPERTYKEY key, string value)
    {
        var pv = new PROPVARIANT { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni(value) };
        try
        {
            store.SetValue(ref key, ref pv);
        }
        finally
        {
            PropVariantClear(ref pv); // libera p (VT_LPWSTR)
        }
    }

    private static IntPtr LoadIconFromFile(string path, int cx, int cy)
    {
        if (!File.Exists(path)) return IntPtr.Zero;
        return LoadImage(IntPtr.Zero, path, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
    }

    private static void MessageBox(string text, string caption)
    {
        MessageBoxW(IntPtr.Zero, text, caption, 0x40 /*MB_ICONINFORMATION*/);
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    // ==================================================================
    // P/Invoke
    // ==================================================================
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0, ICON_BIG = 1;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXICON = 11, SM_CYICON = 12, SM_CXSMICON = 49, SM_CYSMICON = 50;
    private const ushort VT_LPWSTR = 31;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr h, string t, string c, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint load);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore pps);

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint c);
        void GetAt(uint i, out PROPERTYKEY k);
        void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        void Commit();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int cch, IntPtr pfd, uint flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short w);
        void SetHotkey(short w);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int cch, out int i);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    private static Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private static readonly Guid FMTID_AppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID =
        new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 5 };
    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchCommand =
        new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 2 };
    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchIconResource =
        new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 3 };
    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchDisplayNameResource =
        new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 4 };
}
