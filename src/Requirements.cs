using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Win32;

namespace ADUserManager
{
    public enum CheckState { Ok, Warn, Fail, Info }

    public class RequirementCheck
    {
        public string Group = "", Name = "", Detail = "", Fix = "";
        public CheckState State;
    }

    /// <summary>
    /// Checks what the app needs to run on this PC / domain, and the things in the config file
    /// that differ between sites (servers, shares, groups, OUs, UPN suffix). Used by the
    /// 'Scope &amp; settings' page - especially useful after moving the app to another domain.
    /// </summary>
    static class Requirements
    {
        public const int Net48Release = 528040;
        public const string DotNetUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
        public const string RsatCapability = "Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0";

        public const string Instructions =
            "WHAT THE APP NEEDS\r\n" +
            "  • Windows 10 / 11 (or Windows Server 2016+) joined to the domain.\r\n" +
            "  • .NET Framework 4.8 - built into Windows 10 1903+ and Windows 11.\r\n" +
            "    Older PCs: install it from Windows Update or the 'Install .NET 4.8' button.\r\n" +
            "  • Signed in to Windows with a domain account that has the AD rights\r\n" +
            "    to reset passwords / create users in the OUs you manage.\r\n" +
            "  • Network access to a domain controller (LDAP 389, Kerberos 88) and\r\n" +
            "    to the file server(s) for home folders (SMB 445).\r\n" +
            "  • Write access to the app's folder for the config file and audit log\r\n" +
            "    (otherwise the log goes to %LOCALAPPDATA%\\ADUserManager).\r\n" +
            "  • No installer, RSAT, PowerShell modules or admin rights are needed.\r\n" +
            "    RSAT is only for the older 'Change or Reset PW.ps1' script.\r\n" +
            "\r\n" +
            "GIVING A DELEGATED ADMIN PERMISSION (no Domain Admin needed)\r\n" +
            "  1. Create a group, e.g. Delegated-UserAdmins, and add the user to it.\r\n" +
            "  2. In Active Directory Users and Computers, right-click the target OU\r\n" +
            "     and choose 'Delegate Control...'.\r\n" +
            "  3. Add Delegated-UserAdmins.\r\n" +
            "  4. Tick these two common tasks:\r\n" +
            "       - Create, delete, and manage user accounts\r\n" +
            "       - Reset user passwords and force password change at next logon\r\n" +
            "  5. Click Finish.\r\n" +
            "\r\n" +
            "MOVING THE APP TO ANOTHER DOMAIN / SITE\r\n" +
            "  1. Copy ADUserManager.exe only (not the old .ini) to the new site and run it.\r\n" +
            "     A fresh " + AppConfig.FileName + " is created from the template.\r\n" +
            "  2. Click 'Check requirements' and fix anything marked ✖.\r\n" +
            "  3. Open the config file and set: OrganisationName, the OUs to manage\r\n" +
            "     ([ResetPasswordOUs] / [CreateUserOUs]), UpnSuffix (must be listed\r\n" +
            "     under 'UPN suffixes in this forest'), and for each [UserType:...]\r\n" +
            "     the HomePath, DefaultOU and Groups for that site.\r\n" +
            "     'Found on existing users in scope' (left) shows the home / profile\r\n" +
            "     paths already in use - copy them into HomePath= / ProfilePath=.\r\n" +
            "  4. Click 'Reload config', then 'Check requirements' again - every group,\r\n" +
            "     OU and share named in the config is checked against the new domain.\r\n" +
            "  5. Create a test user, check it, then delete it.";

        // ------------------------------------------------------------------
        // Individual probes
        // ------------------------------------------------------------------

        public static int DotNetRelease()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                                  .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    object v = k != null ? k.GetValue("Release") : null;
                    return v != null ? Convert.ToInt32(v) : 0;
                }
            }
            catch { return 0; }
        }

        public static string DotNetName(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= 528040) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 461308) return "4.7.1";
            if (release >= 460798) return "4.7";
            if (release >= 394802) return "4.6.2";
            if (release >= 394254) return "4.6.1";
            if (release >= 393295) return "4.6";
            if (release >= 379893) return "4.5.2";
            if (release > 0) return "4.5 or older";
            return "not found";
        }

        static string WindowsName()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                                  .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string name = Convert.ToString(k.GetValue("ProductName"));
                        string build = Convert.ToString(k.GetValue("CurrentBuildNumber"));
                        string disp = Convert.ToString(k.GetValue("DisplayVersion"));
                        int b;
                        // Windows 11 still reports "Windows 10" in ProductName.
                        if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        return (name + " " + disp + " (build " + build + ")").Replace("  ", " ").Trim();
                    }
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        public static bool IsServerOs()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                                  .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    return k != null && string.Equals(Convert.ToString(k.GetValue("InstallationType")), "Server", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool RsatAdModuleInstalled()
        {
            string sys = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.SystemDirectory;
            return Directory.Exists(Path.Combine(sys, @"WindowsPowerShell\v1.0\Modules\ActiveDirectory"));
        }

        static bool PortOpen(string host, int port, int timeoutMs = 2000)
        {
            if (string.IsNullOrEmpty(host)) return false;
            try
            {
                using (var c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        static bool FolderWritable(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".admgr-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(f, "x");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        /// <summary>\\server\share\a\&lt;username&gt; -> \\server\share\a (the folder that must already exist).</summary>
        static string ParentOf(string expandedPath)
        {
            string p = (expandedPath ?? "").TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            return i > 2 ? p.Substring(0, i) : p;
        }

        // ------------------------------------------------------------------
        // Full run
        // ------------------------------------------------------------------

        public static List<RequirementCheck> Run(AppConfig cfg, AdService ad, string appDir)
        {
            var list = new List<RequirementCheck>();
            Action<string, CheckState, string, string, string> add = delegate (string g, CheckState s, string n, string d, string fix)
            {
                list.Add(new RequirementCheck { Group = g, State = s, Name = n, Detail = d ?? "", Fix = fix ?? "" });
            };

            // ---- This PC ------------------------------------------------------
            const string pc = "This PC";
            add(pc, CheckState.Info, "Windows", WindowsName() + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"), null);

            int rel = DotNetRelease();
            add(pc, rel >= Net48Release ? CheckState.Ok : (rel > 0 ? CheckState.Warn : CheckState.Fail),
                ".NET Framework 4.8", "installed: " + DotNetName(rel),
                rel >= Net48Release ? null : "Install .NET Framework 4.8 ('Install .NET 4.8' button above, or Windows Update), then restart the PC.");

            string joined = null;
            try { joined = System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain().Name; } catch { }
            add(pc, joined != null ? CheckState.Ok : CheckState.Fail, "PC joined to a domain",
                joined ?? "not joined (or the domain cannot be reached)",
                joined != null ? null : "Run the app on a domain-joined PC, or set Domain= / DomainController= in the config file.");

            add(pc, FolderWritable(appDir) ? CheckState.Ok : CheckState.Warn, "App folder writable", appDir,
                "The config file cannot be saved here and the audit log goes to %LOCALAPPDATA%\\ADUserManager. " +
                "Give the operators Modify rights on the folder, or run the app from a writable folder.");

            bool rsat = RsatAdModuleInstalled();
            add(pc, rsat ? CheckState.Ok : CheckState.Info, "RSAT AD PowerShell module (optional)",
                rsat ? "installed" : "not installed - NOT needed by this app",
                rsat ? null : "Only needed for the older 'Change or Reset PW.ps1' script. Use 'Install RSAT (optional)' above (needs admin).");

            // ---- Active Directory --------------------------------------------
            const string dir = "Active Directory";
            if (!ad.Connected)
            {
                add(dir, CheckState.Fail, "Connected to Active Directory", "not connected",
                    "Sign in to Windows with a domain account, check the network, or set DomainController= in the config file.");
                return list;
            }
            add(dir, CheckState.Ok, "Connected to Active Directory", ad.DomainDns + " via " + ad.Dc + " as " + ad.LogonName, null);

            bool ldap = PortOpen(ad.Dc, 389), kerb = PortOpen(ad.Dc, 88);
            add(dir, ldap && kerb ? CheckState.Ok : CheckState.Warn, "Domain controller ports",
                "LDAP 389 " + (ldap ? "open" : "blocked") + ", Kerberos 88 " + (kerb ? "open" : "blocked"),
                "Allow TCP 389 and 88 from this PC to " + ad.Dc + " (firewall).");

            add(dir, ad.ResetRoots.Count > 0 ? CheckState.Ok : CheckState.Fail, "OUs you can search / reset",
                ad.ResetRoots.Count + " OU(s) - " + ad.ResetScopeSource,
                "List OUs under [ResetPasswordOUs], or ask a domain admin to delegate 'Reset password' on the staff/student OUs.");
            add(dir, ad.CreateOUs.Count > 0 ? CheckState.Ok : CheckState.Warn, "OUs you can create users in",
                ad.CreateOUs.Count + " OU(s) - " + ad.CreateScopeSource,
                "List OUs under [CreateUserOUs], or delegate 'Create user objects' on those OUs.");

            // ---- Config file --------------------------------------------------
            const string conf = "Config file (site-specific)";
            add(conf, cfg.Warnings.Count == 0 ? CheckState.Ok : CheckState.Warn, "Config file reads cleanly",
                cfg.Warnings.Count == 0 ? cfg.FilePath : cfg.Warnings.Count + " warning(s) - listed on the left",
                cfg.Warnings.Count == 0 ? null : "Fix the lines listed under WARNINGS on the left, then Reload config.");

            List<UserType> types = cfg.ActiveUserTypes;
            add(conf, types.Count > 0 ? CheckState.Ok : CheckState.Warn, "User types",
                types.Count > 0 ? string.Join(", ", types.Select(t => t.Name).ToArray()) : "none",
                "Add [UserType:Name] sections to the config file.");

            foreach (string s in cfg.MissingServerSettings())
                add(conf, CheckState.Fail, s + "=", "not set and not detected", "Set " + s + "= in [General].");

            // UPN suffixes
            if (ad.UpnSuffixesRead)
                add(conf, CheckState.Info, "UPN suffixes in this forest", string.Join(", ", ad.UpnSuffixes.ToArray()), null);
            var suffixUse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (UserType t in types.DefaultIfEmpty(null))
            {
                string s = cfg.UpnSuffixFor(t, ad.DomainDns);
                List<string> who;
                if (!suffixUse.TryGetValue(s, out who)) suffixUse[s] = who = new List<string>();
                who.Add(t != null ? t.Name : "all users");
            }
            foreach (var kv in suffixUse)
            {
                bool known = !ad.UpnSuffixesRead || ad.IsKnownUpnSuffix(kv.Key);
                add(conf, known ? CheckState.Ok : CheckState.Warn, "UPN suffix @" + kv.Key,
                    "used for " + string.Join(", ", kv.Value.ToArray()) + (ad.UpnSuffixesRead ? (known ? "" : " - NOT registered in the forest") : " (forest list could not be read)"),
                    known ? null : "Add it in 'Active Directory Domains and Trusts' > Properties > UPN Suffixes, or change UpnSuffix= in the config file.");
            }

            // Groups
            foreach (string g in cfg.AllGroups())
            {
                string dn = null;
                try { dn = ad.FindGroup(g); } catch { }
                var users = new List<string>();
                if (cfg.CommonGroups.Exists(x => x.Equals(g, StringComparison.OrdinalIgnoreCase))) users.Add("all users");
                users.AddRange(types.Where(t => t.Groups.Exists(x => x.Equals(g, StringComparison.OrdinalIgnoreCase))).Select(t => t.Name));
                add(conf, dn != null ? CheckState.Ok : CheckState.Fail, "Group '" + g + "'",
                    (dn != null ? AdService.FriendlyPath(AdService.ParentDn(dn)) : "NOT FOUND in " + ad.DomainDns) + "  (" + string.Join(", ", users.ToArray()) + ")",
                    dn != null ? null : "Create the group, or correct the name in Groups= (name, CN or full DN).");
            }

            // Default OUs
            foreach (UserType t in types)
            {
                if (string.IsNullOrWhiteSpace(t.DefaultOU)) continue;
                string dn = ad.NormalizeDn(cfg.Expand(t.DefaultOU, "", "", "", ad.DomainDns, t.Name));
                bool exists = ad.ObjectExists(dn);
                bool inScope = exists && ad.InCreateScope(dn);
                add(conf, inScope ? CheckState.Ok : CheckState.Warn, t.Name + " DefaultOU", AdService.FriendlyPath(dn) +
                    (!exists ? " - not found" : !inScope ? " - not in your create-user OUs" : ""),
                    !exists ? "Create the OU or correct DefaultOU= in [UserType:" + t.Name + "]."
                            : !inScope ? "Add it (or a parent) under [CreateUserOUs]." : null);
            }

            // Home / profile shares - the parent folder must exist; the user's own folder is created by the app.
            var folders = new List<KeyValuePair<string, string>>();   // (what, parent folder)
            foreach (UserType t in types)
            {
                if (!string.IsNullOrWhiteSpace(t.HomePath))
                    folders.Add(new KeyValuePair<string, string>(t.Name + " home folders", ParentOf(cfg.Expand(t.HomePath, "x", "", "", ad.DomainDns, t.Name))));
                string prof = cfg.ProfilePathFor(t);
                if (!string.IsNullOrWhiteSpace(prof))
                    folders.Add(new KeyValuePair<string, string>(t.Name + " profiles", ParentOf(cfg.Expand(prof, "x", "", "", ad.DomainDns, t.Name))));
            }
            var serverOk = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in folders.GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            {
                string path = group.Key;
                string what = string.Join(", ", group.Select(kv => kv.Key).Distinct().ToArray());
                if (!path.StartsWith(@"\\") || path.Contains("<"))
                {
                    add(conf, CheckState.Warn, what, path, "Use a UNC path such as \\\\server\\share\\<username>.");
                    continue;
                }
                string server = AdService.ServerOf(path);
                bool up;
                if (!serverOk.TryGetValue(server, out up)) serverOk[server] = up = PortOpen(server, 445);
                bool exists = up && Directory.Exists(path);
                add(conf, exists ? CheckState.Ok : CheckState.Warn, what, path +
                    (!up ? " - server not reachable (SMB 445)" : !exists ? " - folder not found / no access" : ""),
                    !up ? "Check the server name (FileServer= / DCServer=) and that TCP 445 is open."
                        : !exists ? "Create the share/folder, or correct the path in the config file. Operators need rights to create folders there." : null);
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Installers
        // ------------------------------------------------------------------

        public static void OpenDotNetDownload()
        {
            Process.Start(DotNetUrl);
        }

        /// <summary>Starts an elevated PowerShell that installs the RSAT AD tools. Returns false if UAC was cancelled.</summary>
        public static bool InstallRsat()
        {
            string cmd = IsServerOs()
                ? "Install-WindowsFeature RSAT-AD-PowerShell"
                : "Add-WindowsCapability -Online -Name '" + RsatCapability + "'";
            string script = "Write-Host 'Installing RSAT Active Directory tools - this can take several minutes...';" +
                            "try { " + cmd + " -ErrorAction Stop | Out-Host; Write-Host 'Done.' -ForegroundColor Green }" +
                            " catch { Write-Host $_ -ForegroundColor Red; Write-Host 'If your PCs get updates from WSUS, ask for ''Features on Demand'' to be allowed from Windows Update.' };" +
                            "Read-Host 'Press Enter to close'";
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try { Process.Start(psi); return true; }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return false;   // UAC cancelled
                throw;
            }
        }
    }
}
