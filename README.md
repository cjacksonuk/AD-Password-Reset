# AD User Manager

Single-file Windows app to **reset AD passwords** and **create users** (staff and students), limited to the OUs in its config file.
Developed by support@icthero.co.uk, 11/11/26.

## Deploy
Copy `dist\ADUserManager.exe` to any folder (local or a network share) on a domain-joined Windows 10/11 PC and run it.
It needs no installer, no RSAT and no admin rights. It uses .NET Framework 4.8, which is built into Windows.

When it first runs, it creates `ADUserManager.ini` in the same folder from the built-in template. Edit that file, then click **Scope & settings → Reload config**.

**Requirements & dependencies:** the **Scope & settings** page runs a check every time the app connects. You can also run it with **Check requirements**. It tests:
* this PC: Windows version, .NET 4.8, domain join, whether the app folder is writable
* the domain: DC connection, LDAP/Kerberos ports, and the OUs you can search and create in
* everything the config names: the UPN suffix is registered in the forest, every group exists, each DefaultOU exists and is in scope, and every home/profile share can be reached

Anything missing gets a fix. **Install .NET 4.8** appears only if .NET is too old. **Install RSAT (optional)** is only for the older `Change or Reset PW.ps1` script, because this app does not use RSAT. Step-by-step instructions for moving to another domain are shown under the results.

**Moving to another site:** copy just the exe (not the old ini) and run it. Domain, DC, OU scope (from permissions), DCServer and FileServer are all detected. **Scope & settings** then lists the home and profile path patterns found on that site's existing users. Copy the right ones into `HomePath=` / `ProfilePath=`. Then set `UpnSuffix=` and each user type's `DefaultOU=` / `Groups=`, reload, and run **Check requirements** again.

| Setting | Purpose |
|---|---|
| `DCServer=` / `FileServer=` | Fill in `<dcserver>` / `<fileserver>` in paths. **Blank = auto-detected.** |
| `<domain>` in paths | The DNS domain name, for domain-based DFS paths such as `\\<domain>\dfs\profiles$\Roaming\<username>` (gives `\\paddock-sch.int\dfs\...`). This works whichever DC answers. **Scope & settings** shows the paths found on existing users already written with placeholders, and ticks the ones that match the config. |
| `[ResetPasswordOUs]` | OUs whose users can be listed, searched, reset, unlocked, disabled or given an expiry date. Sub-OUs are included. **If you list OUs, only those are used, even if the operator's AD rights reach further.** Use this to narrow the view. |
| `[CreateUserOUs]` | OUs where users can be created (sub-OUs included). |
| `[Scope] DetectFromPermissions=` | When a list above is empty: `true` works the OUs out from the operator's AD permissions, `false` means nothing is in scope. |
| `[ExcludeOUs]` | OUs that are always hidden |
| `[NewUsers] UsernameFormat=` | Suggested logon name, **always editable**. List several formats separated by `;`. The first is filled in. The others appear as one-click alternatives and are tried when the first is taken. Placeholders: `<firstname>` `<lastname>` `<firstinitial>` `<lastinitial>` `<yyyy>` `<yy>`. Add `:N` to keep the first N letters, e.g. `<lastname:4>`. The template lists 15 ready-made formats. |
| `[NewUsers] UpnSuffix=` | Part after the @ in the sign-in name. Blank = the AD domain. It must be a suffix registered in the forest (Scope & settings lists them). |
| `[NewUsers] Groups=` | Groups **every** new user joins. |
| `[UserType:Name]` | One section per type of account: `Curriculum`, `Admin` and `Student` by default. Settings: `HomePath`, `HomeDrive`, `ProfilePath` (`none` = no profile), `DefaultOU` (placeholders allowed, e.g. `OU=Intake<yyyy>,...`), `Groups` (separate with `;` or repeat the line), `UsernameFormat`, `UpnSuffix`, `EmailFormat`, and `Enabled=false` to hide a type. Add a section to add a new type. Older `[StaffType:Name]` sections still work. |
| `[Features]` `AllowDisable=` / `AllowExpiry=` | Show or hide the Disable/Enable and Account expiry buttons. The buttons are also greyed out when your AD permissions don't allow the change for that user. |
| `[Features]` `ShowResetPasswordTab=` / `ShowCreateUserTab=` / `ShowScopeTab=` | Hide a tab, e.g. for a helpdesk copy that can only reset passwords. This only tidies the app: AD permissions and the OU lists are what limit what someone can do. If every tab is hidden, Scope & settings is still shown. It also reappears if the app cannot connect to AD, so the reason is visible. With it hidden, restart the app to pick up config changes. |
| `[Features] DefaultTab=` | Tab the app opens on: `Reset` (default), `Create` or `Scope`. If that tab is hidden, the first tab shown opens. Scope & settings still opens instead when nothing is in the reset scope or AD can't be reached. |
| `DefaultPassword=` | Default `Welcome2026!`. Using it always forces a password change at next logon. |

## Security
* Sign-in uses the logged-on Windows account (Kerberos). The app never asks for or stores credentials, and AD permissions still apply.
* Search, direct username lookup and reset only work inside the reset OUs. Users outside them are reported as "not found".
* **Audit log:** `ADUserManager-AuditLog.csv` sits next to the exe. Each row has the date and time, the operator (AD logon and name), the computer, the action, the target user, name and OU, the details (including user type and groups) and the result. Failures and app starts are logged too. Passwords are never logged. If the folder is read-only, the log goes to `%LOCALAPPDATA%\ADUserManager\` instead.

## Build / test
* `build.ps1` builds `dist\ADUserManager.exe` using the C# compiler that comes with Windows.
* `ADUserManager.exe /selftest report.txt` runs offline checks, read-only AD checks and the requirements check against your config.
* `ADUserManager.exe /selftest report.txt /write "OU=Test,OU=..."` also creates a temporary user, resets its password and deletes it.
