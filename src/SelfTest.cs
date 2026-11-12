using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Text;

namespace ADUserManager
{
    /// <summary>
    /// ADUserManager.exe /selftest report.txt                    - offline + read-only AD tests
    /// ADUserManager.exe /selftest report.txt /write "OU=Test,..." - also creates, resets and deletes a temporary user
    /// </summary>
    static class SelfTest
    {
        static StringBuilder _out = new StringBuilder();
        static int _pass, _fail;

        public static int Run(string[] args)
        {
            string report = args.Length > 1 ? args[1] : "selftest-report.txt";
            string writeOu = null;
            for (int i = 2; i < args.Length - 1; i++)
                if (args[i].Equals("/write", StringComparison.OrdinalIgnoreCase)) writeOu = args[i + 1];

            Line("AD User Manager self-test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line(new string('=', 70));
            try
            {
                Offline();
                Online(writeOu);
            }
            catch (Exception ex)
            {
                Check("Unhandled exception", false, ex.ToString());
            }
            Line(new string('=', 70));
            Line("PASSED " + _pass + "   FAILED " + _fail);
            File.WriteAllText(report, _out.ToString(), Encoding.UTF8);
            return _fail == 0 ? 0 : 1;
        }

        static void Line(string s) { _out.AppendLine(s); }

        static void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Line((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  ->  " + detail));
        }

        static void Section(string s) { Line(""); Line("--- " + s + " ---"); }

        // ------------------------------------------------------------------
        static void Offline()
        {
            Section("Config");
            AppConfig d = AppConfig.Parse(AppConfig.DefaultTemplate);
            Check("Default template parses without warnings", d.Warnings.Count == 0, string.Join("; ", d.Warnings.ToArray()));
            Check("Default password is Welcome2026!", d.DefaultPassword == "Welcome2026!", d.DefaultPassword);
            Check("DCServer/FileServer blank by default (auto) / flagged until detected", d.DCServer == "" && d.FileServer == "" &&
                  !d.ServerConfigured && d.MissingServerSettings().Count == 1 && d.MissingServerSettings()[0] == "FileServer",
                  string.Join(",", d.MissingServerSettings().ToArray()));
            string dfsP = d.Expand(d.ProfilePath, "jsmith", "", "", "paddock-sch.int", "");
            Check("Default profile path uses the DFS domain namespace (<domain>)", dfsP == @"\\paddock-sch.int\dfs\profiles$\roaming\jsmith", dfsP);
            var adP = new AdService { DomainDns = "paddock-sch.int" };
            AppConfig srv = AppConfig.Parse("[General]\nDCServer=psdc01\nFileServer=\\\\psfp01.paddock-sch.int");
            string g1 = adP.WithPlaceholders(@"\\Paddock-sch.int\dfs\profiles$\Roaming\<username>", srv);
            string g2 = adP.WithPlaceholders(@"\\PSDC01\dfs\profiles$\roaming\<username>", srv);
            string g3 = adP.WithPlaceholders(@"\\psfp01\ustaff\curric\<username>", srv);
            string g4 = adP.WithPlaceholders(@"\\other\x\<username>", srv);
            Check("Found paths shown with <domain>/<dcserver>/<fileserver>", g1 == @"\\<domain>\dfs\profiles$\Roaming\<username>" &&
                  g2 == @"\\<dcserver>\dfs\profiles$\roaming\<username>" && g3 == @"\\<fileserver>\ustaff\curric\<username>" &&
                  g4 == @"\\other\x\<username>", g1 + " | " + g2 + " | " + g3 + " | " + g4);
            Check("DFS domain namespace not mistaken for a file server", adP.IsDomainNamespace("PADDOCK-SCH.INT") && adP.IsDomainNamespace("paddock-sch") &&
                  !adP.IsDomainNamespace("psfp01"), "");
            Check("Old CHANGE-ME value treated as not set", !AppConfig.IsSet("CHANGE-ME") && AppConfig.Parse("[General]\nFileServer=CHANGE-ME").FileServerSource == "", "");
            Check("Path pattern from existing user", AdService.Pattern(@"\\fs01\Staff\Curric\JSmith", "jsmith") == @"\\fs01\Staff\Curric\<username>" &&
                  AdService.Pattern(@"\\fs01\x\other", "jsmith") == null, AdService.Pattern(@"\\fs01\Staff\Curric\JSmith", "jsmith"));
            Check("Server name from UNC", AdService.ServerOf(@"\\fs01\share\u") == "fs01" && AdService.ServerOf("H:\\x") == "", "");
            AppConfig two = AppConfig.Parse("[General]\nDCServer=\\\\psdc01\nFileServer=psfp01\n[Paths]\nProfilePath=\\\\<dcserver>\\dfs\\profiles$\\roaming\\<username>\n" +
                                            "[StaffType:Curriculum]\nHomePath=\\\\<fileserver>\\ustaff\\curric\\<username>\n");
            string twoP = two.Expand(two.ProfilePath, "jsmith", "", "", "", "");
            string twoH = two.Expand(two.UserTypes[0].HomePath, "jsmith", "", "", "", "");
            Check("Two servers: profile on DC, home on file server", two.ServerConfigured &&
                  twoP == @"\\psdc01\dfs\profiles$\roaming\jsmith" && twoH == @"\\psfp01\ustaff\curric\jsmith", twoP + " | " + twoH);
            AppConfig half = AppConfig.Parse("[General]\nFileServer=psfp01\n[StaffType:A]\nHomePath=\\\\<fileserver>\\x\\<username>\n[Paths]\nProfilePath=\\\\<dcserver>\\p\\<username>");
            Check("Unset DCServer flagged only when a path uses it", !half.ServerConfigured && half.MissingServerSettings().Count == 1 &&
                  half.MissingServerSettings()[0] == "DCServer", string.Join(",", half.MissingServerSettings().ToArray()));
            Check("Three user types (Curriculum, Admin, Student)", d.UserTypes.Count == 3 && d.UserTypes[0].Name == "Curriculum" && d.UserTypes[1].Name == "Admin" && d.UserTypes[2].Name == "Student",
                  string.Join(",", d.UserTypes.Select(t => t.Name).ToArray()));
            Check("Older [StaffType:..] sections still read", two.UserTypes.Count == 1 && two.UserTypes[0].Name == "Curriculum", "");
            Check("OU lists empty by default (detect from permissions)", d.CreateUserOUs.Count == 0 && d.ResetPasswordOUs.Count == 0 && d.DetectScopeFromPermissions, "");
            Check("Disable/expiry features on by default", d.AllowDisable && d.AllowExpiry, "");
            AppConfig tabs = AppConfig.Parse("[Features]\nShowResetPasswordTab=false\nShowCreateUserTab=true\nShowScopeTab=no");
            AppConfig none = AppConfig.Parse("[Features]\nShowResetPasswordTab=false\nShowCreateUserTab=false\nShowScopeTab=false");
            Check("Tabs shown by default; [Features] Show...Tab hides them", d.ShowResetTab && d.ShowCreateTab && d.ShowSettingsTab &&
                  !tabs.ShowResetTab && tabs.ShowCreateTab && !tabs.ShowSettingsTab && tabs.Warnings.Count == 0, string.Join(";", tabs.Warnings.ToArray()));
            Check("Hiding every tab keeps Scope & settings (with a warning)", !none.ShowResetTab && !none.ShowCreateTab && none.ShowSettingsTab &&
                  none.Warnings.Count == 1, string.Join(";", none.Warnings.ToArray()));
            AppConfig dtBad = AppConfig.Parse("[Features]\nDefaultTab=banana");
            Check("DefaultTab: Reset by default; Create/Scope/aliases parse; bad value warns", d.DefaultTab == "Reset" &&
                  AppConfig.Parse("[Features]\nDefaultTab=Create").DefaultTab == "Create" &&
                  AppConfig.Parse("[Features]\nDefaultTab=create user").DefaultTab == "Create" &&
                  AppConfig.Parse("[Features]\nDefaultTab=Scope").DefaultTab == "Scope" &&
                  AppConfig.Parse("[Features]\nDefaultTab=Settings").DefaultTab == "Scope" &&
                  AppConfig.Parse("[Features]\nDefaultTab=Reset password").DefaultTab == "Reset" &&
                  dtBad.DefaultTab == "Reset" && dtBad.Warnings.Count == 1, string.Join(";", dtBad.Warnings.ToArray()));
            Check("[Features] switches parse", !AppConfig.Parse("[Features]\nAllowDisable=false\nAllowExpiry=no").AllowDisable && !AppConfig.Parse("[Features]\nAllowExpiry=no").AllowExpiry, "");
            Check("Home drive H:", d.HomeDriveFor(d.UserTypes[0]) == "H:", d.HomeDriveFor(d.UserTypes[0]));

            string tmp = Path.Combine(Path.GetTempPath(), "admgr-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                AppConfig c1 = AppConfig.LoadOrCreate(tmp);
                Check("Config file created when missing", c1.CreatedNew && File.Exists(Path.Combine(tmp, AppConfig.FileName)), c1.FilePath);
                AppConfig c2 = AppConfig.LoadOrCreate(tmp);
                Check("Existing config file re-used (not overwritten)", !c2.CreatedNew, "");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            AppConfig c = AppConfig.Parse(
                "[General]\nServer=psfp01\n[CreateUserOUs]\nOU=Teaching,OU=Users,OU=School\nOU=Staff,DC=x,DC=y\n" +
                "[ResetPasswordOUs]\nOU1=OU=Users,OU=School\n[ExcludeOUs]\nOU=Leavers,OU=Users,OU=School\n" +
                "[UserType:Curriculum]\nHomePath=\\\\<server>\\ustaff\\curric\\<username>\nGroups=Staff; Teachers ;\n" +
                "[UserType:Admin]\nHomePath=\\\\<server>\\ustaff\\admin\\<username>\nHomeDrive=u\n");
            Check("List sections parsed (DNs with '=' and ',')", c.CreateUserOUs.Count == 2 && c.CreateUserOUs[0] == "OU=Teaching,OU=Users,OU=School", string.Join(" | ", c.CreateUserOUs.ToArray()));
            Check("OU1= style key accepted", c.ResetPasswordOUs.Count == 1 && c.ResetPasswordOUs[0] == "OU=Users,OU=School", c.ResetPasswordOUs.FirstOrDefault());
            Check("Exclude list parsed", c.ExcludeOUs.Count == 1, "");
            Check("Groups split on ;", c.UserTypes[0].Groups.Count == 2 && c.UserTypes[0].Groups[1] == "Teachers", string.Join(",", c.UserTypes[0].Groups.ToArray()));
            Check("Per-type HomeDrive override normalised", c.HomeDriveFor(c.UserTypes[1]) == "U:", c.HomeDriveFor(c.UserTypes[1]));

            Section("Placeholders");
            string prof = c.Expand(@"\\<dcserver>\dfs\profiles$\roaming\<username>", "jsmith", "Jane", "Smith", "school.local", "Curriculum");
            Check("Old single Server= still works for both placeholders", prof == @"\\psfp01\dfs\profiles$\roaming\jsmith", prof);
            string hc = c.Expand(c.UserTypes[0].HomePath, "jsmith", "Jane", "Smith", "school.local", "Curriculum");
            Check("Curriculum home path", hc == @"\\psfp01\ustaff\curric\jsmith", hc);
            string ha = c.Expand(c.UserTypes[1].HomePath, "jsmith", "Jane", "Smith", "school.local", "Admin");
            Check("Admin home path", ha == @"\\psfp01\ustaff\admin\jsmith", ha);
            Check("Case-insensitive placeholders + <domain>", c.Expand("<SERVER>|<Domain>|<unknown>", "u", "", "", "d.local", "") == "psfp01|d.local|<unknown>", "");
            Check("Username jane / o'brien-smith", c.BuildUsername("Jane", "O'Brien-Smith") == "jobriensmith", c.BuildUsername("Jane", "O'Brien-Smith"));
            Check("Username strips accents", c.BuildUsername("Zo\u00EB", "Ren\u00E9e") == "zrenee", c.BuildUsername("Zo\u00EB", "Ren\u00E9e"));
            c.UsernameFormat = "<firstname>.<lastname>";
            Check("Username max 20 chars", c.BuildUsername("Alexandria", "Montgomery-Wellington").Length <= 20, c.BuildUsername("Alexandria", "Montgomery-Wellington"));

            Section("Username formats / user types");
            AppConfig u = AppConfig.Parse(
                "[NewUsers]\nUsernameFormat=<firstinitial><lastname>; <firstname>.<lastname>; <lastname:4><firstinitial>\n" +
                "UpnSuffix=@school.org.uk\nGroups=All Users\nGroups=Wi-Fi; All Users\n" +
                "[UserType:Curriculum]\nGroups=CurricStaff; Teachers\n" +
                "[UserType:Student]\nUsernameFormat=<yy><firstinitial><lastname>\nUpnSuffix=students.school.org.uk\n" +
                "DefaultOU=OU=Intake<yyyy>,OU=Students\nGroups=Students\nGroup=SSOStudents\nProfilePath=none\nEmailFormat=<username>@students.school.org.uk\n" +
                "[UserType:Governor]\nEnabled=false\n");
            u.Today = new DateTime(2026, 11, 12);
            UserType cur = u.UserTypes[0], stu = u.UserTypes[1];
            Check("Format list parsed, suggestion is the first", u.BuildUsername("Jane", "Smith", cur) == "jsmith", u.BuildUsername("Jane", "Smith", cur));
            List<string> opts = u.BuildUsernameOptions("Jane", "Smith", cur);
            Check("Alternatives from every format", opts.Count == 3 && opts[1] == "jane.smith" && opts[2] == "smitj", string.Join(",", opts.ToArray()));
            Check("Per-type username format + <yy>", u.BuildUsername("Jane", "Smith", stu) == "26jsmith", u.BuildUsername("Jane", "Smith", stu));
            Check("<lastname:N> truncation", u.Expand("<lastname:3>|<firstname:10>", "", "Jane", "Smith", "", "") == "Smi|Jane", u.Expand("<lastname:3>|<firstname:10>", "", "Jane", "Smith", "", ""));
            Check("Empty part does not leave double dots", u.BuildUsernameFrom("<firstname>.<lastname>", "", "Smith", null) == "smith", u.BuildUsernameFrom("<firstname>.<lastname>", "", "Smith", null));
            string[] presets = { "<firstinitial><lastname>", "<firstname>.<lastname>", "<firstname><lastname>", "<firstinitial>.<lastname>", "<firstname>_<lastname>",
                                 "<firstname>-<lastname>", "<lastname><firstinitial>", "<lastname>.<firstname>", "<firstname><lastinitial>", "<firstname>",
                                 "<lastname:4><firstinitial>", "<firstname:3><lastname:3>", "<yy><firstinitial><lastname>", "<lastname><firstinitial><yy>", "<yyyy><lastname><firstinitial>" };
            string[] expect = { "jsmith", "jane.smith", "janesmith", "j.smith", "jane_smith", "jane-smith", "smithj", "smith.jane", "janes", "jane",
                                "smitj", "jansmi", "26jsmith", "smithj26", "2026smithj" };
            var badPresets = new List<string>();
            for (int i = 0; i < presets.Length; i++)
                if (u.BuildUsernameFrom(presets[i], "Jane", "Smith", null) != expect[i]) badPresets.Add(presets[i] + "=" + u.BuildUsernameFrom(presets[i], "Jane", "Smith", null));
            Check("Every format listed in the template gives the documented result", badPresets.Count == 0, string.Join(", ", badPresets.ToArray()));
            Check("Global UPN suffix (leading @ removed)", u.UpnSuffixFor(cur, "x.local") == "school.org.uk", u.UpnSuffixFor(cur, "x.local"));
            Check("Per-type UPN suffix", u.UpnSuffixFor(stu, "x.local") == "students.school.org.uk", u.UpnSuffixFor(stu, "x.local"));
            Check("Blank UPN suffix falls back to the domain", d.UpnSuffixFor(null, "x.local") == "x.local", "");
            Check("Common groups (repeated lines, no duplicates)", u.CommonGroups.Count == 2, string.Join(",", u.CommonGroups.ToArray()));
            List<string> sg = u.GroupsFor(stu);
            Check("Type groups = common + Groups= + Group= lines", sg.Count == 4 && sg.Contains("Students") && sg.Contains("SSOStudents") && sg.Contains("Wi-Fi"), string.Join(",", sg.ToArray()));
            Check("DefaultOU placeholder", u.Expand(stu.DefaultOU, "", "", "", "", stu.Name) == "OU=Intake2026,OU=Students", u.Expand(stu.DefaultOU, "", "", "", "", stu.Name));
            Check("ProfilePath=none means no profile", u.ProfilePathFor(stu) == "" && u.ProfilePathFor(cur) == u.ProfilePath, "");
            Check("Per-type email format", u.EmailFormatFor(stu).StartsWith("<username>@students") && u.EmailFormatFor(cur) == "", "");
            Check("Enabled=false type hidden from the app", u.UserTypes.Count == 3 && u.ActiveUserTypes.Count == 2, "");
            Check("<usertype> and old <stafftype> placeholder", u.Expand("<usertype>/<stafftype>", "", "", "", "", "Student") == "Student/Student", "");

            Section("Passwords");
            bool allOk = true; string bad = "";
            for (int i = 0; i < 2000; i++)
            {
                string p1 = PasswordTools.Generate("Memorable", 12), p2 = PasswordTools.Generate("Random", 12);
                if (PasswordTools.Validate(p1, 12, true, "jsmith", "Jane Smith") != null && !p1.ToLower().Contains("jane")) { allOk = false; bad = p1; }
                if (PasswordTools.Validate(p2, 12, true, "jsmith", "Jane Smith") != null) { allOk = false; bad = p2; }
            }
            Check("4000 generated passwords meet length 12 + complexity", allOk, bad);
            Check("Sample memorable / random", true, PasswordTools.Generate("Memorable", 12) + "   " + PasswordTools.Generate("Random", 12));
            Check("Default password meets typical policy", PasswordTools.Validate("Welcome2026!", 8, true, "jsmith", "Jane Smith") == null, "");
            Check("Rejects short", PasswordTools.Validate("Ab1!", 8, true, null, null) != null, PasswordTools.Validate("Ab1!", 8, true, null, null));
            Check("Rejects low complexity", PasswordTools.Validate("password123", 8, true, null, null) != null, PasswordTools.Validate("password123", 8, true, null, null));
            Check("Rejects containing username", PasswordTools.Validate("Xjsmith2026!", 8, true, "jsmith", null) != null, "");
            Check("Rejects containing name part", PasswordTools.Validate("Smith2026!x", 8, true, "js", "Jane Smith") != null, "");
            Check("Complexity off accepts simple", PasswordTools.Validate("password", 8, false, null, null) == null, "");

            Section("Audit log");
            string ltmp = Path.Combine(Path.GetTempPath(), "admgr-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ltmp);
            try
            {
                var log = new AuditLog(AppConfig.Parse(AppConfig.DefaultTemplate), ltmp);
                log.OperatorLogon = "DOMAIN\\admin1"; log.OperatorName = "Admin One";
                var target = new AdUser { Sam = "louise.smith", DisplayName = "Louise Smith", Dn = "CN=Louise Smith,OU=Teaching,OU=Users,OU=School,DC=x,DC=y" };
                log.Write("Reset password", target, "default password; must change at next logon=Yes", "Success");
                log.Write("Create user", "jdoe", "Doe, John", "School > Staff", "=HYPERLINK(\"x\")", "FAILED: \"quoted\"");
                Check("Log file created in app folder", log.Path == Path.Combine(ltmp, "ADUserManager-AuditLog.csv") && File.Exists(log.Path), log.Path);
                string[] lines = File.ReadAllLines(log.Path);
                Check("Header + 2 rows", lines.Length == 3, lines.Length + " lines");
                Check("Header columns", lines[0].StartsWith("Date,Time,Operator,Operator name,Computer,Action,Target user"), lines[0]);
                Check("Row records operator, action, target user, OU, details", lines.Length > 1 && lines[1].Contains("DOMAIN\\admin1,Admin One,") &&
                      lines[1].Contains(",Reset password,louise.smith,Louise Smith,School  ›  Users  ›  Teaching,default password"), lines.Length > 1 ? lines[1] : "");
                Check("Commas/quotes escaped, Excel formulas neutralised", lines.Length > 2 && lines[2].Contains("\"Doe, John\"") &&
                      lines[2].Contains("'=HYPERLINK(\"\"x\"\")") && lines[2].Contains("\"FAILED: \"\"quoted\"\"\""), lines.Length > 2 ? lines[2] : "");
                Check("No password text in log", !File.ReadAllText(log.Path).Contains("Welcome2026!"), "");
            }
            finally { try { Directory.Delete(ltmp, true); } catch { } }

            Section("LDAP helpers");
            Check("Filter escaping", AdService.EscapeFilter("a*(b)\\") == "a\\2a\\28b\\29\\5c", AdService.EscapeFilter("a*(b)\\"));
            Check("RDN escaping", AdService.EscapeRdn("Smith, Jane") == "Smith\\, Jane", AdService.EscapeRdn("Smith, Jane"));
            Check("SplitDn honours escapes", AdService.SplitDn("CN=Smith\\, Jane,OU=Staff,DC=x").Count == 3, "");
            Check("FriendlyPath", AdService.FriendlyPath("OU=Teaching,OU=Users,OU=School,DC=x,DC=y") == "School  \u203A  Users  \u203A  Teaching", AdService.FriendlyPath("OU=Teaching,OU=Users,OU=School,DC=x,DC=y"));
            Check("IsUnder positive", AdService.IsUnder("CN=a,OU=Staff,DC=x", "OU=Staff,DC=x"), "");
            Check("IsUnder rejects look-alike OU", !AdService.IsUnder("CN=a,OU=XStaff,DC=x", "OU=Staff,DC=x"), "");
        }

        // ------------------------------------------------------------------
        static void Online(string writeOu)
        {
            Section("Active Directory (read-only)");
            AppConfig cfg = AppConfig.Parse(AppConfig.DefaultTemplate);
            var ad = new AdService();
            try { ad.Connect(cfg); }
            catch (Exception ex) { Check("Connect to domain", false, AdService.Friendly(ex)); return; }
            Check("Connect to domain", ad.Connected, ad.DomainDns + " via " + ad.Dc);
            Check("Seamless identity", ad.UserSam.Length > 0, ad.UserDisplayName + " (" + ad.LogonName + ")  " + ad.UserDn);
            Check("Read password policy", ad.MinPwdLength >= 0, "min " + ad.MinPwdLength + ", complexity " + ad.Complexity);
            Check("Read forest UPN suffixes", ad.UpnSuffixesRead && ad.IsKnownUpnSuffix(ad.DomainDns), string.Join(", ", ad.UpnSuffixes.ToArray()));

            DateTime t0 = DateTime.Now;
            ad.ResolveScopes(cfg, null);
            Check("Scope detected from permissions", ad.ResetScopeSource.StartsWith("detected") && ad.CreateScopeSource.StartsWith("detected"),
                  "reset roots " + ad.ResetRoots.Count + ", create OUs " + ad.CreateOUs.Count + " in " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms");
            foreach (string r in ad.ResetRoots.Take(10)) Line("        reset root: " + AdService.FriendlyPath(r));
            foreach (string w in ad.ScopeWarnings) Line("        warning: " + w);
            Check("Reset roots are collapsed (no root inside another)",
                  !ad.ResetRoots.Any(a => ad.ResetRoots.Any(b => a != b && AdService.IsUnder(a, b))), "");

            bool trunc;
            List<AdUser> all = ad.Search("", 5000, out trunc);
            Check("List all users in scope", all.Count > 0, all.Count + " users");
            Check("Every result inside scope", all.All(u => ad.InResetScope(u.Dn)), "");
            if (all.Count == 0) return;

            AdUser meUser = ad.FindInScope(ad.UserSam);
            Line("        current user in detected scope: " + (meUser != null));

            List<AdUser> byName = ad.Search(all[0].Sam, 50, out trunc);
            Check("Search by username finds it", byName.Any(u => u.Dn == all[0].Dn), all[0].Sam);
            AdUser exact = ad.FindInScope(all[0].Sam);
            Check("Direct username lookup", exact != null && exact.Dn == all[0].Dn, all[0].Sam);
            Check("Wildcard search", ad.Search(all[0].Sam.Substring(0, Math.Min(2, all[0].Sam.Length)) + "*", 500, out trunc).Count > 0, "");
            Check("LDAP injection is neutralised", ad.Search("*)(objectClass=*", 50, out trunc).Count == 0, "");

            // --- Restricted scope: pick an OU that holds users but not the signed-in user.
            string ou = all.Select(u => AdService.ParentDn(u.Dn))
                           .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) && !AdService.IsUnder(ad.UserDn, p))
                           .GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (ou != null)
            {
                Section("Scope enforcement (config restricted to " + AdService.FriendlyPath(ou) + ")");
                AppConfig rc = AppConfig.Parse(AppConfig.DefaultTemplate);
                rc.ResetPasswordOUs.Add(ou);
                rc.CreateUserOUs.Add(ou);
                var ad2 = new AdService();
                ad2.Connect(rc);
                ad2.ResolveScopes(rc, null);
                Check("Config scope used instead of detection", ad2.ResetScopeSource == "config file" && ad2.ResetRoots.Count == 1, ad2.ResetScopeSource);
                List<AdUser> inScope = ad2.Search("", 5000, out trunc);
                Check("List all returns only users in the configured OU", inScope.Count > 0 && inScope.All(u => AdService.IsUnder(u.Dn, ou)), inScope.Count + " users");
                AdUser outsider = all.FirstOrDefault(u => !AdService.IsUnder(u.Dn, ou));
                if (outsider != null)
                {
                    Check("Outside user NOT found by direct username", ad2.FindInScope(outsider.Sam) == null, outsider.Sam + " in " + outsider.Location);
                    Check("Outside user NOT found by search", !ad2.Search(outsider.Sam, 50, out trunc).Any(u => u.Dn == outsider.Dn), outsider.Sam);
                    bool refused = false;
                    try { ad2.ResetPassword(outsider, "Xx-never-used-1!", true, false); } catch (ApplicationException) { refused = true; }
                    Check("Reset of outside user refused before contacting AD", refused, "");
                }
                Check("Signed-in user NOT found (outside configured OU)", ad2.FindInScope(ad.UserSam) == null, ad.UserSam);
                Check("Create OU list includes the configured OU", ad2.InCreateScope(ou), ad2.CreateOUs.Count + " OU(s)");
                Check("Create outside the create OUs refused", !ad2.InCreateScope(AdService.ParentDn(ad.UserDn)), "");

                string parent = AdService.ParentDn(ou);
                var ec = AppConfig.Parse(AppConfig.DefaultTemplate);
                ec.ResetPasswordOUs.Add(parent);
                ec.ExcludeOUs.Add(ou);
                var ad3 = new AdService();
                ad3.Connect(ec);
                ad3.ResolveScopes(ec, null);
                List<AdUser> ex3 = ad3.Search("", 5000, out trunc);
                Check("ExcludeOUs hides users in excluded OU", !ex3.Any(u => AdService.IsUnder(u.Dn, ou)), ex3.Count + " users under " + AdService.FriendlyPath(parent));

                var bc = AppConfig.Parse(AppConfig.DefaultTemplate);
                bc.ResetPasswordOUs.Add("OU=DoesNotExist-12345");
                bc.DetectScopeFromPermissions = false;
                var ad4 = new AdService();
                ad4.Connect(bc);
                ad4.ResolveScopes(bc, null);
                Check("Missing OU reported as warning, nothing searchable", ad4.ResetRoots.Count == 0 && ad4.ScopeWarnings.Count > 0 && ad4.Search("", 10, out trunc).Count == 0,
                      ad4.ScopeWarnings.FirstOrDefault());
            }

            Section("Account status / expiry (read-only)");
            HashSet<string> rights = ad.WritableAttributes(all[0].Dn);
            Check("Effective write permissions read from DC", rights.Count > 0, all[0].Sam + ": userAccountControl=" + rights.Contains("userAccountControl") + ", accountExpires=" + rights.Contains("accountExpires"));
            int withExpiry = all.Count(u => u.ExpiresEndOf.HasValue), expired = all.Count(u => u.Expired), disabled = all.Count(u => !u.Enabled);
            Check("Expiry / disabled status read for users", true, withExpiry + " with an expiry date, " + expired + " expired, " + disabled + " disabled");
            AdUser sample = all.FirstOrDefault(u => u.ExpiresEndOf.HasValue);
            if (sample != null) Line("        e.g. " + sample.Sam + " expires end of " + sample.ExpiresEndOf.Value.ToString("dd/MM/yyyy"));
            bool refusedOutside = false;
            var adR = new AdService(); adR.Connect(cfg);   // not resolved = empty scope
            try { adR.SetEnabled(all[0], false); } catch (ApplicationException) { refusedOutside = true; }
            bool refusedExp = false;
            try { adR.SetExpiry(all[0], DateTime.Today); } catch (ApplicationException) { refusedExp = true; }
            Check("Disable / expiry refused outside scope (no AD change)", refusedOutside && refusedExp, "");

            string myIni = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), AppConfig.FileName);
            if (File.Exists(myIni))
            {
                Section("Your config file: " + myIni);
                AppConfig mine = AppConfig.Parse(File.ReadAllText(myIni));
                foreach (string w in mine.Warnings) Line("        config warning: " + w);
                var adM = new AdService();
                adM.Connect(mine);
                adM.ResolveScopes(mine, null);
                foreach (string w in adM.ScopeWarnings) Line("        scope warning: " + w);
                Check("Reset/search scope", adM.ResetRoots.Count > 0, adM.ResetScopeSource + ": " + string.Join(" | ", adM.ResetRoots.Select(AdService.FriendlyPath).ToArray()));
                Check("Users searchable in scope", adM.Search("", 5000, out trunc).Count > 0, adM.Search("", 5000, out trunc).Count + " users");
                Check("Create OUs (incl. sub-OUs)", adM.CreateOUs.Count > 0, adM.CreateOUs.Count + ": " + string.Join(" | ", adM.CreateOUs.Select(AdService.FriendlyPath).Take(12).ToArray()));
                Check("Signed-in user hidden unless inside scope", adM.FindInScope(adM.UserSam) == null || adM.InResetScope(adM.UserDn), "");
                adM.DiscoverServers(mine);
                Check("Servers resolved (config or auto)", mine.ServerConfigured,
                      "DC=" + mine.DCServer + " (" + mine.DCServerSource + "), files=" + mine.FileServer + " (" + mine.FileServerSource + ")");
                foreach (UserType t in mine.ActiveUserTypes)
                    Line("        " + t.Name + " home: " + mine.Expand(t.HomePath, "jsmith", "Jane", "Smith", adM.DomainDns, t.Name));
                Line("        profile: " + mine.Expand(mine.ProfilePath, "jsmith", "Jane", "Smith", adM.DomainDns, ""));

                // Same config with the server names blanked - as if the app were dropped on a new site.
                string blankText = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(myIni),
                    @"(?im)^\s*(DCServer|FileServer|Server)\s*=.*$", "$1=");
                AppConfig fresh = AppConfig.Parse(blankText);
                var adF = new AdService();
                adF.Connect(fresh);
                adF.ResolveScopes(fresh, null);
                adF.DiscoverServers(fresh);
                Check("Blank DCServer/FileServer auto-detected", fresh.ServerConfigured,
                      "DC=" + fresh.DCServer + " (" + fresh.DCServerSource + "), files=" + fresh.FileServer + " (" + fresh.FileServerSource + ")");
                Line("        " + adF.PatternSample + " users checked; top home patterns: " +
                     string.Join(" | ", adF.HomePatterns.Take(3).Select(kv => kv.Key + " x" + kv.Value).ToArray()));
                Line("        top profile patterns: " +
                     string.Join(" | ", adF.ProfilePatterns.Take(3).Select(kv => kv.Key + " x" + kv.Value).ToArray()));

                Section("Requirements check (your config file)");
                List<RequirementCheck> req = Requirements.Run(mine, adM, Path.GetDirectoryName(myIni));
                foreach (RequirementCheck rc2 in req)
                    Line("        [" + rc2.State.ToString().ToUpperInvariant().PadRight(4) + "] " + rc2.Group + " / " + rc2.Name + "  -  " + rc2.Detail);
                Check("Requirements check ran", req.Count > 5, req.Count(x => x.State == CheckState.Fail) + " fail, " + req.Count(x => x.State == CheckState.Warn) + " warn");
            }

            Section("Username availability");
            Check("Own username reported taken", ad.SamExists(ad.UserSam), ad.UserSam);
            Check("Random username reported free", !ad.SamExists("zz" + Guid.NewGuid().ToString("N").Substring(0, 12)), "");
            Check("Free-name suggestion", ad.SuggestFreeSam(ad.UserSam) != ad.UserSam, ad.SuggestFreeSam(ad.UserSam));

            if (writeOu != null) WriteTests(ad, cfg, writeOu);
            else { Section("Write tests"); Line("        skipped (run with /write \"<OU DN>\" to create, reset and delete a temporary user)"); }
        }

        static void WriteTests(AdService ad, AppConfig cfg, string ou)
        {
            Section("Write tests in " + ou);
            string dn = ad.NormalizeDn(ou);
            cfg.CreateUserOUs.Clear(); cfg.CreateUserOUs.Add(dn);
            cfg.ResetPasswordOUs.Clear(); cfg.ResetPasswordOUs.Add(dn);
            ad.ResolveScopes(cfg, null);
            if (!ad.InCreateScope(dn)) { Check("Test OU accepted", false, "OU not found: " + dn + " " + string.Join(";", ad.ScopeWarnings.ToArray())); return; }

            string sam = "zzadmtest" + new Random().Next(1000, 9999);
            var q = new NewUserRequest
            {
                OuDn = ad.CreateOUs[0], FirstName = "Zz Test", LastName = "AdUserManager", DisplayName = "Zz Test AdUserManager " + sam,
                Sam = sam, Upn = sam + "@" + ad.DomainDns, Description = "Temporary self-test account - safe to delete",
                ProfilePath = cfg.Expand(@"\\testserver\dfs\profiles$\roaming\<username>", sam, "", "", ad.DomainDns, ""),
                HomeDirectory = cfg.Expand(@"\\testserver\ustaff\curric\<username>", sam, "", "", ad.DomainDns, ""), HomeDrive = "H:",
                Password = PasswordTools.Generate("Memorable", 14), MustChange = true, Enabled = true, CreateHomeFolder = false,
                ExpiresEndOf = DateTime.Today.AddDays(60)
            };
            CreateResult res = null;
            try
            {
                res = ad.CreateUser(q);
                Check("Create user", res.Dn != null, res.Dn + "  " + string.Join("; ", res.Done.ToArray()) + "  " + string.Join("; ", res.Warnings.ToArray()));
                AdUser u = ad.FindInScope(sam);
                Check("New user is searchable", u != null, "");
                if (u != null)
                {
                    Check("Enabled + must change", u.Enabled && u.MustChange, "enabled=" + u.Enabled + " mustChange=" + u.MustChange);
                    Check("Profile/home attributes set", u.ProfilePath == q.ProfilePath && u.HomeDirectory == q.HomeDirectory && u.HomeDrive == "H:", u.ProfilePath + " | " + u.HomeDrive + " " + u.HomeDirectory);
                    Check("UPN set", u.Upn == q.Upn, u.Upn);
                    Check("Created with expiry date", u.ExpiresEndOf == q.ExpiresEndOf, "end of " + u.ExpiresEndOf);

                    ad.ResetPassword(u, PasswordTools.Generate("Random", 14), false, false);
                    AdUser u2 = ad.Reload(u.Dn);
                    Check("Reset without forced change clears flag", !u2.MustChange && u2.PwdLastSet.HasValue, "pwdLastSet " + u2.PwdLastSet);

                    ad.ResetPassword(u2, cfg.DefaultPassword, true, true);
                    AdUser u3 = ad.Reload(u.Dn);
                    Check("Reset to default password forces change", u3.MustChange, "");

                    ad.SetEnabled(u3, false);
                    Check("Disable account", !ad.Reload(u.Dn).Enabled, "");
                    ad.SetEnabled(u3, true);
                    Check("Enable account", ad.Reload(u.Dn).Enabled, "");
                    DateTime last = DateTime.Today.AddDays(30);
                    ad.SetExpiry(u3, last);
                    AdUser u4 = ad.Reload(u.Dn);
                    Check("Set account expiry", u4.ExpiresEndOf == last && !u4.Expired, "end of " + u4.ExpiresEndOf);
                    ad.SetExpiry(u3, DateTime.Today.AddDays(-2));
                    Check("Past expiry shows as expired", ad.Reload(u.Dn).Expired, "");
                    ad.SetExpiry(u3, null);
                    AdUser u5 = ad.Reload(u.Dn);
                    Check("Clear expiry (never)", !u5.ExpiresEndOf.HasValue && !u5.Expired, "");

                    bool policyMsg = false;
                    try { ad.ResetPassword(u3, "a", true, false); }
                    catch (ApplicationException ex) { policyMsg = ex.Message.Contains("policy"); Line("        policy error text: " + ex.Message.Replace("\n", " ")); }
                    Check("Weak password rejected with friendly message", policyMsg, "");
                }
                bool dup = false;
                try { ad.CreateUser(q); } catch (ApplicationException) { dup = true; }
                Check("Duplicate username refused", dup, "");
            }
            catch (Exception ex)
            {
                Check("Write tests", false, AdService.Friendly(ex));
            }
            finally
            {
                if (res != null && res.Dn != null)
                {
                    try
                    {
                        using (DirectoryEntry e = ad.Entry(res.Dn)) e.DeleteTree();
                        Check("Temporary user deleted", ad.FindInScope(sam) == null, sam);
                    }
                    catch (Exception ex) { Check("Temporary user deleted", false, "DELETE " + res.Dn + " MANUALLY: " + AdService.Friendly(ex)); }
                }
            }
        }
    }
}
