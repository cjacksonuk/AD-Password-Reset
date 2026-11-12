using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ADUserManager
{
    /// <summary>
    /// A kind of account the app can create (e.g. Curriculum, Admin, Student) - one [UserType:Name]
    /// section in the config file. Any setting left blank falls back to the global value.
    /// </summary>
    public class UserType
    {
        public string Name = "";
        public string Description = "";
        public string HomePath = "";
        public string HomeDrive = "";
        public string ProfilePath = "";
        public string DefaultOU = "";
        public List<string> Groups = new List<string>();
        /// <summary>Overrides [NewUsers] UsernameFormat for this type (blank = use the global one).</summary>
        public string UsernameFormat = "";
        /// <summary>Overrides [NewUsers] UpnSuffix for this type (blank = use the global one).</summary>
        public string UpnSuffix = "";
        /// <summary>Overrides [NewUsers] EmailFormat for this type (blank = use the global one).</summary>
        public string EmailFormat = "";
        /// <summary>False = section kept in the file but not offered in the app (e.g. a type being prepared).</summary>
        public bool Enabled = true;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Description) ? Name : Name + "  -  " + Description;
        }
    }

    /// <summary>
    /// Settings stored in ADUserManager.ini next to the executable.
    /// </summary>
    public class AppConfig
    {
        public const string FileName = "ADUserManager.ini";
        public const string PlaceholderServer = "CHANGE-ME";
        public const string DefaultUsernameFormat = "<firstinitial><lastname>";

        public string FilePath = "";
        public bool CreatedNew;
        public List<string> Warnings = new List<string>();

        // [General]
        public string OrganisationName = "";
        public string Domain = "";
        public string DomainController = "";
        /// <summary>Domain controller name for the &lt;dcserver&gt; placeholder (e.g. profiles / DFS).</summary>
        public string DCServer = "";
        /// <summary>File server name for the &lt;fileserver&gt; placeholder (home folders). &lt;server&gt; is an alias.</summary>
        public string FileServer = "";
        /// <summary>Old single Server= setting from earlier config files - used when FileServer/DCServer are not set.</summary>
        public string LegacyServer = "";
        /// <summary>Where DCServer / FileServer came from: "config file", "auto-detected ..." or "".</summary>
        public string DCServerSource = "", FileServerSource = "";
        public string LogFile = "ADUserManager-AuditLog.csv";
        public int MaxSearchResults = 500;

        // [Scope]
        public bool DetectScopeFromPermissions = true;
        public List<string> CreateUserOUs = new List<string>();
        public List<string> ResetPasswordOUs = new List<string>();
        public List<string> ExcludeOUs = new List<string>();

        // [Paths]
        public string ProfilePath = @"\\<domain>\dfs\profiles$\roaming\<username>";
        public string HomeDrive = "H:";

        // [Passwords]
        public string DefaultPassword = "Welcome2026!";
        public string GeneratedPasswordStyle = "Memorable";
        public int GeneratedPasswordLength = 12;

        // [NewUsers]
        /// <summary>One or more formats separated by ; - the first is the suggestion, the rest are offered as alternatives.</summary>
        public string UsernameFormat = DefaultUsernameFormat;
        public string UpnSuffix = "";
        public string EmailFormat = "";
        /// <summary>Groups every new user is added to, whatever the user type.</summary>
        public List<string> CommonGroups = new List<string>();
        public bool EnableAccount = true;
        public bool CreateHomeFolder = true;
        public string HomeFolderRights = "Modify";

        // [Features]
        public bool AllowDisable = true;
        public bool AllowExpiry = true;
        // [Features] tabs shown in the app (at least one is always kept)
        public bool ShowResetTab = true;
        public bool ShowCreateTab = true;
        public bool ShowSettingsTab = true;
        /// <summary>Tab opened when the app starts: "Reset", "Create" or "Scope".</summary>
        public string DefaultTab = "Reset";

        /// <summary>Every [UserType:...] section, including ones switched off with Enabled=false.</summary>
        public List<UserType> UserTypes = new List<UserType>();

        /// <summary>User types offered in the app (Enabled=true).</summary>
        public List<UserType> ActiveUserTypes
        {
            get { return UserTypes.FindAll(t => t.Enabled); }
        }

        public static bool IsSet(string server)
        {
            return !string.IsNullOrWhiteSpace(server) &&
                   !string.Equals(server.Trim(), PlaceholderServer, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when every server placeholder used in the path templates has a value.</summary>
        public bool ServerConfigured
        {
            get { return MissingServerSettings().Count == 0; }
        }

        IEnumerable<string> PathTemplates()
        {
            yield return ProfilePath;
            yield return EmailFormat;
            foreach (UserType t in ActiveUserTypes)
            {
                yield return t.HomePath;
                yield return t.ProfilePath;
                yield return t.EmailFormat;
            }
        }

        static bool Uses(string template, string placeholder)
        {
            return !string.IsNullOrEmpty(template) &&
                   template.IndexOf("<" + placeholder + ">", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Names of server settings (DCServer / FileServer) that a path uses but which are not filled in.</summary>
        public List<string> MissingServerSettings()
        {
            bool usesDc = false, usesFs = false;
            foreach (string t in PathTemplates())
            {
                if (Uses(t, "dcserver")) usesDc = true;
                if (Uses(t, "fileserver") || Uses(t, "server")) usesFs = true;
            }
            var missing = new List<string>();
            if (usesDc && !IsSet(DCServer)) missing.Add("DCServer");
            if (usesFs && !IsSet(FileServer)) missing.Add("FileServer");
            return missing;
        }

        // ------------------------------------------------------------------
        // Load / create
        // ------------------------------------------------------------------

        public static AppConfig LoadOrCreate(string directory)
        {
            string path = Path.Combine(directory, FileName);
            bool created = false;
            string createError = null;

            if (!File.Exists(path))
            {
                try
                {
                    File.WriteAllText(path, DefaultTemplate, new UTF8Encoding(true));
                    created = true;
                }
                catch (Exception ex)
                {
                    createError = ex.Message;
                }
            }

            AppConfig cfg;
            if (File.Exists(path))
            {
                cfg = Parse(File.ReadAllText(path));
            }
            else
            {
                cfg = Parse(DefaultTemplate);
                cfg.Warnings.Add("Could not create " + path + " (" + createError +
                                 "). Built-in defaults are being used.");
            }
            cfg.FilePath = path;
            cfg.CreatedNew = created;
            return cfg;
        }

        static readonly HashSet<string> ListSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CreateUserOUs", "ResetPasswordOUs", "ExcludeOUs"
        };

        /// <summary>Section prefixes for user types. StaffType: is the older name and still accepted.</summary>
        static readonly string[] TypePrefixes = { "UserType:", "StaffType:" };

        public static AppConfig Parse(string text)
        {
            var cfg = new AppConfig();
            string section = "";
            UserType currentType = null;
            int lineNo = 0;

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                lineNo++;
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    currentType = null;
                    foreach (string prefix in TypePrefixes)
                    {
                        if (!section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        string name = section.Substring(prefix.Length).Trim();
                        if (name.Length == 0) { cfg.Warnings.Add("Line " + lineNo + ": [" + section + "] has no type name."); break; }
                        if (cfg.UserTypes.Exists(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            cfg.Warnings.Add("User type '" + name + "' is defined more than once - only the first is used.");
                            currentType = new UserType { Name = name };   // parsed but discarded
                            break;
                        }
                        currentType = new UserType { Name = name };
                        cfg.UserTypes.Add(currentType);
                        break;
                    }
                    continue;
                }

                if (ListSections.Contains(section))
                {
                    List<string> target = section.Equals("CreateUserOUs", StringComparison.OrdinalIgnoreCase)
                        ? cfg.CreateUserOUs
                        : section.Equals("ResetPasswordOUs", StringComparison.OrdinalIgnoreCase)
                            ? cfg.ResetPasswordOUs
                            : cfg.ExcludeOUs;
                    // Also accept "OU1=OU=Staff,DC=..." style - strip a leading key that is not an RDN type.
                    string value = line;
                    Match m = Regex.Match(value, @"^(OU\d+|Path\d*|Item\d*)\s*=\s*(.+)$", RegexOptions.IgnoreCase);
                    if (m.Success) value = m.Groups[2].Value.Trim();
                    if (value.Length > 0 && !target.Contains(value)) target.Add(value);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    cfg.Warnings.Add("Line " + lineNo + " ignored (expected Key=Value): " + line);
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (currentType != null)
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "description": currentType.Description = val; break;
                        case "homepath": currentType.HomePath = val; break;
                        case "homedrive": currentType.HomeDrive = val; break;
                        case "profilepath": currentType.ProfilePath = val; break;
                        case "defaultou": currentType.DefaultOU = val; break;
                        // Groups= may be repeated - each line adds to the list.
                        case "groups":
                        case "group": AddUnique(currentType.Groups, SplitList(val)); break;
                        case "usernameformat": currentType.UsernameFormat = val; break;
                        case "upnsuffix": currentType.UpnSuffix = val.TrimStart('@'); break;
                        case "emailformat": currentType.EmailFormat = val; break;
                        case "enabled": currentType.Enabled = ToBool(val, true); break;
                        default: cfg.Warnings.Add("Unknown setting '" + key + "' in [" + section + "]"); break;
                    }
                    continue;
                }

                switch (section.ToLowerInvariant() + "." + key.ToLowerInvariant())
                {
                    case "general.organisationname": cfg.OrganisationName = val; break;
                    case "general.domain": cfg.Domain = val; break;
                    case "general.domaincontroller": cfg.DomainController = val; break;
                    case "general.dcserver": cfg.DCServer = CleanServer(val); break;
                    case "general.fileserver": cfg.FileServer = CleanServer(val); break;
                    case "general.server": cfg.LegacyServer = CleanServer(val); break;
                    case "general.logfile": cfg.LogFile = val; break;
                    case "general.maxsearchresults": cfg.MaxSearchResults = ToInt(val, 500, 10, 5000); break;
                    case "scope.detectfrompermissions": cfg.DetectScopeFromPermissions = ToBool(val, true); break;
                    case "paths.profilepath": cfg.ProfilePath = val; break;
                    case "paths.homedrive": cfg.HomeDrive = val; break;
                    case "passwords.defaultpassword": cfg.DefaultPassword = val; break;
                    case "passwords.generatedpasswordstyle": cfg.GeneratedPasswordStyle = val; break;
                    case "passwords.generatedpasswordlength": cfg.GeneratedPasswordLength = ToInt(val, 12, 8, 64); break;
                    case "newusers.usernameformat": cfg.UsernameFormat = val; break;
                    case "newusers.upnsuffix": cfg.UpnSuffix = val.TrimStart('@'); break;
                    case "newusers.emailformat": cfg.EmailFormat = val; break;
                    case "newusers.groups":
                    case "newusers.group": AddUnique(cfg.CommonGroups, SplitList(val)); break;
                    case "newusers.enableaccount": cfg.EnableAccount = ToBool(val, true); break;
                    case "newusers.createhomefolder": cfg.CreateHomeFolder = ToBool(val, true); break;
                    case "newusers.homefolderrights": cfg.HomeFolderRights = val; break;
                    case "features.allowdisable": cfg.AllowDisable = ToBool(val, true); break;
                    case "features.allowexpiry": cfg.AllowExpiry = ToBool(val, true); break;
                    case "features.showresetpasswordtab":
                    case "features.showresettab": cfg.ShowResetTab = ToBool(val, true); break;
                    case "features.showcreateusertab":
                    case "features.showcreatetab": cfg.ShowCreateTab = ToBool(val, true); break;
                    case "features.showscopetab":
                    case "features.showsettingstab": cfg.ShowSettingsTab = ToBool(val, true); break;
                    case "features.defaulttab":
                    {
                        string v = val.Trim().ToLowerInvariant().Replace(" ", "");
                        if (v.Length == 0 || v.StartsWith("reset") || v == "password") cfg.DefaultTab = "Reset";
                        else if (v.StartsWith("create") || v == "newuser" || v == "adduser") cfg.DefaultTab = "Create";
                        else if (v.StartsWith("scope") || v.StartsWith("setting")) cfg.DefaultTab = "Scope";
                        else
                        {
                            cfg.DefaultTab = "Reset";
                            cfg.Warnings.Add("DefaultTab=" + val + " not recognised - use Reset, Create or Scope. Opening on Reset password.");
                        }
                        break;
                    }
                    default:
                        cfg.Warnings.Add("Unknown setting '" + key + "'" +
                                         (section.Length > 0 ? " in [" + section + "]" : ""));
                        break;
                }
            }

            if (UsernameFormats(cfg.UsernameFormat).Count == 0) cfg.UsernameFormat = DefaultUsernameFormat;
            foreach (UserType t in cfg.UserTypes)
                foreach (string f in UsernameFormats(t.UsernameFormat))
                    if (f.IndexOf('<') < 0)
                        cfg.Warnings.Add("[UserType:" + t.Name + "] UsernameFormat '" + f + "' has no <placeholders> - every suggestion would be the same.");
            foreach (string f in UsernameFormats(cfg.UsernameFormat))
                if (f.IndexOf('<') < 0)
                    cfg.Warnings.Add("[NewUsers] UsernameFormat '" + f + "' has no <placeholders> - every suggestion would be the same.");

            // Older config files had a single Server= value - use it for whichever new setting is not filled in.
            if (IsSet(cfg.LegacyServer))
            {
                if (!IsSet(cfg.FileServer)) cfg.FileServer = cfg.LegacyServer;
                if (!IsSet(cfg.DCServer)) cfg.DCServer = cfg.LegacyServer;
            }
            if (IsSet(cfg.DCServer)) cfg.DCServerSource = "config file";
            if (IsSet(cfg.FileServer)) cfg.FileServerSource = "config file";
            if (!cfg.ShowResetTab && !cfg.ShowCreateTab && !cfg.ShowSettingsTab)
            {
                cfg.ShowSettingsTab = true;
                cfg.Warnings.Add("[Features] hides every tab - the Scope & settings tab is shown so the app can still be used.");
            }
            return cfg;
        }

        /// <summary>Accepts "psfp01", "\\psfp01" or "\\psfp01\" and stores just the name.</summary>
        static string CleanServer(string val)
        {
            return (val ?? "").Trim().Trim('\\').Trim();
        }

        static List<string> SplitList(string val)
        {
            var list = new List<string>();
            foreach (string part in (val ?? "").Split(';'))
            {
                string p = part.Trim();
                if (p.Length > 0) list.Add(p);
            }
            return list;
        }

        static void AddUnique(List<string> target, IEnumerable<string> items)
        {
            foreach (string i in items)
                if (!target.Exists(x => x.Equals(i, StringComparison.OrdinalIgnoreCase))) target.Add(i);
        }

        static bool ToBool(string v, bool def)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "true" || v == "yes" || v == "1" || v == "on") return true;
            if (v == "false" || v == "no" || v == "0" || v == "off") return false;
            return def;
        }

        static int ToInt(string v, int def, int min, int max)
        {
            int n;
            if (!int.TryParse(v, out n)) return def;
            return Math.Max(min, Math.Min(max, n));
        }

        // ------------------------------------------------------------------
        // Placeholder expansion
        // ------------------------------------------------------------------

        /// <summary>Year used for &lt;yyyy&gt; / &lt;yy&gt; - settable so tests are repeatable.</summary>
        public DateTime Today = DateTime.Today;

        /// <summary>
        /// Replaces &lt;placeholders&gt;. Any text placeholder can be cut to its first N characters
        /// with a colon, e.g. &lt;lastname:4&gt; or &lt;firstname:2&gt;.
        /// </summary>
        public string Expand(string template, string username, string first, string last, string domain, string userType)
        {
            if (string.IsNullOrEmpty(template)) return "";
            first = first ?? ""; last = last ?? "";
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map["dcserver"] = (DCServer ?? "").Trim();
            map["fileserver"] = (FileServer ?? "").Trim();
            map["server"] = map["fileserver"];   // older placeholder name
            map["domain"] = domain ?? "";
            map["username"] = username ?? "";
            map["firstname"] = first;
            map["lastname"] = last;
            map["firstinitial"] = first.Length > 0 ? first.Substring(0, 1) : "";
            map["lastinitial"] = last.Length > 0 ? last.Substring(0, 1) : "";
            map["usertype"] = userType ?? "";
            map["stafftype"] = map["usertype"];  // older placeholder name
            map["yyyy"] = Today.Year.ToString("0000");
            map["yy"] = (Today.Year % 100).ToString("00");

            return Regex.Replace(template, @"<([A-Za-z]+)(?::(\d{1,2}))?>", delegate (Match m)
            {
                string v;
                if (!map.TryGetValue(m.Groups[1].Value, out v)) return m.Value;
                if (m.Groups[2].Success)
                {
                    int n = int.Parse(m.Groups[2].Value);
                    if (n > 0 && v.Length > n) v = v.Substring(0, n);
                }
                return v;
            });
        }

        /// <summary>Splits "fmt1; fmt2" into its formats.</summary>
        public static List<string> UsernameFormats(string value)
        {
            return SplitList(value);
        }

        /// <summary>The username formats for a user type (its own UsernameFormat, else the global one).</summary>
        public List<string> UsernameFormatsFor(UserType t)
        {
            List<string> f = t != null ? UsernameFormats(t.UsernameFormat) : new List<string>();
            if (f.Count == 0) f = UsernameFormats(UsernameFormat);
            if (f.Count == 0) f.Add(DefaultUsernameFormat);
            return f;
        }

        /// <summary>Builds the suggested logon name from the first username format.</summary>
        public string BuildUsername(string first, string last, UserType t = null)
        {
            return BuildUsernameFrom(UsernameFormatsFor(t)[0], first, last, t);
        }

        /// <summary>Every distinct logon name the configured formats give for this person, suggestion first.</summary>
        public List<string> BuildUsernameOptions(string first, string last, UserType t = null)
        {
            var list = new List<string>();
            foreach (string f in UsernameFormatsFor(t))
            {
                string s = BuildUsernameFrom(f, first, last, t);
                if (s.Length > 0 && !list.Contains(s)) list.Add(s);
            }
            return list;
        }

        public string BuildUsernameFrom(string format, string first, string last, UserType t)
        {
            string clean = Expand(format, "", Simplify(first), Simplify(last), "", t != null ? t.Name : "");
            clean = Regex.Replace(clean.ToLowerInvariant(), @"[^a-z0-9._-]", "");
            clean = Regex.Replace(clean, @"([._-])[._-]+", "$1");   // "jane..smith" when a part is empty
            clean = clean.Trim('.', '-', '_');
            if (clean.Length > 20) clean = clean.Substring(0, 20).TrimEnd('.', '-', '_');
            return clean;
        }

        static string Simplify(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Remove accents (e.g. é -> e) then anything that is not a letter/digit.
            string formD = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in formD)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) ==
                    System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        public string ProfilePathFor(UserType t)
        {
            if (t != null && t.ProfilePath.Equals("none", StringComparison.OrdinalIgnoreCase)) return "";
            return t != null && !string.IsNullOrEmpty(t.ProfilePath) ? t.ProfilePath : ProfilePath;
        }

        public string HomeDriveFor(UserType t)
        {
            string d = t != null && !string.IsNullOrEmpty(t.HomeDrive) ? t.HomeDrive : HomeDrive;
            d = (d ?? "").Trim().TrimEnd('\\');
            if (d.Length == 1) d += ":";
            return d.ToUpperInvariant();
        }

        /// <summary>UPN suffix for a user type: the type's UpnSuffix, else [NewUsers] UpnSuffix, else the AD domain.</summary>
        public string UpnSuffixFor(UserType t, string domainDns)
        {
            if (t != null && !string.IsNullOrWhiteSpace(t.UpnSuffix)) return t.UpnSuffix.Trim();
            if (!string.IsNullOrWhiteSpace(UpnSuffix)) return UpnSuffix.Trim();
            return domainDns ?? "";
        }

        public string EmailFormatFor(UserType t)
        {
            return t != null && !string.IsNullOrWhiteSpace(t.EmailFormat) ? t.EmailFormat : EmailFormat;
        }

        /// <summary>[NewUsers] Groups plus the user type's Groups, without duplicates.</summary>
        public List<string> GroupsFor(UserType t)
        {
            var list = new List<string>(CommonGroups);
            if (t != null) AddUnique(list, t.Groups);
            return list;
        }

        /// <summary>Every group named anywhere in the config (for the requirements check).</summary>
        public List<string> AllGroups()
        {
            var list = new List<string>(CommonGroups);
            foreach (UserType t in ActiveUserTypes) AddUnique(list, t.Groups);
            return list;
        }

        // ------------------------------------------------------------------
        // Default file
        // ------------------------------------------------------------------

        public const string DefaultTemplate =
@"; =====================================================================
;  AD User Manager - configuration file
;
;  Created automatically next to ADUserManager.exe the first time the
;  app runs. Edit with Notepad, then click 'Reload config' in the app's
;  'Scope & settings' page (or restart the app).
;
;  Placeholders you can use in paths and formats:
;    <dcserver>      the value of DCServer= below (domain controller)
;    <fileserver>    the value of FileServer= below (home folder server)
;    <domain>        DNS domain name, e.g. school.local (auto-detected). Use it
;                    for domain-based DFS paths, e.g. \\<domain>\dfs\profiles$\...
;    <username>      the user's logon name (sAMAccountName)
;    <firstname> <lastname> <firstinitial> <lastinitial>
;    <usertype>      the user type chosen when creating the user
;    <yyyy> <yy>     the current year, e.g. 2026 / 26 (handy for intake years)
;  Add :N to cut a placeholder to its first N letters, e.g. <lastname:4>
; =====================================================================

[General]
; Name shown in the app header (optional)
OrganisationName=
; Leave blank to use the domain this PC is joined to
Domain=
; Leave blank to pick a domain controller automatically
DomainController=
; Domain controller name used for the <dcserver> placeholder, e.g. dc01
; Blank = the domain controller the app connects to. (Name only - the \\ is optional.)
DCServer=
; File server holding the home folders, used for the <fileserver> placeholder, e.g. fs01
; Blank = the server most existing users in scope have their home folder on.
FileServer=
; Audit log (CSV, opens in Excel) kept next to the app: who did what, to which user,
; from which PC. Blank = no log. Passwords are never logged.
LogFile=ADUserManager-AuditLog.csv
MaxSearchResults=500

[Scope]
; Which accounts the app may touch.
;  * If [ResetPasswordOUs] / [CreateUserOUs] below are EMPTY and this is true,
;    the app works out the OUs the signed-in person has AD permission for.
;  * If you list OUs below, ONLY those OUs are used - even if the person's AD
;    permissions reach further. Use this to narrow what the app shows.
;  * Set to false to require the lists to be filled in (nothing is detected).
; AD permissions are always enforced as well - listing an OU never grants extra rights.
DetectFromPermissions=true

[ResetPasswordOUs]
; OUs whose users can be LISTED / SEARCHED, have their PASSWORD RESET, be
; unlocked, disabled/enabled or given an expiry date. One OU per line, UNDER
; these comments (lines starting with ; are ignored). Sub-OUs are included.
; Users outside these OUs cannot be found in the app at all.
; Full DN, or leave off the DC= part and the domain is added automatically.
; Examples:
; OU=Users,OU=School,DC=school,DC=local
; OU=Staff,OU=Users,OU=School
; OU=Students,OU=Users,OU=School

[CreateUserOUs]
; OUs where NEW users may be CREATED - one per line, as above.
; Sub-OUs are included in the 'Create in OU' drop-down.
; Examples:
; OU=Staff,OU=Users,OU=School
; OU=Students,OU=Users,OU=School

[ExcludeOUs]
; Optional - OUs (and their sub-OUs) that are always hidden, even when
; permissions or the lists above would include them.
; OU=Domain Controllers
; OU=Leavers,OU=Users,OU=School

[Paths]
; Roaming profile path for new users (blank = none). A user type can override it.
; Match what existing users have ('Scope & settings' lists the paths found):
;   \\<domain>\dfs\...     domain-based DFS namespace  -> \\school.local\dfs\...
;   \\<dcserver>\dfs\...   one named server            -> \\dc01\dfs\...
;   \\<fileserver>\...     the home folder server      -> \\fs01\...
ProfilePath=\\<domain>\dfs\profiles$\roaming\<username>
; Drive letter the home folder is mapped to. A user type can override it.
HomeDrive=H:

[Passwords]
; Offered in the app as the default. Using it always forces a change at next logon.
DefaultPassword=Welcome2026!
; Memorable (e.g. Maple-Tiger-47!) or Random (e.g. k7#Rt9wQ2!mP)
GeneratedPasswordStyle=Memorable
GeneratedPasswordLength=12

[NewUsers]
; Suggested logon name (sAMAccountName). The person creating the account can
; ALWAYS type a different one - this is only the suggestion.
; List several formats separated by ; - the first is filled in, the others are
; shown as one-click alternatives (and tried if the first is already taken).
; Names are lower-cased, accents and punctuation removed, max 20 characters.
;
;   Format                          Jane Smith gives
;   <firstinitial><lastname>        jsmith
;   <firstname>.<lastname>          jane.smith
;   <firstname><lastname>           janesmith
;   <firstinitial>.<lastname>       j.smith
;   <firstname>_<lastname>          jane_smith
;   <firstname>-<lastname>          jane-smith
;   <lastname><firstinitial>        smithj
;   <lastname>.<firstname>          smith.jane
;   <firstname><lastinitial>        janes
;   <firstname>                     jane
;   <lastname:4><firstinitial>      smitj      (first 4 letters of the surname)
;   <firstname:3><lastname:3>       jansmi
;   <yy><firstinitial><lastname>    26jsmith   (current year - e.g. student intake)
;   <lastname><firstinitial><yy>    smithj26
;   <yyyy><lastname><firstinitial>  2026smithj
;
; Example with alternatives:  UsernameFormat=<firstinitial><lastname>; <firstname>.<lastname>
UsernameFormat=<firstinitial><lastname>

; UPN suffix - the part after the @ in the user's sign-in name (user@suffix).
; Blank = the AD domain name (e.g. school.local). To use another suffix, such
; as your email / Microsoft 365 domain, it must first be added in 'Active
; Directory Domains and Trusts' > Properties > UPN Suffixes. The 'Scope &
; settings' page lists the suffixes your forest has. A user type can override it.
; Example: UpnSuffix=school.org.uk
UpnSuffix=

; Email address to set, e.g. <username>@school.org.uk  (blank = not set)
; A user type can override it.
EmailFormat=

; Groups EVERY new user is added to, whatever the user type (optional).
; Separate with ; or repeat the line. Group name (sAMAccountName / CN) or full DN.
; Example: Groups=All Users; Wi-Fi Users
Groups=

EnableAccount=true
; Create the home folder on the server and give the user rights to it
CreateHomeFolder=true
; Modify or FullControl
HomeFolderRights=Modify

[Features]
; Show the Disable / Enable account button (still subject to AD permissions)
AllowDisable=true
; Show account expiry options (still subject to AD permissions)
AllowExpiry=true

; Tabs shown in the app - set to false to hide one, e.g. a helpdesk copy that
; can only reset passwords. Hiding a tab only tidies the app: AD permissions
; and the OU lists above are what actually limit what someone can do.
; If every tab is set to false, Scope & settings is still shown.
; With Scope & settings hidden, restart the app to pick up config changes.
ShowResetPasswordTab=true
ShowCreateUserTab=true
ShowScopeTab=true
; Tab the app opens on: Reset (Reset password), Create (Create user) or
; Scope (Scope & settings). If that tab is hidden, the first shown tab opens.
DefaultTab=Reset

; ---------------------------------------------------------------------
; User types - one [UserType:Name] section per kind of account. They
; appear in the 'User type' drop-down in the order listed here.
; To add a new type in future, copy a section, give it a new name and
; reload the config. Settings left blank use the global value above.
;
;   Description     text shown in the drop-down
;   HomePath        home folder (mapped to HomeDrive) - blank = no home folder
;   HomeDrive       optional - overrides [Paths] HomeDrive
;   ProfilePath     optional - overrides [Paths] ProfilePath. none = no roaming
;                   profile for this type
;   DefaultOU       optional - OU pre-selected when this type is chosen.
;                   Placeholders work, e.g. OU=Intake<yyyy>,OU=Students,OU=Users
;   Groups          optional - groups to add the user to. Separate with ; or
;                   repeat the Groups= line. Name (sAMAccountName / CN) or full DN.
;   UsernameFormat  optional - overrides [NewUsers] UsernameFormat
;   UpnSuffix       optional - overrides [NewUsers] UpnSuffix
;   EmailFormat     optional - overrides [NewUsers] EmailFormat
;   Enabled         optional - false hides the type without deleting it
;
; (Older files that use [StaffType:Name] still work.)
; ---------------------------------------------------------------------
[UserType:Curriculum]
Description=Curriculum / teaching staff
HomePath=\\<fileserver>\ustaff\curric\<username>
DefaultOU=
; Example: Groups=CurricStaff; Teachers; Staff Wi-Fi
Groups=

[UserType:Admin]
Description=Admin / support staff
HomePath=\\<fileserver>\ustaff\admin\<username>
DefaultOU=
; Example: Groups=AdminStaff; Finance Users
Groups=

[UserType:Student]
Description=Students / pupils
HomePath=\\<fileserver>\ustudents\<username>
; Example: DefaultOU=OU=Intake<yyyy>,OU=Students,OU=Users,OU=School
DefaultOU=
; Example: Groups=Students; Student Wi-Fi
Groups=
; Example: UsernameFormat=<yy><firstinitial><lastname>; <firstname>.<lastname>
UsernameFormat=
; Example: UpnSuffix=students.school.org.uk
UpnSuffix=

; Example of a type prepared for later - copy, rename and set Enabled=true.
; [UserType:Governor]
; Description=Governors
; HomePath=
; Groups=Governors
; Enabled=false
";
    }
}
