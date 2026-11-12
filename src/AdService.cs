using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ADUserManager
{
    public class AdUser
    {
        public string Dn = "", Sam = "", DisplayName = "", GivenName = "", Surname = "", Upn = "";
        public string Description = "", Mail = "", Title = "";
        public string ProfilePath = "", HomeDirectory = "", HomeDrive = "";
        public bool Enabled, Locked, MustChange, NeverExpires, CannotChange;
        public DateTime? PwdLastSet, LastLogon, Created;
        /// <summary>Last day the account can be used (local date), null = never expires.</summary>
        public DateTime? ExpiresEndOf;
        public bool Expired;

        public string Name { get { return string.IsNullOrEmpty(DisplayName) ? Sam : DisplayName; } }
        public string Location { get { return AdService.FriendlyPath(AdService.ParentDn(Dn)); } }
    }

    public class NewUserRequest
    {
        public string OuDn, FirstName, LastName, DisplayName, Sam, Upn, Mail, Title, Description;
        public string ProfilePath, HomeDirectory, HomeDrive;
        public List<string> Groups = new List<string>();
        public string Password;
        public bool MustChange, Enabled, CreateHomeFolder;
        public string HomeFolderRights = "Modify";
        public DateTime? ExpiresEndOf;
    }

    public class CreateResult
    {
        public string Dn, Sam;
        public List<string> Warnings = new List<string>();
        public List<string> Done = new List<string>();
    }

    /// <summary>
    /// All Active Directory work. Uses the signed-in Windows account (Kerberos) -
    /// no credentials are ever prompted for or stored.
    /// </summary>
    public class AdService
    {
        const AuthenticationTypes Auth =
            AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing;

        static readonly Guid ResetPasswordRight = new Guid("00299570-246d-11d0-a768-00aa006e0529");
        static readonly Guid UserClass = new Guid("bf967aba-0de6-11d0-a285-00aa003049e2");

        static readonly string[] UserProps =
        {
            "distinguishedName", "sAMAccountName", "displayName", "givenName", "sn", "userPrincipalName",
            "description", "mail", "title", "profilePath", "homeDirectory", "homeDrive",
            "userAccountControl", "msDS-User-Account-Control-Computed", "lockoutTime", "pwdLastSet",
            "lastLogonTimestamp", "whenCreated", "accountExpires"
        };

        public string DomainDns = "", DomainDn = "", Dc = "";
        public string LogonName = "", UserSid = "", UserSam = "", UserDisplayName = "", UserDn = "", UserHome = "";

        /// <summary>Home / profile path patterns found on existing users in scope, most common first ("\\fs01\staff\&lt;username&gt;", count).</summary>
        public List<KeyValuePair<string, int>> HomePatterns = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> ProfilePatterns = new List<KeyValuePair<string, int>>();
        public int PatternSample;
        public int MinPwdLength = 7;
        public bool Complexity = true;
        /// <summary>UPN suffixes registered in the forest (domain name first).</summary>
        public List<string> UpnSuffixes = new List<string>();
        public bool UpnSuffixesRead;

        /// <summary>True when the suffix is the domain name or one of the forest's extra UPN suffixes.</summary>
        public bool IsKnownUpnSuffix(string suffix)
        {
            return UpnSuffixes.Contains((suffix ?? "").Trim().TrimStart('@'), StringComparer.OrdinalIgnoreCase);
        }

        public List<string> CreateOUs = new List<string>();     // every OU the user may create in
        public List<string> ResetRoots = new List<string>();    // collapsed search roots
        public List<string> ExcludeOUs = new List<string>();
        public string CreateScopeSource = "", ResetScopeSource = "";
        public List<string> ScopeWarnings = new List<string>();

        HashSet<string> _sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<ActiveDirectoryAccessRule> _userDefaultRules;

        public bool Connected { get { return DomainDn.Length > 0 && UserDn.Length > 0; } }

        // ------------------------------------------------------------------
        // Connection & identity
        // ------------------------------------------------------------------

        public void Connect(AppConfig cfg)
        {
            WindowsIdentity me = WindowsIdentity.GetCurrent();
            LogonName = me.Name;
            UserSid = me.User.Value;

            DomainDns = (cfg.Domain ?? "").Trim();
            if (DomainDns.Length == 0)
            {
                try { DomainDns = System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain().Name; }
                catch
                {
                    DomainDns = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
                }
            }
            if (string.IsNullOrEmpty(DomainDns))
                throw new ApplicationException("This PC does not appear to be joined to a domain. " +
                                               "Set Domain= in " + AppConfig.FileName + " or run the app on a domain PC.");

            string target = string.IsNullOrEmpty(cfg.DomainController) ? DomainDns : cfg.DomainController.Trim();
            try
            {
                using (var root = new DirectoryEntry("LDAP://" + target + "/RootDSE", null, null, Auth))
                {
                    DomainDn = Convert.ToString(root.Properties["defaultNamingContext"].Value);
                    Dc = Convert.ToString(root.Properties["dnsHostName"].Value);
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Could not contact a domain controller for '" + target + "'.\n\n" +
                                               Friendly(ex));
            }

            using (var d = Entry(DomainDn))
            {
                d.RefreshCache(new[] { "minPwdLength", "pwdProperties" });
                if (d.Properties["minPwdLength"].Value != null) MinPwdLength = Convert.ToInt32(d.Properties["minPwdLength"].Value);
                if (d.Properties["pwdProperties"].Value != null) Complexity = (Convert.ToInt32(d.Properties["pwdProperties"].Value) & 1) == 1;
            }

            // UPN suffixes users can sign in with: the domain name plus any extra suffixes added in
            // 'Active Directory Domains and Trusts' (stored on the Partitions container).
            UpnSuffixes = new List<string> { DomainDns };
            try
            {
                string conf;
                using (var rootDse = new DirectoryEntry("LDAP://" + Dc + "/RootDSE", null, null, Auth))
                    conf = Convert.ToString(rootDse.Properties["configurationNamingContext"].Value);
                using (var p = Entry("CN=Partitions," + conf))
                {
                    p.RefreshCache(new[] { "uPNSuffixes" });
                    foreach (object o in p.Properties["uPNSuffixes"])
                    {
                        string s = Convert.ToString(o).Trim();
                        if (s.Length > 0 && !UpnSuffixes.Contains(s, StringComparer.OrdinalIgnoreCase)) UpnSuffixes.Add(s);
                    }
                }
                UpnSuffixesRead = true;
            }
            catch { /* best effort - only used for warnings */ }

            try
            {
                using (var u = new DirectoryEntry("LDAP://" + Dc + "/<SID=" + UserSid + ">", null, null, Auth))
                {
                    u.RefreshCache(new[] { "distinguishedName", "sAMAccountName", "displayName", "tokenGroups", "homeDirectory" });
                    UserHome = Convert.ToString(u.Properties["homeDirectory"].Value);
                    UserDn = Convert.ToString(u.Properties["distinguishedName"].Value);
                    UserSam = Convert.ToString(u.Properties["sAMAccountName"].Value);
                    UserDisplayName = Convert.ToString(u.Properties["displayName"].Value);
                    if (UserDisplayName.Length == 0) UserDisplayName = UserSam;

                    _sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserSid, "S-1-1-0", "S-1-5-11" };
                    foreach (object o in u.Properties["tokenGroups"])
                    {
                        byte[] b = o as byte[];
                        if (b != null) _sids.Add(new SecurityIdentifier(b, 0).Value);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Signed in as " + LogonName + ", but that account could not be found in " +
                                               DomainDns + ". Sign in to Windows with a domain account.\n\n" + Friendly(ex));
            }
            if (me.Groups != null)
                foreach (IdentityReference g in me.Groups) _sids.Add(g.Value);
        }

        public DirectoryEntry Entry(string dn)
        {
            return new DirectoryEntry("LDAP://" + Dc + "/" + dn.Replace("/", "\\/"), null, null, Auth);
        }

        // ------------------------------------------------------------------
        // Scope
        // ------------------------------------------------------------------

        public void ResolveScopes(AppConfig cfg, Action<string> progress)
        {
            ScopeWarnings = new List<string>();
            ExcludeOUs = cfg.ExcludeOUs.Select(NormalizeDn).ToList();

            List<string> create = WithSubOUs(ValidateOUs(cfg.CreateUserOUs, "CreateUserOUs"));
            List<string> reset = ValidateOUs(cfg.ResetPasswordOUs, "ResetPasswordOUs");
            CreateScopeSource = create.Count > 0 ? "config file" : "";
            ResetScopeSource = reset.Count > 0 ? "config file" : "";

            if ((create.Count == 0 || reset.Count == 0) && cfg.DetectScopeFromPermissions)
            {
                List<string> dc, dr;
                if (progress != null) progress("Checking your Active Directory permissions...");
                DetectScopes(out dc, out dr);
                if (create.Count == 0) { create = dc; CreateScopeSource = "detected from your permissions"; }
                if (reset.Count == 0) { reset = dr; ResetScopeSource = "detected from your permissions"; }
            }
            if (create.Count == 0 && CreateScopeSource.Length == 0) CreateScopeSource = "none configured";
            if (reset.Count == 0 && ResetScopeSource.Length == 0) ResetScopeSource = "none configured";

            CreateOUs = create.Where(dn => !IsExcluded(dn))
                              .OrderBy(dn => FriendlyPath(dn), StringComparer.OrdinalIgnoreCase).ToList();
            ResetRoots = Collapse(reset.Where(dn => !IsExcluded(dn)))
                              .OrderBy(dn => FriendlyPath(dn), StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ------------------------------------------------------------------
        // Server / path discovery (used when DCServer= / FileServer= are blank)
        // ------------------------------------------------------------------

        /// <summary>
        /// Looks at existing users in scope to learn the home / profile path patterns, then fills in
        /// DCServer and FileServer on <paramref name="cfg"/> when the config file leaves them blank.
        /// Call after ResolveScopes. Never throws - discovery is best-effort.
        /// </summary>
        public void DiscoverServers(AppConfig cfg)
        {
            var home = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var prof = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            PatternSample = 0;
            try
            {
                var roots = Collapse(CreateOUs.Concat(ResetRoots)).ToList();
                const string filter = "(&(objectCategory=person)(objectClass=user)(|(homeDirectory=\\5c\\5c*)(profilePath=\\5c\\5c*)))";
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string r in roots)
                {
                    foreach (AdUser u in Query(r, filter, SearchScope.Subtree, 1000))
                    {
                        if (!seen.Add(u.Dn) || IsExcluded(u.Dn)) continue;
                        PatternSample++;
                        Count(home, Pattern(u.HomeDirectory, u.Sam));
                        Count(prof, Pattern(u.ProfilePath, u.Sam));
                    }
                    if (PatternSample >= 2000) break;
                }
            }
            catch { /* best effort */ }

            HomePatterns = home.OrderByDescending(kv => kv.Value).ToList();
            ProfilePatterns = prof.OrderByDescending(kv => kv.Value).ToList();

            if (!AppConfig.IsSet(cfg.DCServer) && Dc.Length > 0)
            {
                cfg.DCServer = Dc.Split('.')[0].ToLowerInvariant();
                cfg.DCServerSource = "auto-detected: the domain controller in use";
            }
            if (!AppConfig.IsSet(cfg.FileServer))
            {
                // Vote by short server name so \\FS01\.. and \\fs01.school.local\.. count together.
                // Domain-based DFS paths (\\school.local\dfs\..) are not a file server - skip them.
                var votes = HomePatterns.Where(kv => !IsDomainNamespace(ServerOf(kv.Key)))
                                        .GroupBy(kv => ServerOf(kv.Key).Split('.')[0].ToLowerInvariant())
                                        .Where(g => g.Key.Length > 0)
                                        .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(kv => kv.Value)))
                                        .OrderByDescending(kv => kv.Value).ToList();
                string s = votes.Count > 0 ? votes[0].Key : "";
                if (s.Length > 0)
                    cfg.FileServerSource = "auto-detected: home folders of " + votes[0].Value + " existing user(s)";
                else
                {
                    s = ServerOf(UserHome).Split('.')[0].ToLowerInvariant();
                    if (s.Length > 0) cfg.FileServerSource = "auto-detected: your own home folder";
                }
                if (s.Length > 0) cfg.FileServer = s;
            }
        }

        static void Count(Dictionary<string, int> d, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int n; d.TryGetValue(key, out n); d[key] = n + 1;
        }

        /// <summary>\\fs01\staff\jsmith (sam jsmith) -> \\fs01\staff\&lt;username&gt;. Null when the last folder isn't the username.</summary>
        public static string Pattern(string path, string sam)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(sam) || !path.StartsWith(@"\\")) return null;
            string p = path.TrimEnd('\\');
            // roaming profiles may carry a .V2/.V6 suffix on disk but AD normally stores the bare name
            if (p.EndsWith("\\" + sam, StringComparison.OrdinalIgnoreCase))
                return p.Substring(0, p.Length - sam.Length) + "<username>";
            return null;
        }

        /// <summary>True for the domain's own name (\\school.local or \\SCHOOL), i.e. a domain-based DFS namespace.</summary>
        public bool IsDomainNamespace(string server)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(DomainDns)) return false;
            return server.Equals(DomainDns, StringComparison.OrdinalIgnoreCase) ||
                   server.Equals(DomainDns.Split('.')[0], StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// \\school.local\dfs\..\&lt;username&gt; -> \\&lt;domain&gt;\dfs\..\&lt;username&gt;, and likewise
        /// &lt;dcserver&gt; / &lt;fileserver&gt;, so a path found on existing users can be pasted into the config.
        /// </summary>
        public string WithPlaceholders(string pattern, AppConfig cfg)
        {
            string server = ServerOf(pattern);
            if (server.Length == 0) return pattern;
            string rest = pattern.Substring(2 + server.Length);
            string shortName = server.Split('.')[0];
            string ph = null;
            if (server.Equals(DomainDns ?? "", StringComparison.OrdinalIgnoreCase)) ph = "<domain>";
            else if (cfg != null && SameServer(shortName, cfg.DCServer)) ph = "<dcserver>";
            else if (cfg != null && SameServer(shortName, cfg.FileServer)) ph = "<fileserver>";
            return ph == null ? pattern : @"\\" + ph + rest;
        }

        static bool SameServer(string shortName, string configured)
        {
            string c = (configured ?? "").Trim().TrimStart('\\').Split('.')[0];
            return c.Length > 0 && c.Equals(shortName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>\\fs01\share\x -> fs01</summary>
        public static string ServerOf(string unc)
        {
            if (string.IsNullOrEmpty(unc) || !unc.StartsWith(@"\\")) return "";
            string rest = unc.Substring(2);
            int i = rest.IndexOf('\\');
            return (i < 0 ? rest : rest.Substring(0, i)).Trim();
        }

        public string NormalizeDn(string dn)
        {
            dn = (dn ?? "").Trim().TrimEnd(',');
            if (dn.Length == 0) return dn;
            if (dn.IndexOf("DC=", StringComparison.OrdinalIgnoreCase) < 0) dn = dn + "," + DomainDn;
            return dn;
        }

        List<string> ValidateOUs(List<string> configured, string section)
        {
            var result = new List<string>();
            foreach (string raw in configured)
            {
                string dn = NormalizeDn(raw);
                try
                {
                    using (var e = Entry(dn))
                    {
                        e.RefreshCache(new[] { "distinguishedName" });
                        string canonical = Convert.ToString(e.Properties["distinguishedName"].Value);
                        if (!result.Contains(canonical, StringComparer.OrdinalIgnoreCase)) result.Add(canonical);
                    }
                }
                catch (Exception ex)
                {
                    ScopeWarnings.Add("[" + section + "] '" + raw + "' was not found or cannot be read: " + Friendly(ex));
                }
            }
            return result;
        }

        /// <summary>Adds every OU below each listed OU, so new users can be placed in sub-OUs.</summary>
        List<string> WithSubOUs(List<string> ous)
        {
            var all = new List<string>(ous);
            foreach (string ou in ous)
            {
                try
                {
                    using (var root = Entry(ou))
                    using (var s = new DirectorySearcher(root, "(objectClass=organizationalUnit)", new[] { "distinguishedName" }, SearchScope.Subtree))
                    {
                        s.PageSize = 500;
                        using (SearchResultCollection rc = s.FindAll())
                            foreach (SearchResult r in rc)
                            {
                                string dn = Str(r, "distinguishedName");
                                if (!all.Contains(dn, StringComparer.OrdinalIgnoreCase)) all.Add(dn);
                            }
                    }
                }
                catch (Exception ex) { ScopeWarnings.Add("Could not list sub-OUs of " + FriendlyPath(ou) + ": " + Friendly(ex)); }
            }
            return all;
        }

        void DetectScopes(out List<string> create, out List<string> reset)
        {
            create = new List<string>();
            reset = new List<string>();
            LoadUserDefaultRules();

            bool userDefaultGrantsReset = _userDefaultRules.Any(r => GrantsReset(r, true)) &&
                                          !_userDefaultRules.Any(r => r.AccessControlType == AccessControlType.Deny && GrantsReset(r, true));

            var withoutChildClasses = new List<string>();
            using (var root = Entry(DomainDn))
            using (var s = new DirectorySearcher(root))
            {
                s.Filter = "(|(objectClass=organizationalUnit)(distinguishedName=CN=Users," + DomainDn + "))";
                s.SearchScope = SearchScope.Subtree;
                s.PageSize = 500;
                s.SecurityMasks = SecurityMasks.Dacl;
                s.PropertiesToLoad.AddRange(new[] { "distinguishedName", "nTSecurityDescriptor", "allowedChildClassesEffective" });
                using (SearchResultCollection results = s.FindAll())
                {
                    foreach (SearchResult r in results)
                    {
                        string dn = Str(r, "distinguishedName");

                        if (r.Properties.Contains("allowedChildClassesEffective"))
                        {
                            foreach (object o in r.Properties["allowedChildClassesEffective"])
                                if (string.Equals(Convert.ToString(o), "user", StringComparison.OrdinalIgnoreCase))
                                { create.Add(dn); break; }
                        }
                        else withoutChildClasses.Add(dn);

                        if (r.Properties.Contains("nTSecurityDescriptor"))
                        {
                            var sec = new ActiveDirectorySecurity();
                            sec.SetSecurityDescriptorBinaryForm((byte[])r.Properties["nTSecurityDescriptor"][0], AccessControlSections.Access);
                            if (CanResetIn(sec, userDefaultGrantsReset)) reset.Add(dn);
                        }
                        else if (userDefaultGrantsReset) reset.Add(dn);
                    }
                }
            }

            // Some DCs only return constructed attributes on base-scope reads.
            foreach (string dn in withoutChildClasses)
            {
                try
                {
                    using (var e = Entry(dn))
                    {
                        e.RefreshCache(new[] { "allowedChildClassesEffective" });
                        foreach (object o in e.Properties["allowedChildClassesEffective"])
                            if (string.Equals(Convert.ToString(o), "user", StringComparison.OrdinalIgnoreCase))
                            { create.Add(dn); break; }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Reads the schema default security descriptor for user objects - this is where
        /// built-in rights such as Domain Admins / Account Operators come from.
        /// </summary>
        void LoadUserDefaultRules()
        {
            _userDefaultRules = new List<ActiveDirectoryAccessRule>();
            try
            {
                string schemaDn;
                using (var rootDse = new DirectoryEntry("LDAP://" + Dc + "/RootDSE", null, null, Auth))
                    schemaDn = Convert.ToString(rootDse.Properties["schemaNamingContext"].Value);
                using (var cls = Entry("CN=User," + schemaDn))
                {
                    cls.RefreshCache(new[] { "defaultSecurityDescriptor" });
                    string sddl = Convert.ToString(cls.Properties["defaultSecurityDescriptor"].Value);
                    var sec = new ActiveDirectorySecurity();
                    sec.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
                    foreach (ActiveDirectoryAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                        if (_sids.Contains(r.IdentityReference.Value)) _userDefaultRules.Add(r);
                }
            }
            catch (Exception ex)
            {
                ScopeWarnings.Add("Could not read the default user permissions from the schema: " + Friendly(ex));
            }
        }

        static bool GrantsReset(ActiveDirectoryAccessRule r, bool appliesToSelf)
        {
            ActiveDirectoryRights rights = r.ActiveDirectoryRights;
            if ((rights & ActiveDirectoryRights.GenericAll) == ActiveDirectoryRights.GenericAll) return true;
            return (rights & ActiveDirectoryRights.ExtendedRight) != 0 &&
                   (r.ObjectType == Guid.Empty || r.ObjectType == ResetPasswordRight);
        }

        bool CanResetIn(ActiveDirectorySecurity ouSec, bool defaultAllows)
        {
            bool allow = defaultAllows, deny = false;
            foreach (ActiveDirectoryAccessRule r in ouSec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (!_sids.Contains(r.IdentityReference.Value)) continue;
                if (r.InheritanceType == ActiveDirectorySecurityInheritance.None) continue;     // applies to the OU only
                if (r.InheritedObjectType != Guid.Empty && r.InheritedObjectType != UserClass) continue;
                if (!GrantsReset(r, false)) continue;
                if (r.AccessControlType == AccessControlType.Deny) deny = true; else allow = true;
            }
            return allow && !deny;
        }

        static List<string> Collapse(IEnumerable<string> dns)
        {
            var list = dns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return list.Where(dn => !list.Any(other => !other.Equals(dn, StringComparison.OrdinalIgnoreCase) && IsUnder(dn, other)))
                       .ToList();
        }

        public static bool IsUnder(string dn, string container)
        {
            if (string.IsNullOrEmpty(dn) || string.IsNullOrEmpty(container)) return false;
            return dn.Equals(container, StringComparison.OrdinalIgnoreCase) ||
                   dn.EndsWith("," + container, StringComparison.OrdinalIgnoreCase);
        }

        bool IsExcluded(string dn)
        {
            return ExcludeOUs.Any(x => IsUnder(dn, x));
        }

        public bool InResetScope(string dn)
        {
            return ResetRoots.Any(r => IsUnder(dn, r)) && !IsExcluded(dn);
        }

        public bool InCreateScope(string ouDn)
        {
            return CreateOUs.Any(o => o.Equals(ouDn, StringComparison.OrdinalIgnoreCase)) && !IsExcluded(ouDn);
        }

        // ------------------------------------------------------------------
        // Search (always restricted to the reset scope)
        // ------------------------------------------------------------------

        public List<AdUser> Search(string term, int max, out bool truncated)
        {
            truncated = false;
            term = (term ?? "").Trim();
            string filter;
            if (term.Length == 0 || term == "*")
                filter = "(&(objectCategory=person)(objectClass=user))";
            else
            {
                string e = EscapeFilter(term).Replace("\\2a", "*");   // allow user wildcards
                string contains = e.Contains("*") ? e : "*" + e + "*";
                filter = "(&(objectCategory=person)(objectClass=user)(|" +
                         "(sAMAccountName=" + contains + ")(displayName=" + contains + ")(cn=" + contains + ")" +
                         "(givenName=" + e + "*)(sn=" + e + "*)(userPrincipalName=" + e + "*)(mail=" + e + "*)))";
                if (term.Contains(" ") && !term.Contains("*"))
                {
                    // "jane smi" -> first name + surname
                    string[] p = term.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    filter = filter.Substring(0, filter.Length - 2) +
                             "(&(givenName=" + EscapeFilter(p[0]) + "*)(sn=" + EscapeFilter(p[1].Trim()) + "*))))";
                }
            }

            var found = new Dictionary<string, AdUser>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootDn in ResetRoots)
            {
                foreach (AdUser u in Query(rootDn, filter, SearchScope.Subtree, max + 1))
                {
                    if (!InResetScope(u.Dn)) continue;
                    found[u.Dn] = u;
                }
            }
            var list = found.Values.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count > max) { truncated = true; list = list.Take(max).ToList(); }
            return list;
        }

        /// <summary>Exact logon-name lookup, restricted to the reset scope.</summary>
        public AdUser FindInScope(string sam)
        {
            sam = (sam ?? "").Trim();
            if (sam.Contains("\\")) sam = sam.Substring(sam.LastIndexOf('\\') + 1);
            if (sam.Length == 0) return null;
            string filter = "(&(objectCategory=person)(objectClass=user)(|(sAMAccountName=" + EscapeFilter(sam) +
                            ")(userPrincipalName=" + EscapeFilter(sam) + ")))";
            foreach (string rootDn in ResetRoots)
                foreach (AdUser u in Query(rootDn, filter, SearchScope.Subtree, 2))
                    if (InResetScope(u.Dn)) return u;
            return null;
        }

        public AdUser Reload(string dn)
        {
            List<AdUser> l = Query(dn, "(objectClass=user)", SearchScope.Base, 1);
            return l.Count > 0 ? l[0] : null;
        }

        List<AdUser> Query(string rootDn, string filter, SearchScope scope, int sizeLimit)
        {
            var list = new List<AdUser>();
            using (var root = Entry(rootDn))
            using (var s = new DirectorySearcher(root))
            {
                s.Filter = filter;
                s.SearchScope = scope;
                s.PageSize = 250;
                s.SizeLimit = sizeLimit;
                s.PropertiesToLoad.AddRange(UserProps);
                using (SearchResultCollection rc = s.FindAll())
                    foreach (SearchResult r in rc) list.Add(FromResult(r));
            }
            return list;
        }

        static AdUser FromResult(SearchResult r)
        {
            var u = new AdUser();
            u.Dn = Str(r, "distinguishedName");
            u.Sam = Str(r, "sAMAccountName");
            u.DisplayName = Str(r, "displayName");
            u.GivenName = Str(r, "givenName");
            u.Surname = Str(r, "sn");
            u.Upn = Str(r, "userPrincipalName");
            u.Description = Str(r, "description");
            u.Mail = Str(r, "mail");
            u.Title = Str(r, "title");
            u.ProfilePath = Str(r, "profilePath");
            u.HomeDirectory = Str(r, "homeDirectory");
            u.HomeDrive = Str(r, "homeDrive");

            long uac = Lng(r, "userAccountControl");
            u.Enabled = (uac & 0x2) == 0;
            u.NeverExpires = (uac & 0x10000) != 0;
            if (r.Properties.Contains("msDS-User-Account-Control-Computed"))
                u.Locked = (Lng(r, "msDS-User-Account-Control-Computed") & 0x10) != 0;
            else
                u.Locked = Lng(r, "lockoutTime") > 0;

            long pls = Lng(r, "pwdLastSet");
            u.MustChange = r.Properties.Contains("pwdLastSet") && pls == 0;
            u.PwdLastSet = pls > 0 ? (DateTime?)DateTime.FromFileTime(pls) : null;
            long ll = Lng(r, "lastLogonTimestamp");
            u.LastLogon = ll > 0 ? (DateTime?)DateTime.FromFileTime(ll) : null;
            long ae = Lng(r, "accountExpires");
            if (ae > 0 && ae != long.MaxValue)
            {
                DateTime at = DateTime.FromFileTime(ae);              // moment the account stops working
                u.ExpiresEndOf = at.AddTicks(-1).Date;                 // ADUC shows this as "end of" the previous day
                u.Expired = at <= DateTime.Now;
            }
            if (r.Properties.Contains("whenCreated")) u.Created = ((DateTime)r.Properties["whenCreated"][0]).ToLocalTime();
            return u;
        }

        // ------------------------------------------------------------------
        // Reset password
        // ------------------------------------------------------------------

        public void ResetPassword(AdUser user, string password, bool mustChange, bool unlock)
        {
            if (!InResetScope(user.Dn))
                throw new ApplicationException(user.Name + " is outside the OUs you are allowed to manage.");

            using (DirectoryEntry e = Entry(user.Dn))
            {
                try
                {
                    e.Invoke("SetPassword", new object[] { password });
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("The password could not be set.\n\n" + Friendly(ex));
                }

                e.Properties["pwdLastSet"].Value = mustChange ? 0 : -1;
                if (unlock) e.Properties["lockoutTime"].Value = 0;
                try { e.CommitChanges(); }
                catch (Exception ex)
                {
                    throw new ApplicationException("The password WAS changed, but the 'change at next logon'" +
                                                   (unlock ? " / unlock" : "") + " setting could not be saved.\n\n" + Friendly(ex));
                }
            }
        }

        // ------------------------------------------------------------------
        // Enable / disable / expiry
        // ------------------------------------------------------------------

        public void SetEnabled(AdUser user, bool enabled)
        {
            if (!InResetScope(user.Dn))
                throw new ApplicationException(user.Name + " is outside the OUs you are allowed to manage.");
            using (DirectoryEntry e = Entry(user.Dn))
            {
                try
                {
                    e.RefreshCache(new[] { "userAccountControl" });
                    int uac = Convert.ToInt32(e.Properties["userAccountControl"].Value);
                    e.Properties["userAccountControl"].Value = enabled ? (uac & ~0x2) : (uac | 0x2);
                    e.CommitChanges();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("The account could not be " + (enabled ? "enabled" : "disabled") + ".\n\n" + Friendly(ex));
                }
            }
        }

        /// <summary>lastDay = final day the account works (expires at midnight that night); null = never.</summary>
        public void SetExpiry(AdUser user, DateTime? lastDay)
        {
            if (!InResetScope(user.Dn))
                throw new ApplicationException(user.Name + " is outside the OUs you are allowed to manage.");
            using (DirectoryEntry e = Entry(user.Dn))
            {
                try
                {
                    e.Properties["accountExpires"].Value = ExpiryValue(lastDay);
                    e.CommitChanges();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("The account expiry could not be changed.\n\n" + Friendly(ex));
                }
            }
        }

        static string ExpiryValue(DateTime? lastDay)
        {
            // ADSI converts the string to the Large Integer syntax; "0" means never expires.
            return lastDay.HasValue ? lastDay.Value.Date.AddDays(1).ToFileTime().ToString() : "0";
        }

        /// <summary>Attributes the signed-in user may write on this object (computed by the DC from real permissions).</summary>
        public HashSet<string> WritableAttributes(string dn)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (DirectoryEntry e = Entry(dn))
            {
                e.RefreshCache(new[] { "allowedAttributesEffective" });
                foreach (object o in e.Properties["allowedAttributesEffective"]) set.Add(Convert.ToString(o));
            }
            return set;
        }

        // ------------------------------------------------------------------
        // Create user
        // ------------------------------------------------------------------

        public bool SamExists(string sam)
        {
            return CountDomain("(sAMAccountName=" + EscapeFilter(sam) + ")") > 0;
        }

        public bool UpnExists(string upn)
        {
            return CountDomain("(userPrincipalName=" + EscapeFilter(upn) + ")") > 0;
        }

        int CountDomain(string filter)
        {
            using (var root = Entry(DomainDn))
            using (var s = new DirectorySearcher(root, filter, new[] { "distinguishedName" }, SearchScope.Subtree))
            {
                s.SizeLimit = 1;
                SearchResult r = s.FindOne();
                return r == null ? 0 : 1;
            }
        }

        /// <summary>
        /// First free name: the base name, then each alternative (other configured username formats),
        /// then the base name with 2, 3, 4... added.
        /// </summary>
        public string SuggestFreeSam(string baseSam, IEnumerable<string> alternatives)
        {
            if (string.IsNullOrEmpty(baseSam)) return baseSam;
            if (!SamExists(baseSam)) return baseSam;
            if (alternatives != null)
                foreach (string alt in alternatives)
                    if (!string.IsNullOrEmpty(alt) && !alt.Equals(baseSam, StringComparison.OrdinalIgnoreCase) && !SamExists(alt))
                        return alt;
            return SuggestFreeSam(baseSam);
        }

        public string SuggestFreeSam(string baseSam)
        {
            if (string.IsNullOrEmpty(baseSam)) return baseSam;
            if (!SamExists(baseSam)) return baseSam;
            for (int i = 2; i < 100; i++)
            {
                string suffix = i.ToString();
                string b = baseSam.Length + suffix.Length > 20 ? baseSam.Substring(0, 20 - suffix.Length) : baseSam;
                if (!SamExists(b + suffix)) return b + suffix;
            }
            return baseSam;
        }

        public CreateResult CreateUser(NewUserRequest q)
        {
            if (!InCreateScope(q.OuDn))
                throw new ApplicationException("You are not allowed to create users in " + FriendlyPath(q.OuDn) + ".");
            if (SamExists(q.Sam))
                throw new ApplicationException("The username '" + q.Sam + "' is already in use.");
            if (!string.IsNullOrEmpty(q.Upn) && UpnExists(q.Upn))
                throw new ApplicationException("The logon name '" + q.Upn + "' is already in use.");

            var result = new CreateResult { Sam = q.Sam };
            byte[] sidBytes = null;

            using (DirectoryEntry ou = Entry(q.OuDn))
            {
                string cn = q.DisplayName;
                if (ChildExists(q.OuDn, cn)) cn = q.DisplayName + " (" + q.Sam + ")";

                DirectoryEntry user = ou.Children.Add("CN=" + EscapeRdn(cn), "user");
                try
                {
                    user.Properties["sAMAccountName"].Value = q.Sam;
                    Set(user, "userPrincipalName", q.Upn);
                    Set(user, "givenName", q.FirstName);
                    Set(user, "sn", q.LastName);
                    Set(user, "displayName", q.DisplayName);
                    Set(user, "mail", q.Mail);
                    Set(user, "title", q.Title);
                    Set(user, "description", q.Description);
                    Set(user, "profilePath", q.ProfilePath);
                    Set(user, "homeDirectory", q.HomeDirectory);
                    if (!string.IsNullOrEmpty(q.HomeDirectory)) Set(user, "homeDrive", q.HomeDrive);
                    if (q.ExpiresEndOf.HasValue) user.Properties["accountExpires"].Value = ExpiryValue(q.ExpiresEndOf);
                    try { user.CommitChanges(); }
                    catch (Exception ex) { throw new ApplicationException("The account could not be created.\n\n" + Friendly(ex)); }

                    result.Dn = "CN=" + EscapeRdn(cn) + "," + q.OuDn;
                    result.Done.Add("Account created in " + FriendlyPath(q.OuDn));

                    try
                    {
                        user.Invoke("SetPassword", new object[] { q.Password });
                    }
                    catch (Exception ex)
                    {
                        string msg = "The password could not be set, so the new account was removed again.\n\n" + Friendly(ex);
                        try { ou.Children.Remove(user); ou.CommitChanges(); }
                        catch (Exception rex)
                        {
                            msg = "The password could not be set: " + Friendly(ex) +
                                  "\n\nThe account was created DISABLED and could not be removed automatically (" +
                                  Friendly(rex) + "). Delete or fix it in Active Directory Users and Computers.";
                        }
                        throw new ApplicationException(msg);
                    }

                    // 512 = normal account, 514 = normal + disabled
                    user.Properties["userAccountControl"].Value = q.Enabled ? 512 : 514;
                    user.Properties["pwdLastSet"].Value = q.MustChange ? 0 : -1;
                    try
                    {
                        user.CommitChanges();
                        result.Done.Add("Password set" + (q.MustChange ? " (must change at next logon)" : ""));
                        result.Done.Add(q.Enabled ? "Account enabled" : "Account left disabled");
                        if (q.ExpiresEndOf.HasValue) result.Done.Add("Account expires at the end of " + q.ExpiresEndOf.Value.ToString("dd/MM/yyyy"));
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add("Password set, but enabling the account failed - enable it manually. " + Friendly(ex));
                    }

                    user.RefreshCache(new[] { "objectSid", "distinguishedName" });
                    sidBytes = user.Properties["objectSid"].Value as byte[];
                    result.Dn = Convert.ToString(user.Properties["distinguishedName"].Value);
                }
                finally
                {
                    user.Dispose();
                }
            }

            foreach (string g in q.Groups)
            {
                try
                {
                    string groupDn = FindGroup(g);
                    if (groupDn == null) { result.Warnings.Add("Group '" + g + "' was not found."); continue; }
                    using (var ge = Entry(groupDn))
                    {
                        ge.Properties["member"].Add(result.Dn);
                        ge.CommitChanges();
                    }
                    result.Done.Add("Added to group " + g);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("Could not add to group '" + g + "': " + Friendly(ex));
                }
            }

            if (q.CreateHomeFolder && !string.IsNullOrEmpty(q.HomeDirectory) && sidBytes != null)
            {
                try
                {
                    var di = new DirectoryInfo(q.HomeDirectory);
                    bool existed = di.Exists;
                    if (!existed) di.Create();
                    DirectorySecurity sec = di.GetAccessControl(AccessControlSections.Access);
                    FileSystemRights rights = string.Equals(q.HomeFolderRights, "FullControl", StringComparison.OrdinalIgnoreCase)
                        ? FileSystemRights.FullControl : FileSystemRights.Modify;
                    sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sidBytes, 0), rights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                    di.SetAccessControl(sec);
                    result.Done.Add((existed ? "Home folder already existed - " : "Home folder created - ") +
                                    rights + " rights granted");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("Home folder " + q.HomeDirectory + " could not be created / secured: " + Friendly(ex));
                }
            }
            return result;
        }

        bool ChildExists(string ouDn, string cn)
        {
            using (var root = Entry(ouDn))
            using (var s = new DirectorySearcher(root, "(cn=" + EscapeFilter(cn) + ")", new[] { "cn" }, SearchScope.OneLevel))
                return s.FindOne() != null;
        }

        /// <summary>True when the OU / container exists and can be read.</summary>
        public bool ObjectExists(string dn)
        {
            try
            {
                using (var e = Entry(NormalizeDn(dn))) { e.RefreshCache(new[] { "distinguishedName" }); return true; }
            }
            catch { return false; }
        }

        /// <summary>DN of a group by name (sAMAccountName / CN) or DN; null when not found.</summary>
        public string FindGroup(string nameOrDn)
        {
            string f = nameOrDn.IndexOf("CN=", StringComparison.OrdinalIgnoreCase) >= 0 && nameOrDn.Contains(",")
                ? "(distinguishedName=" + EscapeFilter(NormalizeDn(nameOrDn)) + ")"
                : "(|(sAMAccountName=" + EscapeFilter(nameOrDn) + ")(cn=" + EscapeFilter(nameOrDn) + "))";
            using (var root = Entry(DomainDn))
            using (var s = new DirectorySearcher(root, "(&(objectClass=group)" + f + ")", new[] { "distinguishedName" }, SearchScope.Subtree))
            {
                SearchResult r = s.FindOne();
                return r == null ? null : Str(r, "distinguishedName");
            }
        }

        static void Set(DirectoryEntry e, string attr, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) e.Properties[attr].Value = value.Trim();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static string Str(SearchResult r, string p)
        {
            return r.Properties.Contains(p) && r.Properties[p].Count > 0 ? Convert.ToString(r.Properties[p][0]) : "";
        }

        static long Lng(SearchResult r, string p)
        {
            if (!r.Properties.Contains(p) || r.Properties[p].Count == 0) return 0;
            try { return Convert.ToInt64(r.Properties[p][0]); } catch { return 0; }
        }

        public static string EscapeFilter(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '\\': sb.Append("\\5c"); break;
                    case '*': sb.Append("\\2a"); break;
                    case '(': sb.Append("\\28"); break;
                    case ')': sb.Append("\\29"); break;
                    case '\0': sb.Append("\\00"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string EscapeRdn(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (",+\"\\<>;=/".IndexOf(c) >= 0 || (i == 0 && (c == '#' || c == ' ')) || (i == s.Length - 1 && c == ' '))
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Splits a DN into RDNs, honouring backslash escapes.</summary>
        public static List<string> SplitDn(string dn)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < (dn ?? "").Length; i++)
            {
                char c = dn[i];
                if (c == '\\' && i + 1 < dn.Length) { sb.Append(c).Append(dn[++i]); continue; }
                if (c == ',') { parts.Add(sb.ToString().Trim()); sb.Length = 0; continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) parts.Add(sb.ToString().Trim());
            return parts;
        }

        public static string ParentDn(string dn)
        {
            List<string> p = SplitDn(dn);
            return p.Count > 1 ? string.Join(",", p.Skip(1).ToArray()) : "";
        }

        /// <summary>OU=Teaching,OU=Users,OU=School,DC=x,DC=y  ->  School › Users › Teaching</summary>
        public static string FriendlyPath(string dn)
        {
            var names = new List<string>();
            foreach (string rdn in SplitDn(dn))
            {
                if (rdn.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)) break;
                int eq = rdn.IndexOf('=');
                names.Add(Unescape(eq >= 0 ? rdn.Substring(eq + 1) : rdn));
            }
            names.Reverse();
            return names.Count == 0 ? "(domain root)" : string.Join("  \u203A  ", names.ToArray());
        }

        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>Turns ADSI / COM errors into something a person can act on.</summary>
        public static string Friendly(Exception ex)
        {
            while ((ex is TargetInvocationException || ex is AggregateException) && ex.InnerException != null)
                ex = ex.InnerException;

            var com = ex as COMException;
            var dse = ex as DirectoryServicesCOMException;
            int code = com != null ? com.ErrorCode : (dse != null ? dse.ErrorCode : 0);
            string extended = dse != null && !string.IsNullOrEmpty(dse.ExtendedErrorMessage) ? dse.ExtendedErrorMessage : "";

            switch ((uint)code)
            {
                case 0x800708C5:
                    return "The password does not meet the domain's password policy (length, complexity or password history). " +
                           "Try a different password.";
                case 0x80070005:
                    return "Access denied - your account does not have permission to do this.";
                case 0x80072030:
                    return "The object was not found in Active Directory.";
                case 0x8007203A:
                    return "The domain controller is not available.";
                case 0x80071392:
                    return "An object with that name already exists.";
                case 0x80072035:
                    return "The server refused the request (it may break a directory rule or policy). " + extended;
            }
            if (extended.Contains("0000052D")) return "The password does not meet the domain's password policy.";
            if (extended.Contains("00000005")) return "Access denied - your account does not have permission to do this.";
            return ex.Message.Trim() + (extended.Length > 0 ? " (" + extended.Trim() + ")" : "");
        }
    }
}
