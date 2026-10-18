using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Threading;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Modules.Expenses;
using HotelPOS.Desktop.Modules.Gst;
using HotelPOS.Desktop.Modules.Kitchen;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Modules.Reservations;
using HotelPOS.Desktop.Modules.Tables;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Rendering;

// The demo pack (roles = "demo"): three chapters of numbered screenshots plus a test record, written under
// HOTELPOS_LIVE_SCREENS_DIR: 01-admin-setup, 04-cashier-desktop, 05-manager-desktop and desktop-test-record.md.
// Every step is an action on the real view-models against the real API, an assertion on the view-model state and a
// screenshot; a failed assertion is recorded as Fail and the run continues with the next step.
public partial class LiveFlowScreenshots
{
    private sealed class DemoCheckException : Exception
    {
        public DemoCheckException(string message)
            : base(message)
        {
        }
    }

    private const string AdminChapterFolder = "01-admin-setup";
    private const string CashierChapterFolder = "04-cashier-desktop";
    private const string ManagerChapterFolder = "05-manager-desktop";
    private const string DemoSection = "Demo Patio";
    private const string DemoTable1 = "DP1";
    private const string DemoTable2 = "DP2";

    private readonly List<string> _demoIssues = new();
    private readonly List<string> _stepShots = new();
    private Terminal? _demoTerminal;
    private string _demoChapter = "";
    private int _demoShotNo;
    private int _demoStepNo;
    private int _demoPass;
    private int _demoFail;
    private readonly List<string> _demoFailures = new();

    private static string RecordPath => Path.Combine(OutputDirectory, "desktop-test-record.md");

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new DemoCheckException(message);
        }
    }

    private async Task DemoFlowAsync()
    {
        Directory.CreateDirectory(OutputDirectory);
        var header = new StringBuilder();
        header.AppendLine("# HotelPOS desktop client - demo test record");
        header.AppendLine();
        header.AppendLine($"Run: {DateTime.Now:yyyy-MM-dd HH:mm} against {ApiBaseUrl}, table {TableCode}, 1366x800, light theme unless noted.");
        header.AppendLine("Every step drives the real desktop view-models against the real API; a step passes when the action completed and the expected state was observed in the view-model.");
        header.AppendLine();
        header.AppendLine("| # | Chapter | Step | Action | Expected | Result (Pass/Fail) | Screenshot | Notes |");
        header.AppendLine("|---|---------|------|--------|----------|--------------------|------------|-------|");
        await File.WriteAllTextAsync(RecordPath, header.ToString());

        var chapters = (System.Environment.GetEnvironmentVariable("HOTELPOS_DEMO_CHAPTERS") ?? "admin,cashier,manager").Split(',');
        if (chapters.Contains("admin"))
        {
            await RunChapterAsync("Admin setup", AdminChapterFolder, DemoAdminChapterAsync);
        }

        if (chapters.Contains("cashier"))
        {
            await RunChapterAsync("Cashier desktop", CashierChapterFolder, DemoCashierChapterAsync);
        }

        if (chapters.Contains("manager"))
        {
            await RunChapterAsync("Manager desktop", ManagerChapterFolder, DemoManagerChapterAsync);
        }

        var summary = new StringBuilder();
        summary.AppendLine();
        summary.AppendLine($"**Summary: {_demoPass} passed, {_demoFail} failed, {_demoPass + _demoFail} steps in total.**");
        summary.AppendLine();
        if (_demoFailures.Count > 0)
        {
            summary.AppendLine("Failed steps:");
            foreach (var failure in _demoFailures)
            {
                summary.AppendLine("- " + failure);
            }

            summary.AppendLine();
        }

        summary.AppendLine("UI/UX issues noticed:");
        if (_demoIssues.Count == 0)
        {
            summary.AppendLine("- none recorded by the run itself");
        }

        foreach (var issue in _demoIssues)
        {
            summary.AppendLine("- " + issue);
        }

        await File.AppendAllTextAsync(RecordPath, summary.ToString());
    }

    private async Task RunChapterAsync(string chapter, string folder, Func<Task> body)
    {
        _demoChapter = chapter;
        _folder = folder;
        _demoShotNo = 0;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            // A chapter-level failure (a sign-in that did not work) is recorded as a failed step; the next chapter still runs.
            await RecordAsync("Chapter", "Run the chapter", "The chapter runs to the end", "Fail", "", $"{ex.GetType().Name}: {ex.Message}");
            _demoFail++;
            _demoFailures.Add($"{chapter}: chapter aborted - {ex.Message}");
        }
        finally
        {
            if (_demoTerminal is { } terminal)
            {
                _demoTerminal = null;
                try
                {
                    await terminal.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Note("dispose failed: " + ex.Message);
                }
            }
        }
    }

    // ---------------------------------------------------------------- step plumbing

    private async Task DemoShotAsync(string name, Func<System.Windows.Controls.ScrollViewer, bool>? scrollToEnd = null, double? fraction = null)
    {
        var file = $"{++_demoShotNo:00}-{name}";
        await RenderAsync(_demoTerminal!, file, scrollToEnd, fraction);
        _stepShots.Add(file + ".png");
    }

    private async Task StepAsync(string step, string action, string expected, Func<Task<string?>> body)
    {
        _stepShots.Clear();
        var watch = Stopwatch.StartNew();
        var result = "Pass";
        var notes = "";
        try
        {
            var bodyTask = body();
            var finished = await Task.WhenAny(bodyTask, Task.Delay(TimeSpan.FromSeconds(150)));
            if (finished != bodyTask)
            {
                throw new DemoCheckException("the step did not finish within 150 s (the view-model or the API hung)");
            }

            notes = await bodyTask ?? "";
        }
        catch (Exception ex)
        {
            result = "Fail";
            notes = ex is DemoCheckException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            try
            {
                if (_stepShots.Count == 0 && _demoTerminal is not null)
                {
                    await DemoShotAsync(Slug(step) + "-failed");
                }
            }
            catch (Exception shotEx)
            {
                notes += " (no screenshot: " + shotEx.Message + ")";
            }

            try
            {
                if (_demoTerminal?.Dialogs.Current is { } dialog)
                {
                    dialog.CancelCommand.Execute(null);
                }
            }
            catch
            {
                // Best effort: leave the next step a clean screen.
            }
        }

        watch.Stop();
        if (watch.Elapsed > TimeSpan.FromSeconds(8))
        {
            var slow = $"Slow step: '{step}' took {watch.Elapsed.TotalSeconds:0.#} s ({string.Join(", ", _stepShots)})";
            _demoIssues.Add(slow);
            notes = (notes + " " + $"[took {watch.Elapsed.TotalSeconds:0.#} s]").Trim();
        }

        if (result == "Pass")
        {
            _demoPass++;
        }
        else
        {
            _demoFail++;
            _demoFailures.Add($"{_demoChapter} / {step}: {notes}");
            Note($"FAIL {step}: {notes}");
        }

        await RecordAsync(_demoChapter, step, action, expected, result, string.Join("<br>", _stepShots), notes);
    }

    private async Task RecordAsync(string chapter, string step, string action, string expected, string result, string shots, string notes)
    {
        _demoStepNo++;
        static string Cell(string text) => text.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        var line = $"| {_demoStepNo} | {Cell(chapter)} | {Cell(step)} | {Cell(action)} | {Cell(expected)} | {result} | {shots} | {Cell(notes)} |";
        await File.AppendAllTextAsync(RecordPath, line + System.Environment.NewLine);
    }

    // Overload used by the chapter-abort path: (chapter, step, expected, result, shots, notes).
    private Task RecordAsync(string chapter, string step, string expected, string result, string shots, string notes) =>
        RecordAsync(chapter, step, "-", expected, result, shots, notes);

    // The dev API went unresponsive right after an expense was recorded in every run; wait for it (and the terminal's
    // realtime link) to come back so the next steps do not fail for that reason, and record the outage as an issue.
    private async Task DemoRecoverAsync(string after, Terminal terminal)
    {
        var watch = Stopwatch.StartNew();
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var outage = false;
        var healthyStreak = 0;
        while (watch.Elapsed < TimeSpan.FromMinutes(6) && healthyStreak < 3)
        {
            try
            {
                var body = await http.GetStringAsync(ApiBaseUrl.TrimEnd('/') + "/health");
                healthyStreak = body.Contains("Healthy", StringComparison.OrdinalIgnoreCase) ? healthyStreak + 1 : 0;
            }
            catch (Exception)
            {
                outage = true;
                healthyStreak = 0;
            }

            await Task.Delay(2000);
        }

        await WaitUntilAsync(() => terminal.Realtime.Status == ConnectionStatus.Connected, TimeSpan.FromMinutes(2), "realtime reconnect", throwOnTimeout: false);
        if (outage)
        {
            _demoIssues.Add($"The API stopped answering /health after {after} on the cashier terminal and was unreachable for about {watch.Elapsed.TotalSeconds:0} s (seen in every run; the realtime link showed 'Reconnecting'). Not reproduced with the same request sent by curl without a connected terminal");
            Note($"API outage after {after}, recovered after {watch.Elapsed.TotalSeconds:0} s.");
        }
    }

    private static string Slug(string text) =>
        new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    // Like DriveAsync, but the dialogs it meets are captured as numbered demo shots.
    private async Task DemoDriveAsync(Task action, string dialogShot, params DialogAnswer[] answers)
    {
        var index = 0;
        DialogViewModel? seen = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (!action.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(120);
            if (_demoTerminal!.Dialogs.Current is { } dialog && !ReferenceEquals(dialog, seen))
            {
                seen = dialog;
                var answer = index < answers.Length ? answers[index] : null;
                index++;
                if (answer?.Approver is not null)
                {
                    dialog.ApproverUsername = answer.Approver;
                }

                if (answer?.Input is not null)
                {
                    dialog.Input = answer.Input;
                }

                await DemoShotAsync(index == 1 ? dialogShot : $"{dialogShot}-{index}");
                if (answer is null)
                {
                    dialog.CancelCommand.Execute(null);
                }
                else
                {
                    dialog.ConfirmCommand.Execute(null);
                }
            }
        }

        Check(action.IsCompleted, "the action did not finish within 90 s");
        await action;
    }

    private async Task<ShellViewModel> DemoSignInAsync(Terminal terminal, string username, string password, string loginShot)
    {
        _demoTerminal = terminal;
        ShellViewModel? shell = null;
        await StepAsync($"Sign in as {username}", $"Open the sign-in screen, type {username} and the password, press Sign in",
            "The server check shows Reachable, sign-in succeeds, the shell opens and realtime connects", async () =>
            {
                var navigator = terminal.Get<IAppNavigator>();
                await navigator.ShowLoginAsync();
                var login = (LoginViewModel)terminal.Main.CurrentScreen!;
                await WaitUntilAsync(() => login.ServerState != ServerState.Checking && login.ServerState != ServerState.Unknown, TimeSpan.FromSeconds(15), "server check");
                login.Username = username;
                login.Password = password;
                await DemoShotAsync(loginShot);
                Check(login.ServerState == ServerState.Reachable, $"server state is {login.ServerState}");
                await login.LoginCommand.ExecuteAsync(null);
                Check(terminal.Main.CurrentScreen is ShellViewModel, $"{username} could not sign in: {login.ErrorMessage}");
                shell = (ShellViewModel)terminal.Main.CurrentScreen!;
                await WaitUntilAsync(() => terminal.Realtime.Status == ConnectionStatus.Connected, TimeSpan.FromSeconds(30), "realtime connected");
                return $"Signed in on {terminal.Settings.Current.DeviceName}; realtime connected.";
            });
        return shell ?? throw new InvalidOperationException($"{username} could not sign in; the chapter cannot continue.");
    }

    private async Task<T> DemoOpenAsync<T>(ShellViewModel shell, string module, object? parameter = null, int settleMs = 400)
        where T : class
    {
        Check(shell.NavItems.Any(n => n.Key == module), $"module '{module}' is not in the sidebar of this user");
        await shell.NavigateToAsync(module, parameter);
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Delay(settleMs);
        var page = shell.CurrentPage as T;
        Check(page is not null, $"'{module}' opened as {shell.CurrentPage?.GetType().Name ?? "nothing"}, not {typeof(T).Name}");
        return page!;
    }

    private void ClearToasts() => _demoTerminal!.Get<NotificationService>().Items.Clear();

    // ---------------------------------------------------------------- chapter 01: admin setup

    private async Task DemoAdminChapterAsync()
    {
        var password = System.Environment.GetEnvironmentVariable("HOTELPOS_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("HOTELPOS_ADMIN_PASSWORD is not set; the admin chapter needs the admin account.");
        }

        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        var shell = await DemoSignInAsync(terminal, "admin", password, "sign-in");

        await StepAsync("Dashboard", "Land on the dashboard after sign-in", "The dashboard loads KPI cards and the order/table panels", async () =>
        {
            var dashboard = shell.CurrentPage as DashboardViewModel
                ?? await DemoOpenAsync<DashboardViewModel>(shell, ModuleRegistry.Dashboard);
            await Task.Delay(1200);
            ClearToasts();
            await DemoShotAsync("dashboard");
            Check(dashboard.Cards.Count >= 4, $"only {dashboard.Cards.Count} KPI cards");
            return $"{dashboard.Cards.Count} KPI cards.";
        });

        // Users: list, New user dialog, after.
        UsersViewModel? users = null;
        var userName = "";
        await StepAsync("Users list", "Open Users", "The staff accounts are listed", async () =>
        {
            users = await DemoOpenAsync<UsersViewModel>(shell, ModuleRegistry.Users, settleMs: 800);
            users.SelectedUser = users.Users.FirstOrDefault(u => u.Username == "waiter1") ?? users.Users.FirstOrDefault();
            await DemoShotAsync("users-list");
            Check(users.Users.Count >= 5, $"{users.Users.Count} users listed");
            return $"{users.Users.Count} users.";
        });

        if (users is not null)
        {
            await StepAsync("New user dialog", "Press New user, fill username, display name, password and the Waiter role", "The editor opens with the details filled in", async () =>
            {
                var taken = users.Users.Select(u => u.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var n = 1;
                while (taken.Contains($"demo.waiter{n}"))
                {
                    n++;
                }

                userName = $"demo.waiter{n}";
                users.SelectedUser = null;
                users.NewUserCommand.Execute(null);
                users.Editor.Username = userName;
                users.Editor.DisplayName = "Kavya (Demo waiter)";
                users.Editor.Password = "Pass@123";
                foreach (var role in users.Editor.Roles)
                {
                    role.IsSelected = role.Name == "Waiter";
                }

                await DemoShotAsync("users-new-dialog");
                Check(users.Editor.IsOpen, "the user editor did not open");
                return userName;
            });

            await StepAsync("Create user", "Press Create user", "The editor closes and the new account is in the list", async () =>
            {
                await DemoDriveAsync(users.SaveUserCommand.ExecuteAsync(null), "users-create-dialog");
                var error = users.Editor.IsOpen ? users.Editor.ErrorMessage : null;
                users.SelectedUser = users.Users.FirstOrDefault(u => u.Username == userName);
                await Task.Delay(300);
                await DemoShotAsync("users-created");
                Check(!users.Editor.IsOpen, "the editor stayed open: " + error);
                Check(users.Users.Any(u => u.Username == userName), $"{userName} is not in the list");
                return $"{userName} created (left active in the dev DB).";
            });
        }

        // Roles & permissions.
        await StepAsync("Roles & permissions", "Open Roles & Permissions, pick Manager, scroll to the end of the list",
            "The permission list includes the Phase 22 permissions: reservations.view/manage, expenses.view/manage, floor.layout.manage", async () =>
            {
                var roles = await DemoOpenAsync<RolesViewModel>(shell, ModuleRegistry.RolesModule, settleMs: 600);
                roles.SelectedRole = roles.Roles.FirstOrDefault(r => r.RoleName == "Manager") ?? roles.Roles.FirstOrDefault();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await DemoShotAsync("roles-permissions");
                await DemoShotAsync("roles-permissions-end", v => v.ViewportWidth > 400);
                var wanted = new[] { Permissions.ReservationsView, Permissions.ReservationsManage, Permissions.ExpensesView, Permissions.ExpensesManage, Permissions.FloorLayoutManage };
                var missing = wanted.Where(k => roles.PermissionItems.All(p => p.Key != k)).ToList();
                if (missing.Count > 0)
                {
                    _demoIssues.Add($"Roles & Permissions lists only {roles.PermissionItems.Count} permissions and has no row for {string.Join(", ", missing)}; saving a role from this page also drops those permissions from the role (the saved list is built from the rows shown) ({string.Join(", ", _stepShots)})");
                }

                Check(missing.Count == 0, "no row for: " + string.Join(", ", missing));
                return null;
            });

        await StepAsync("Devices", "Open Devices", "The registered terminals are listed with their online state", async () =>
        {
            var devices = await DemoOpenAsync<DevicesViewModel>(shell, ModuleRegistry.Devices, settleMs: 800);
            devices.SelectedDevice = devices.Devices.FirstOrDefault(d => d.Name == terminal.Settings.Current.DeviceName) ?? devices.Devices.FirstOrDefault();
            await DemoShotAsync("devices");
            Check(devices.Devices.Count > 0, "no devices listed");
            return $"{devices.Devices.Count} devices.";
        });

        // Sections & tables (the demo section and tables are reused when they exist).
        FloorViewModel? floor = null;
        await StepAsync("Sections", "Open Sections & Tables, Sections tab (create the demo section when it is not there)", "The Demo Patio section is listed", async () =>
        {
            floor = await DemoOpenAsync<FloorViewModel>(shell, ModuleRegistry.Floor);
            floor.SelectedTab = FloorViewModel.SectionsTab;
            await floor.Sections.LoadAsync();
            if (floor.Sections.Sections.All(s => s.Name != DemoSection))
            {
                floor.Sections.NewSectionCommand.Execute(null);
                floor.Sections.Editor.Name = DemoSection;
                await floor.Sections.SaveSectionCommand.ExecuteAsync(null);
                Check(!floor.Sections.Editor.IsOpen, "create section: " + floor.Sections.Editor.ErrorMessage);
                await floor.Sections.LoadAsync();
            }

            floor.Sections.SelectedSection = floor.Sections.Sections.FirstOrDefault(s => s.Name == DemoSection);
            await Task.Delay(300);
            await DemoShotAsync("sections-tab");
            Check(floor.Sections.Sections.Any(s => s.Name == DemoSection), "Demo Patio is not listed");
            return $"{floor.Sections.Sections.Count} sections.";
        });

        if (floor is not null)
        {
            await StepAsync("Tables", "Tables tab: add table DP1 in the editor (DP2 too, quietly), save", "DP1 and DP2 are listed under Demo Patio", async () =>
            {
                floor.SelectedTab = FloorViewModel.TablesTab;
                await floor.Tables.LoadAsync();
                async Task EnsureAsync(string code, string name, bool capture)
                {
                    var existing = floor.Tables.Tables.FirstOrDefault(t => t.Table.Code == code);
                    if (existing is not null)
                    {
                        if (capture)
                        {
                            floor.Tables.SelectedRow = existing;
                            floor.Tables.EditCommand.Execute(null);
                            await DemoShotAsync("tables-editor");
                            floor.Tables.CancelEditCommand.Execute(null);
                        }

                        return;
                    }

                    floor.Tables.NewTableCommand.Execute(null);
                    floor.Tables.Editor.Code = code;
                    floor.Tables.Editor.Name = name;
                    floor.Tables.Editor.Section = floor.Tables.Editor.Sections.First(s => s.Name == DemoSection);
                    floor.Tables.Editor.Capacity = "4";
                    if (capture)
                    {
                        await DemoShotAsync("tables-editor");
                    }

                    await floor.Tables.SaveTableCommand.ExecuteAsync(null);
                    Check(!floor.Tables.Editor.IsOpen, $"create {code}: {floor.Tables.Editor.ErrorMessage}");
                    await floor.Tables.LoadAsync();
                }

                await EnsureAsync(DemoTable1, "Patio 1", true);
                await EnsureAsync(DemoTable2, "Patio 2", false);
                floor.Tables.SelectedRow = floor.Tables.Tables.FirstOrDefault(t => t.Table.Code == DemoTable1);
                await Task.Delay(300);
                await DemoShotAsync("tables-tab");
                Check(floor.Tables.Tables.Any(t => t.Table.Code == DemoTable1) && floor.Tables.Tables.Any(t => t.Table.Code == DemoTable2), "DP1/DP2 are not both listed");
                return null;
            });
        }

        // Floor plan editor.
        FloorEditorViewModel? plan = null;
        await StepAsync("Floor plan: section", "Open Floor plan, pick Demo Patio, place its tables on the canvas", "Both demo tables sit on the canvas", async () =>
        {
            plan = await DemoOpenAsync<FloorEditorViewModel>(shell, ModuleRegistry.FloorPlan, settleMs: 600);
            plan.SelectedSection = plan.Sections.FirstOrDefault(s => s.Name == DemoSection);
            Check(plan.SelectedSection is not null, "Demo Patio is not offered in the floor plan");
            await plan.LoadLayoutAsync();
            foreach (var item in plan.Unplaced.ToList())
            {
                plan.PlaceCommand.Execute(item);
            }

            plan.Select(null);
            await DemoShotAsync("floor-plan-section");
            Check(plan.Placed.Count >= 2, $"{plan.Placed.Count} tables placed");
            return $"{plan.Placed.Count} tables placed.";
        });

        if (plan is not null)
        {
            await StepAsync("Floor plan: select a tile", "Click the first tile", "The tile is selected (handles shown) and Rotate / Shape are enabled", async () =>
            {
                var tile = plan.Placed.First();
                plan.Select(tile);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await DemoShotAsync("floor-plan-tile-selected");
                Check(plan.SelectedItem == tile && tile.IsSelected, "tile not selected");
                Check(plan.RotateCommand.CanExecute(null) && plan.ToggleShapeCommand.CanExecute(null), "Rotate/Shape stayed disabled");
                return null;
            });

            await StepAsync("Floor plan: rotate and shape", "Press Rotate 90 and toggle the shape, nudge the tile", "Rotation is 90 degrees apart, the shape flips, the editor shows unsaved changes", async () =>
            {
                var tile = plan.SelectedItem!;
                var rotation = tile.Rotation;
                var shape = tile.Shape;
                plan.RotateCommand.Execute(null);
                plan.ToggleShapeCommand.Execute(null);
                plan.Nudge(16, 16);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await DemoShotAsync("floor-plan-rotated");
                Check(tile.Rotation == (rotation + 90) % 360, $"rotation {rotation} -> {tile.Rotation}");
                Check(tile.Shape != shape, "shape did not change");
                Check(plan.IsDirty, "not marked as unsaved");
                return $"Rotation {rotation} -> {tile.Rotation}; shape {shape} -> {tile.Shape}.";
            });

            await StepAsync("Floor plan: save", "Press Save", "The layout is stored, the editor reports all changes saved", async () =>
            {
                ClearToasts();
                await plan.SaveCommand.ExecuteAsync(null);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await DemoShotAsync("floor-plan-saved");
                Check(!plan.IsDirty, "still dirty: " + plan.ErrorMessage);
                return null;
            });
        }

        // Menu.
        MenuAdminViewModel? menu = null;
        await StepAsync("Menu categories", "Open Menu, Categories tab", "The categories are listed", async () =>
        {
            menu = await DemoOpenAsync<MenuAdminViewModel>(shell, ModuleRegistry.Menu, MenuAdminViewModel.CategoriesTab, 600);
            await DemoShotAsync("menu-categories");
            Check(menu.SelectedTab is CategoriesPageViewModel { Categories.Count: > 0 }, "no categories listed");
            return null;
        });

        if (menu is not null)
        {
            await StepAsync("Menu items", "Items tab, select an item that has a picture", "The items are listed with the selected item's detail", async () =>
            {
                menu.SelectedTab = menu.Tabs.Single(t => t.Title == "Items");
                await menu.SelectedTab.LoadAsync();
                await Task.Delay(500);
                menu.Items.SelectedItem = menu.Items.Items.FirstOrDefault(i => i.ImageUrl is not null) ?? menu.Items.Items.FirstOrDefault();
                await DemoShotAsync("menu-items");
                Check(menu.Items.Items.Count > 0, "no items listed");
                return $"{menu.Items.Items.Count} items.";
            });

            await StepAsync("Menu item editor", "Press Edit on the selected item", "The editor opens with the picture, price and modifier groups", async () =>
            {
                Check(menu.Items.SelectedItem is not null, "no item selected");
                await menu.Items.EditCommand.ExecuteAsync(null);
                await Task.Delay(500);
                await DemoShotAsync("menu-item-editor");
                await DemoShotAsync("menu-item-editor-modifiers", v => v.ViewportWidth < 600);
                Check(!menu.Items.Editor.IsNew, "the editor did not open on the item");
                var hasPicture = menu.Items.Editor.HasPicture;
                menu.Items.CancelEditCommand.Execute(null);
                return hasPicture ? "Item has a picture." : "Item has no picture (none uploaded in this database).";
            });

            await StepAsync("Menu modifiers", "Modifiers tab", "The modifier groups are listed", async () =>
            {
                menu.SelectedTab = menu.Tabs.Single(t => t.Title == "Modifiers");
                await menu.SelectedTab.LoadAsync();
                await Task.Delay(500);
                await DemoShotAsync("menu-modifiers");
                Check(menu.SelectedTab is ModifiersPageViewModel, "modifiers tab did not open");
                return null;
            });
        }

        await StepAsync("Payments & discounts", "Open Payments & discounts", "The payment methods include Wallet", async () =>
        {
            var setup = await DemoOpenAsync<BillingSetupViewModel>(shell, ModuleRegistry.Discounts, settleMs: 800);
            setup.SelectedTab = BillingSetupViewModel.DiscountsTab;
            await Task.Delay(300);
            await DemoShotAsync("payments-discounts");
            Check(setup.Methods.Any(m => m.Code == PaymentMethodCodes.Wallet), "no Wallet payment method; methods: " + string.Join(", ", setup.Methods.Select(m => m.Code)));
            return "Methods: " + string.Join(", ", setup.Methods.Select(m => m.Name));
        });

        SettingsHubViewModel? hub = null;
        await StepAsync("Restaurant settings", "Open Restaurant Settings", "The settings list loads", async () =>
        {
            hub = await DemoOpenAsync<SettingsHubViewModel>(shell, ModuleRegistry.Settings, SettingsHubViewModel.SettingsTab, 800);
            await DemoShotAsync("restaurant-settings");
            Check(hub.Settings.Items.Count > 0, "no settings loaded");
            return $"{hub.Settings.Items.Count} settings.";
        });

        await StepAsync("Printers", "Open Printers", "The printer profiles are shown", async () =>
        {
            var printers = await DemoOpenAsync<PrinterSettingsViewModel>(shell, ModuleRegistry.Printers, settleMs: 500);
            await DemoShotAsync("printers");
            Check(printers.Profiles.Count > 0, "no printer profiles");
            return null;
        });

        await StepAsync("Expense categories", "Open Restaurant Settings, Expense categories tab", "The seeded expense categories are listed", async () =>
        {
            hub = await DemoOpenAsync<SettingsHubViewModel>(shell, ModuleRegistry.Settings, SettingsHubViewModel.CategoriesTab, 800);
            await hub.Categories.LoadAsync();
            if (hub.Categories.Categories.Count > 0)
            {
                hub.Categories.SelectedCategory = hub.Categories.Categories.First();
            }

            await DemoShotAsync("expense-categories");
            Check(hub.IsCategoriesTab, "the Expense categories tab is not selected");
            Check(hub.Categories.Categories.Count > 0, "no expense categories");
            return $"{hub.Categories.Categories.Count} categories.";
        });

        await StepAsync("Licence", "Open Licence", "The licence status is shown", async () =>
        {
            await DemoOpenAsync<object>(shell, ModuleRegistry.License, settleMs: 800);
            await DemoShotAsync("licence");
            return null;
        });

        await StepAsync("Server", "Open Server", "The server log loads", async () =>
        {
            await DemoOpenAsync<object>(shell, ModuleRegistry.Server, settleMs: 1200);
            await DemoShotAsync("server");
            return null;
        });

        await StepAsync("Backup", "Open Backup (nothing is run)", "The backup page shows its schedule and history", async () =>
        {
            await DemoOpenAsync<object>(shell, ModuleRegistry.Backup, settleMs: 800);
            await DemoShotAsync("backup");
            return null;
        });

        await StepAsync("Audit log", "Open Audit Log", "Entries are listed, newest first", async () =>
        {
            var audit = await DemoOpenAsync<AuditLogsViewModel>(shell, ModuleRegistry.Audit, settleMs: 800);
            audit.SelectedEntry = audit.AuditEntries.FirstOrDefault();
            await DemoShotAsync("audit-log");
            Check(audit.AuditEntries.Count > 0, "no audit entries");
            return $"{audit.AuditEntries.Count} entries on the page.";
        });

        await StepAsync("Command palette", "Back on the dashboard press Ctrl+K and type 're'", "The palette opens with matching modules", async () =>
        {
            await DemoOpenAsync<DashboardViewModel>(shell, ModuleRegistry.Dashboard, settleMs: 600);
            ClearToasts();
            await shell.OpenPaletteCommand.ExecuteAsync(null);
            shell.Palette.Query = "re";
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("command-palette");
            var count = shell.Palette.Results.Count;
            shell.Palette.CloseCommand.Execute(null);
            Check(count > 0, "the palette found nothing for 're'");
            return $"{count} results.";
        });

        await StepAsync("Dark theme", "Switch the theme to dark on the dashboard", "The dashboard re-renders in the dark theme", async () =>
        {
            terminal.Theme.Apply(ThemeService.Dark);
            try
            {
                await DemoShotAsync("dashboard-dark");
            }
            finally
            {
                terminal.Theme.Apply(ThemeService.Light);
            }

            return null;
        });

        await terminal.Get<IAppNavigator>().LogoutAsync();
    }

    // ---------------------------------------------------------------- shared API preparation

    private async Task<int> PrepareTakeawayAsync(bool kitchenReady, bool requestBill, string customer)
    {
        await using var waiter = new Terminal(DeviceName("WAITER", "VERIFY-01"), DeviceType.Waiter, headless: true);
        await waiter.SignInAsync("waiter1", "Pass@123");
        var orders = waiter.Get<IOrdersApi>();
        var created = await orders.CreateAsync(new CreateOrderRequest
        {
            OrderType = OrderType.Takeaway,
            GuestCount = 1,
            CustomerName = customer,
            CustomerPhone = "9840012345",
            Items = new[]
            {
                new OrderItemInput { MenuItemId = 5, Quantity = 1, ModifierOptionIds = new[] { 3 } },
                new OrderItemInput { MenuItemId = 9, Quantity = 2 },
            },
            Submit = true,
        }, Guid.NewGuid());
        Require(created, "create takeaway order");
        var orderId = created.Data!.Id;
        Note($"Created takeaway order #{created.Data.OrderNumber} ({created.Data.TableCode}).");
        if (!kitchenReady && !requestBill)
        {
            return orderId;
        }

        await using var kitchen = new Terminal(DeviceName("KITCHEN", "APP-KITCHEN-B4134F"), DeviceType.Kitchen, headless: true);
        await kitchen.SignInAsync("kitchen1", "Pass@123");
        var kitchenApi = kitchen.Get<IKitchenApi>();
        var open = await kitchenApi.GetOpenAsync(null);
        Require(open, "kitchen tickets");
        foreach (var ticket in open.Data!.Tickets.Where(t => t.OrderId == orderId))
        {
            var status = ticket.Status;
            if (status == KitchenOrderStatus.New)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Accept), "accept");
                status = KitchenOrderStatus.Accepted;
            }

            if (status == KitchenOrderStatus.Accepted)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Start), "start");
                status = KitchenOrderStatus.Preparing;
            }

            if (status == KitchenOrderStatus.Preparing)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Ready), "ready");
            }
        }

        if (requestBill)
        {
            var served = await orders.ServeAsync(orderId);
            Note(served.Success ? "Takeaway served." : "Takeaway serve skipped: " + served.Message);
            var billed = await orders.RequestBillAsync(orderId, Guid.NewGuid());
            Require(billed, "request takeaway bill");
            Note($"Takeaway bill requested: {billed.Data!.TableCode} total {Money.Format(billed.Data.GrandTotal)}.");
        }

        return orderId;
    }

    // Grants or removes the two expense permissions on the Cashier role through the admin API (the cashier has none by default).
    private async Task SetCashierExpensePermissionsAsync(bool grant)
    {
        var password = System.Environment.GetEnvironmentVariable("HOTELPOS_ADMIN_PASSWORD");
        await using var admin = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin, headless: true);
        await admin.SignInAsync("admin", password!);
        var api = admin.Get<IUsersApi>();
        var roles = await api.ListAllRolePermissionsAsync();
        Require(roles, "list roles");
        var cashier = roles.Data!.Single(r => r.RoleName == "Cashier");
        var permissions = cashier.Permissions.ToList();
        foreach (var key in new[] { Permissions.ExpensesView, Permissions.ExpensesManage })
        {
            if (grant && !permissions.Contains(key))
            {
                permissions.Add(key);
            }
            else if (!grant)
            {
                permissions.Remove(key);
            }
        }

        var saved = await api.UpdateRolePermissionsAsync(cashier.RoleId, new UpdateRolePermissionsRequest { Permissions = permissions, ForceRelogin = false });
        Require(saved, "update Cashier permissions");
        Note($"Cashier role: expenses permissions {(grant ? "granted" : "removed again")} through the admin API ({permissions.Count} permissions).");
    }

    // ---------------------------------------------------------------- chapter 04: cashier

    private async Task DemoCashierChapterAsync()
    {
        await SetCashierExpensePermissionsAsync(grant: true);
        try
        {
            await DemoCashierBodyAsync();
        }
        finally
        {
            await SetCashierExpensePermissionsAsync(grant: false);
        }
    }

    private async Task DemoCashierBodyAsync()
    {
        var terminal = new Terminal(DeviceName("BILLING", "BILLING-01"), DeviceType.Billing, invoicePrinterUnreachable: true);
        var notifications = terminal.Get<NotificationService>();
        var printQueue = terminal.Get<IPrintQueue>();
        var shell = await DemoSignInAsync(terminal, "cashier1", "Pass@123", "sign-in");

        await StepAsync("Waiter and kitchen prepare", $"As waiter1 and kitchen1 (API): order on {TableCode}, a takeaway order, kitchen ready, served, bills requested",
            "Both bills reach the cashier terminal in real time", async () =>
            {
                await PrepareTableAsync();
                var takeawayId = await PrepareTakeawayAsync(kitchenReady: true, requestBill: true, "Anita (walk-in)");
                return $"Table order on {TableCode}; takeaway order id {takeawayId}.";
            });

        CashierHomeViewModel? home = null;
        await StepAsync("Cashier home", "Look at the cashier home", "The waiting bills include the table bill and the takeaway bill", async () =>
        {
            home = shell.CurrentPage as CashierHomeViewModel ?? await DemoOpenAsync<CashierHomeViewModel>(shell, ModuleRegistry.CashierHome);
            await WaitUntilAsync(() => home.WaitingBills.Any(b => b.TableCode == TableCode), TimeSpan.FromSeconds(8), "home refresh", throwOnTimeout: false);
            if (home.WaitingBills.All(b => b.TableCode != TableCode))
            {
                await home.RefreshAsync();
            }

            await Dispatcher.Yield(DispatcherPriority.Background);
            notifications.Items.Clear();
            await DemoShotAsync("cashier-home");
            Check(home.WaitingBills.Count > 0, "the cashier home lists no waiting bill");
            return $"{home.WaitingBills.Count} waiting bills shown (the home lists the six oldest; {TableCode} on it: {home.WaitingBills.Any(b => b.TableCode == TableCode)}).";
        });

        await StepAsync("Open shift", "Press Open shift, count the float of 3,000, confirm", "A shift is open for cashier1", async () =>
        {
            if (shell.ShiftBar.Shift is not null)
            {
                await DemoShotAsync("shift-already-open");
                return "A shift was already open on this terminal; reused.";
            }

            await shell.ShiftBar.OpenShiftCommand.ExecuteAsync(null);
            var panel = shell.ShiftBar.Panel as ShiftCountPanelViewModel;
            Check(panel is not null, "the open-shift panel did not open");
            FillSheet(panel!.Sheet, 3000m);
            await DemoShotAsync("open-shift");
            await panel.ConfirmCommand.ExecuteAsync(null);
            Check(shell.ShiftBar.Shift is not null, "shift not opened: " + panel.ErrorMessage);
            shell.ShiftBar.ClosePanelCommand.Execute(null);
            return "Shift opened with a float of 3,000.";
        });

        CashierWorkspaceViewModel? workspace = null;
        await StepAsync("Queue with a takeaway", "Open Billing", "The queue lists the table bill and the takeaway bill with its TA- label", async () =>
        {
            workspace = await DemoOpenAsync<CashierWorkspaceViewModel>(shell, ModuleRegistry.Billing, settleMs: 600);
            notifications.Items.Clear();
            await DemoShotAsync("queue");
            var takeaway = workspace.Bills.FirstOrDefault(b => b.OrderType == OrderType.Takeaway);
            Check(takeaway is not null, "no takeaway bill in the queue");
            Check(takeaway!.TableCode.StartsWith("TA-", StringComparison.Ordinal), $"takeaway label is '{takeaway.TableCode}'");
            return $"{workspace.Bills.Count} bills; takeaway label {takeaway.TableCode}.";
        });

        await StepAsync("Queue: Takeaway chip", "Select the Takeaway channel chip", "Only takeaway bills are listed, labelled TA-nnnn", async () =>
        {
            workspace!.SelectChannelFilterCommand.Execute(OrderType.Takeaway);
            await Task.Delay(300);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("queue-takeaway");
            var all = workspace.Bills.ToList();
            workspace.SelectChannelFilterCommand.Execute(null);
            Check(all.Count > 0 && all.All(b => b.TableCode.StartsWith("TA-", StringComparison.Ordinal)), "the takeaway chip lists: " + string.Join(", ", all.Select(b => b.TableCode)));
            return string.Join(", ", all.Select(b => b.TableCode));
        });

        BillDetailViewModel? bill = null;
        await StepAsync("Workspace with a bill", $"Select the {TableCode} bill", "The order and the payment column open for the bill", async () =>
        {
            var card = workspace!.Bills.FirstOrDefault(b => b.TableCode == TableCode) ?? throw new DemoCheckException($"no {TableCode} bill in the queue");
            await workspace.SelectAsync(card);
            bill = workspace.Detail;
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("workspace-bill");
            Check(bill is not null, "the bill did not open");
            return null;
        });

        await StepAsync("Discount", "Open Discount, enter 5 % for a regular guest, apply", "The bill shows the discount", async () =>
        {
            await bill!.ShowDiscountCommand.ExecuteAsync(null);
            bill.ManualValue = "5";
            bill.ManualReason = "Regular guest";
            await DemoShotAsync("discount");
            await GuardedAsync(terminal, bill.ApplyManualCommand.ExecuteAsync(null), "discount");
            var applied = bill.HasDiscount;
            if (bill.IsPanelOpen)
            {
                bill.ClosePanelCommand.Execute(null);
            }

            Check(applied, "discount not applied: " + bill.PanelError);
            return bill.DiscountLabel;
        });

        await StepAsync("Finalize", "Finalize and print the invoice", "An invoice number is issued; the invoice print to the unreachable printer fails and shows in the print queue", async () =>
        {
            await bill!.FinalizeCommand.ExecuteAsync(null);
            bill.ClosePanelCommand.Execute(null);
            await WaitUntilAsync(() => printQueue.FailedCount > 0, TimeSpan.FromSeconds(40), "invoice print failure", throwOnTimeout: false);
            await DemoShotAsync("finalized");
            Check(bill.Bill?.InvoiceNumber is not null, "no invoice number: " + bill.ErrorMessage);
            return $"Invoice {bill.Bill!.InvoiceNumber}; {printQueue.FailedCount} failed print job(s).";
        });

        await StepAsync("Cash payment (F2)", "Press F2 and type a rounded-up tender", "The cash panel opens with the change shown", async () =>
        {
            bill!.PayHotkeyCommand.Execute("F2");
            Check(bill.Payment is { IsCash: true }, "F2 did not open the cash payment");
            bill.Payment!.TenderedText = Money.Format(Math.Ceiling(bill.Bill!.BalanceDue / 500m) * 500m);
            notifications.Items.Clear();
            await DemoShotAsync("cash-payment");
            return "Shown, not confirmed; the bill is settled by the split payment next.";
        });

        await StepAsync("Split payment", "Choose Split: cash for about half, card (with reference) for the rest, confirm", "The bill is paid", async () =>
        {
            bill!.PaySplitCommand.Execute(null);
            var payment = bill.Payment ?? throw new DemoCheckException("the payment panel did not open");
            var balance = bill.Bill!.BalanceDue;
            var cashPart = Math.Max(10m, Math.Floor(balance / 2m / 10m) * 10m);
            payment.AddLineCommand.Execute(payment.Methods.Single(m => m.Code == PaymentMethodCodes.Cash));
            payment.Lines[0].AmountText = Money.Format(cashPart);
            payment.AddLineCommand.Execute(payment.Methods.Single(m => m.Code == PaymentMethodCodes.Card));
            payment.Lines[1].AmountText = Money.Format(balance - cashPart);
            payment.Lines[1].Reference = "VISA 4421 - APPR 071519";
            notifications.Items.Clear();
            await DemoShotAsync("split-payment");
            await GuardedAsync(terminal, bill.ConfirmPaymentCommand.ExecuteAsync(null), "split-payment");
            Check(bill.IsPaid || bill.IsDonePanel, "split payment not completed: " + bill.PanelError);
            return $"Cash {Money.Format(cashPart)} + card {Money.Format(balance - cashPart)}.";
        });

        BillDetailViewModel? takeawayBill = null;
        await StepAsync("Wallet payment", "Select the takeaway bill, choose the Wallet method, type the wallet reference", "The Wallet panel asks for the reference and is ready to confirm", async () =>
        {
            await workspace!.SelectFilterCommand.ExecuteAsync(QueueFilter.All);
            var card = workspace.Bills.FirstOrDefault(b => b.OrderType == OrderType.Takeaway) ?? throw new DemoCheckException("no takeaway bill in the queue");
            await workspace.SelectAsync(card);
            takeawayBill = workspace.Detail ?? throw new DemoCheckException("the takeaway bill did not open");
            var wallet = takeawayBill.Methods.FirstOrDefault(m => m.Code == PaymentMethodCodes.Wallet) ?? throw new DemoCheckException("the bill offers no Wallet method");
            takeawayBill.PayWithCommand.Execute(wallet);
            Check(takeawayBill.Payment is not null, "the payment panel did not open for Wallet");
            takeawayBill.Payment!.Reference = "WALLET-PAYTM-93021";
            notifications.Items.Clear();
            await DemoShotAsync("wallet-payment");
            Check(takeawayBill.Payment.NeedsReference, "Wallet does not ask for a reference");
            return $"Takeaway bill {card.TableCode}.";
        });

        await StepAsync("Paid state", "Confirm the Wallet payment", "The bill is PAID", async () =>
        {
            await GuardedAsync(terminal, takeawayBill!.ConfirmPaymentCommand.ExecuteAsync(null), "wallet-paid");
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("paid");
            Check(takeawayBill.IsPaid || takeawayBill.IsDonePanel, "wallet payment not completed: " + takeawayBill.PanelError);
            return null;
        });

        await StepAsync("Print receipt", "Press Print receipt (no receipt printer is set up on this terminal)", "A warning toast explains there is no receipt printer", async () =>
        {
            notifications.Items.Clear();
            await takeawayBill!.PrintReceiptCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("print-receipt-warning");
            Check(notifications.Items.Count > 0, "no toast after Print receipt");
            return notifications.Items.FirstOrDefault()?.Message;
        });

        await StepAsync("Queue: Paid chip", "Select the Paid chip in the queue", "Today's two paid bills are listed", async () =>
        {
            notifications.Items.Clear();
            await workspace!.SelectFilterCommand.ExecuteAsync(QueueFilter.Paid);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await DemoShotAsync("queue-paid-chip");
            var count = workspace.Bills.Count;
            await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.All);
            Check(count >= 2, $"{count} paid bills listed");
            return $"{count} paid bills.";
        });

        await StepAsync("Closed bills", "Open Closed Bills", "The settled bills are listed with the payments of the selected one", async () =>
        {
            var closed = await DemoOpenAsync<ClosedBillsViewModel>(shell, ModuleRegistry.ClosedBills, settleMs: 700);
            closed.SelectedBill = closed.Bills.FirstOrDefault(b => b.Bill.OrderType == OrderType.Takeaway) ?? closed.Bills.FirstOrDefault();
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(500);
            await DemoShotAsync("closed-bills");
            Check(closed.Bills.Count > 0, "no closed bills");
            return $"{closed.Bills.Count} closed bills.";
        });

        await StepAsync("Print log", "Open Print Log (Failed filter)", "The failed invoice print is listed", async () =>
        {
            var filter = printQueue.FailedCount > 0 ? PrintLogFilter.Failed : PrintLogFilter.All;
            await DemoOpenAsync<PrintLogViewModel>(shell, ModuleRegistry.PrintLog, new PrintLogContext(filter), 600);
            notifications.Items.Clear();
            await DemoShotAsync("print-log");
            Check(printQueue.FailedCount > 0, "no failed print job in the queue");
            return $"{printQueue.FailedCount} failed.";
        });

        await StepAsync("Pay-out", "Billing, Pay in/out: pay out 450 for vegetables", "The pay-out is recorded", async () =>
        {
            await DemoOpenAsync<CashierWorkspaceViewModel>(shell, ModuleRegistry.Billing);
            shell.ShiftBar.PayInOutCommand.Execute(null);
            var movement = shell.ShiftBar.Panel as CashMovementPanelViewModel ?? throw new DemoCheckException("the pay in/out panel did not open (no open shift?)");
            movement.SelectedType = movement.Types.Single(t => t.Type == CashMovementType.PayOut);
            movement.AmountText = "450";
            movement.Reason = "Vegetables from the market";
            await DemoShotAsync("pay-out");
            await GuardedAsync(terminal, movement.ConfirmCommand.ExecuteAsync(null), "pay-out");
            var recorded = shell.ShiftBar.Panel is null;
            shell.ShiftBar.ClosePanelCommand.Execute(null);
            Check(recorded, "pay-out not recorded: " + movement.ErrorMessage);
            return null;
        });

        ExpensesViewModel? expenses = null;
        await StepAsync("Expenses list", "Finance > Expenses (the Cashier role was granted expenses.view/manage for the demo)", "Today's expenses are listed", async () =>
        {
            expenses = await DemoOpenAsync<ExpensesViewModel>(shell, ModuleRegistry.Expenses, settleMs: 800);
            await DemoShotAsync("expenses-list");
            return $"{expenses.Expenses.Count} expenses; {expenses.SummaryText}";
        });

        if (expenses is not null)
        {
            await StepAsync("Record expense dialog", "Press Record expense, choose a category, amount, payee, notes", "The editor opens with the details filled in", async () =>
            {
                expenses.NewExpenseCommand.Execute(null);
                Check(expenses.Editor.IsOpen, "the expense editor did not open");
                expenses.Editor.SelectedCategory = expenses.Editor.CategoryChoices.FirstOrDefault();
                expenses.Editor.Amount = "850";
                expenses.Editor.Payee = "Sri Ganesh Vegetables";
                expenses.Editor.Notes = "Weekly vegetables (demo)";
                expenses.Editor.SelectedPaymentSource = expenses.Editor.PaymentSources.FirstOrDefault(s => s.Code != ExpensePaymentSources.CashDrawer) ?? expenses.Editor.PaymentSources.First();
                await DemoShotAsync("expense-record-dialog");
                Check(expenses.Editor.SelectedCategory is not null, "no category to choose");
                return null;
            });

            await StepAsync("Expense recorded", "Press Record expense", "The editor closes and the expense is in the list", async () =>
            {
                await expenses.SaveEditorCommand.ExecuteAsync(null);
                await Task.Delay(400);
                await DemoShotAsync("expense-recorded");
                Check(!expenses.Editor.IsOpen, "the editor stayed open: " + expenses.Editor.ErrorMessage);
                Check(expenses.Expenses.Any(e => e.Payee == "Sri Ganesh Vegetables"), "the new expense is not in the list");
                return null;
            });

            await DemoRecoverAsync("recording an expense", terminal);
        }

        await StepAsync("X report", "Press X report in the shift bar", "The mid-shift report opens", async () =>
        {
            await DemoOpenAsync<CashierWorkspaceViewModel>(shell, ModuleRegistry.Billing);
            await shell.ShiftBar.XReportCommand.ExecuteAsync(null);
            Check(shell.ShiftBar.Panel is ShiftReportPanelViewModel, "the X report panel did not open");
            await DemoShotAsync("x-report");
            shell.ShiftBar.ClosePanelCommand.Execute(null);
            return null;
        });

        await StepAsync("Notifications bell", "Go to the cashier home and open the bell", "The notices received in this session are listed", async () =>
        {
            await DemoOpenAsync<CashierHomeViewModel>(shell, ModuleRegistry.CashierHome);
            notifications.Items.Clear();
            shell.ToggleNoticesCommand.Execute(null);
            await DemoShotAsync("notifications-bell");
            shell.ToggleNoticesCommand.Execute(null);
            return null;
        });

        await StepAsync("Close shift", "Close shift with the drawer counted 50 short and a reason", "The shift is closed", async () =>
        {
            await shell.ShiftBar.CloseShiftCommand.ExecuteAsync(null);
            var panel = shell.ShiftBar.Panel as ShiftCountPanelViewModel ?? throw new DemoCheckException("the close-shift panel did not open");
            FillSheet(panel.Sheet, (panel.Sheet.Expected ?? 3000m) - 50m);
            panel.Reason = "Change given twice";
            await DemoShotAsync("close-shift");
            await GuardedAsync(terminal, panel.ConfirmCommand.ExecuteAsync(null), "close-shift");
            var closed = shell.ShiftBar.Shift is null;
            await DemoShotAsync("close-shift-z-report");
            shell.ShiftBar.ClosePanelCommand.Execute(null);
            Check(closed, "shift not closed: " + panel.ErrorMessage);
            return null;
        });

        await terminal.Get<IAppNavigator>().LogoutAsync();
    }

    // ---------------------------------------------------------------- chapter 05: manager

    private async Task DemoManagerChapterAsync()
    {
        // Live orders for the dashboard, the Orders page and the kitchen display: a takeaway order the kitchen has not
        // touched and an order on the table with ready tickets. Both are cancelled again at the end.
        var cleanup = new List<int>();
        cleanup.Add(await PrepareTakeawayAsync(kitchenReady: false, requestBill: false, "Ravi (phone order)"));
        cleanup.Add(await PrepareTableAsync(kitchenReady: true, requestBill: false));

        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        try
        {
            var shell = await DemoSignInAsync(terminal, "manager1", "Pass@123", "sign-in");

            await StepAsync("Dashboard", "Land on the manager dashboard", "Five KPI cards, live order status, active orders, hourly sales bars, staff today and the table cards are populated", async () =>
            {
                var dashboard = shell.CurrentPage as DashboardViewModel ?? await DemoOpenAsync<DashboardViewModel>(shell, ModuleRegistry.Dashboard);
                await Task.Delay(1500);
                ClearToasts();
                await DemoShotAsync("dashboard-kpis");
                await DemoShotAsync("dashboard-middle", fraction: 0.5);
                await DemoShotAsync("dashboard-bottom", v => v.ViewportWidth > 400);
                Check(dashboard.Cards.Count == 5, $"{dashboard.Cards.Count} KPI cards instead of 5");
                Check(dashboard.OrderStatusSlices.Count > 0, "live order status is empty");
                Check(dashboard.ActiveOrderRows.Count > 0, "no active orders");
                Check(dashboard.HourlyBars.Count > 0, "Hourly sales has no bars");
                Check(dashboard.Staff.Count > 0, "Staff today is empty");
                Check(dashboard.TableTiles.Count > 0, "no table status cards");
                return $"{dashboard.Cards.Count} cards, {dashboard.HourlyBars.Count} hourly bars, {dashboard.Staff.Count} staff rows, {dashboard.TableTiles.Count} table cards.";
            });

            await StepAsync("Orders page", "Open Orders", "Every active order is listed with channel chips; a takeaway row shows", async () =>
            {
                var orders = await DemoOpenAsync<ActiveOrdersViewModel>(shell, ModuleRegistry.Orders, settleMs: 800);
                var takeaway = orders.Rows.FirstOrDefault(r => r.Order.OrderType == OrderType.Takeaway);
                orders.SelectedRow = takeaway ?? orders.Rows.FirstOrDefault();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await DemoShotAsync("orders");
                Check(orders.Rows.Count > 0, "no active orders");
                Check(takeaway is not null, "no takeaway row");
                Check(takeaway!.Order.TableCode.StartsWith("TA-", StringComparison.Ordinal), $"takeaway label '{takeaway.Order.TableCode}'");
                return $"{orders.Rows.Count} orders.";
            });

            ReservationsViewModel? reservations = null;
            await StepAsync("Reservations list", "Live Operations > Reservations", "Today's reservations are listed", async () =>
            {
                reservations = await DemoOpenAsync<ReservationsViewModel>(shell, ModuleRegistry.Reservations, settleMs: 800);
                await DemoShotAsync("reservations-list");
                return $"{reservations.Reservations.Count} reservations today. {reservations.SummaryText}";
            });

            var guest = "Demo Guest " + DateTime.Now.ToString("HHmm", CultureInfo.InvariantCulture);
            if (reservations is not null)
            {
                await StepAsync("New reservation form", "Press New reservation, pick table DP1, guest, party and time", "The form opens with the details filled in", async () =>
                {
                    reservations.NewReservationCommand.Execute(null);
                    Check(reservations.Editor.IsOpen, "the reservation form did not open");
                    reservations.Editor.SelectedTable = reservations.Editor.TableChoices.FirstOrDefault(t => t.Display.Contains(DemoTable1)) ?? reservations.Editor.TableChoices.FirstOrDefault();
                    reservations.Editor.CustomerName = guest;
                    reservations.Editor.CustomerPhone = "9840099001";
                    reservations.Editor.PartySize = 4;
                    var start = DateTime.Now.AddMinutes(20);
                    start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute / 5 * 5, 0);
                    var end = start.AddMinutes(60);
                    if (end.Date != start.Date)
                    {
                        start = DateTime.Today.AddHours(22);
                        end = start.AddMinutes(60);
                    }

                    reservations.Editor.Date = DateTime.Today;
                    reservations.Editor.StartTime = start.ToString("HH:mm", CultureInfo.InvariantCulture);
                    reservations.Editor.EndTime = end.ToString("HH:mm", CultureInfo.InvariantCulture);
                    reservations.Editor.Notes = "Window seat, birthday (demo)";
                    await DemoShotAsync("reservation-new-form");
                    Check(reservations.Editor.SelectedTable is not null, "no table to choose");
                    return $"{reservations.Editor.SelectedTable!.Display} at {reservations.Editor.StartTime}.";
                });

                await StepAsync("Reservation created", "Press Create reservation", "The form closes and the reservation is in the list as Booked", async () =>
                {
                    ClearToasts();
                    await reservations.SaveReservationCommand.ExecuteAsync(null);
                    if (reservations.Editor.IsOpen)
                    {
                        var error = reservations.Editor.ErrorMessage;
                        // A leftover booking of an earlier run may overlap: try the other demo table.
                        reservations.Editor.SelectedTable = reservations.Editor.TableChoices.FirstOrDefault(t => t.Display.Contains(DemoTable2)) ?? reservations.Editor.SelectedTable;
                        await reservations.SaveReservationCommand.ExecuteAsync(null);
                        if (reservations.Editor.IsOpen)
                        {
                            await DemoShotAsync("reservation-create-error");
                            throw new DemoCheckException("create reservation: " + error + " / " + reservations.Editor.ErrorMessage);
                        }
                    }

                    reservations.SelectedReservation = reservations.Reservations.FirstOrDefault(r => r.CustomerName == guest);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    await DemoShotAsync("reservation-created");
                    Check(reservations.SelectedReservation is not null, "the new reservation is not in the list");
                    Check(reservations.SelectedReservation!.Status == ReservationStatus.Booked, $"status {reservations.SelectedReservation.Status}");
                    return $"{guest} on {reservations.SelectedReservation.TableText}.";
                });

                await StepAsync("Seat reservation", "Press Seat on the new reservation", "The reservation becomes Seated and the table is occupied", async () =>
                {
                    var item = reservations.SelectedReservation ?? throw new DemoCheckException("no reservation selected");
                    ClearToasts();
                    await reservations.SeatCommand.ExecuteAsync(item);
                    await Task.Delay(500);
                    reservations.SelectedReservation = reservations.Reservations.FirstOrDefault(r => r.CustomerName == guest);
                    await DemoShotAsync("reservation-seated");
                    Check(reservations.SelectedReservation?.Status == ReservationStatus.Seated, $"status is {reservations.SelectedReservation?.Status}");
                    return $"Table {item.TableCode} occupied (released again at the end).";
                });
            }

            await StepAsync("Tables: Map mode", "Open Tables and switch to Map", "Sections with a saved floor plan show the canvas", async () =>
            {
                var map = await DemoOpenAsync<TableMapViewModel>(shell, ModuleRegistry.Tables, settleMs: 800);
                map.ViewMode = TableMapViewModel.MapMode;
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(300);
                await DemoShotAsync("tables-map");
                Check(map.Sections.Any(s => s.UseCanvas), "no section shows the floor-plan canvas in Map mode");
                return null;
            });

            await StepAsync("Tables: List mode", "Switch to List", "All sections show tile lists", async () =>
            {
                var map = (TableMapViewModel)shell.CurrentPage!;
                map.ViewMode = TableMapViewModel.ListMode;
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(300);
                await DemoShotAsync("tables-list");
                Check(map.Sections.All(s => !s.UseCanvas), "a section still shows the canvas in List mode");
                return null;
            });

            await StepAsync("Kitchen display", "Open Kitchen Display", "The takeaway ticket is New and the table ticket is Ready", async () =>
            {
                var kitchen = await DemoOpenAsync<KitchenDisplayViewModel>(shell, ModuleRegistry.KitchenDisplay, settleMs: 1000);
                await DemoShotAsync("kitchen-display");
                Check(kitchen.NewTickets.Count + kitchen.PreparingTickets.Count + kitchen.ReadyTickets.Count > 0, "no tickets on the display");
                return $"{kitchen.NewHeader}, {kitchen.PreparingHeader}, {kitchen.ReadyHeader}; takeaway ticket: {kitchen.NewTickets.Concat(kitchen.PreparingTickets).Concat(kitchen.ReadyTickets).Any(t => t.IsTakeaway)}.";
            });

            ReportsViewModel? reports = null;
            async Task RunReportAsync(string key, string shot, Func<ReportsViewModel, string?>? extra = null)
            {
                reports ??= await DemoOpenAsync<ReportsViewModel>(shell, ModuleRegistry.Reports, settleMs: 600);
                reports.SelectedReport = ReportsViewModel.Catalogue.Single(r => r.Key == key);
                await reports.RunCommand.ExecuteAsync(null);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(300);
                await DemoShotAsync(shot);
                Check(reports.ErrorMessage is null, "report error: " + reports.ErrorMessage);
                Check(reports.Table is not null, "the report returned no table");
                if (extra?.Invoke(reports) is { } problem)
                {
                    throw new DemoCheckException(problem);
                }
            }

            await StepAsync("Report: Daily sales", "Reports > Daily sales > Run", "The table loads for the default range", async () =>
            {
                await RunReportAsync(ReportKeys.DailySales, "report-daily-sales");
                return reports!.SummaryText;
            });

            await StepAsync("Report: Hourly sales", "Choose Hourly sales > Run", "Hours with sales are listed", async () =>
            {
                await RunReportAsync(ReportKeys.HourlySales, "report-hourly-sales");
                Check(reports!.Table!.Rows.Count > 0, "no hourly rows");
                return reports.SummaryText;
            });

            await StepAsync("Report: Profit & Loss", "Choose Profit & Loss > Run", "Sales minus the expenses by category", async () =>
            {
                await RunReportAsync(ReportKeys.ProfitLoss, "report-profit-loss");
                Check(reports!.Table!.Rows.Count > 0, "no profit & loss rows");
                return reports.SummaryText;
            });

            await StepAsync("Report: Payments", "Choose Payments > Run", "The payment methods are listed and Wallet is among them", async () =>
            {
                await RunReportAsync(ReportKeys.Payments, "report-payments");
                var wallet = reports!.Table!.Rows.Cast<System.Data.DataRow>().Any(r => r.ItemArray.Any(c => c?.ToString()?.Contains("Wallet", StringComparison.OrdinalIgnoreCase) == true));
                Check(wallet, "no Wallet row in the Payments report");
                return reports.SummaryText;
            });

            var exports = Path.Combine(Path.GetTempPath(), "hotelpos-live-exports");
            Directory.CreateDirectory(exports);
            terminal.Get<IFilePicker>().PickSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => Path.Combine(exports, call.ArgAt<string>(0)));

            await StepAsync("Export Excel", "Press Export Excel (the file picker is stubbed to a temp folder)", "The workbook is saved and a Saved toast with an Open action appears", async () =>
            {
                ClearToasts();
                var before = Directory.GetFiles(exports, "*.xlsx").Length;
                await DemoShotAsync("report-export-excel-before");
                await reports!.ExportExcelCommand.ExecuteAsync(null);
                await DemoShotAsync("report-export-excel-saved");
                Check(Directory.GetFiles(exports, "*.xlsx").Length >= Math.Max(1, before), "no .xlsx was written");
                return "The Windows Save dialog itself is stubbed in the harness and cannot be captured.";
            });

            await StepAsync("Save PDF", "Press Save PDF", "A PDF is written and the Saved toast appears", async () =>
            {
                ClearToasts();
                await reports!.SavePdfCommand.ExecuteAsync(null);
                await DemoShotAsync("report-save-pdf");
                Check(Directory.GetFiles(exports, "*.pdf").Length > 0, "no .pdf was written");
                return null;
            });

            await StepAsync("Day End pre-check", "Open Day End (the day is not closed)", "The pre-close checks are listed", async () =>
            {
                await DemoOpenAsync<DayEndViewModel>(shell, ModuleRegistry.DayEnd, settleMs: 1000);
                await DemoShotAsync("day-end-precheck");
                return null;
            });

            await StepAsync("GST current month", "Open Tax Returns for the current month and run", "The month's GST summary loads", async () =>
            {
                var gst = await DemoOpenAsync<GstHubViewModel>(shell, ModuleRegistry.Gst, settleMs: 600);
                gst.Year = DateTime.Today.Year;
                gst.Month = DateTime.Today.Month;
                await gst.RunCommand.ExecuteAsync(null);
                await Task.Delay(300);
                await DemoShotAsync("gst-current-month");
                return null;
            });

            await StepAsync("Audit log filter", "Open Audit Log, filter user cashier1, action 'Bill.'", "Only the cashier's bill actions are listed", async () =>
            {
                var audit = await DemoOpenAsync<AuditLogsViewModel>(shell, ModuleRegistry.Audit, settleMs: 800);
                audit.UserFilter = "cashier1";
                audit.ActionFilter = "Bill.";
                await audit.SearchCommand.ExecuteAsync(null);
                audit.SelectedEntry = audit.AuditEntries.FirstOrDefault();
                await DemoShotAsync("audit-log-filter");
                Check(audit.AuditEntries.Count > 0, "the filter found no entries");
                Check(audit.AuditEntries.All(e => e.Action.StartsWith("Bill.", StringComparison.OrdinalIgnoreCase)), "an entry outside the filter is listed");
                return $"{audit.AuditEntries.Count} entries.";
            });

            await StepAsync("Dark dashboard", "Back on the dashboard switch to the dark theme", "The dashboard re-renders in the dark theme", async () =>
            {
                await DemoOpenAsync<DashboardViewModel>(shell, ModuleRegistry.Dashboard, settleMs: 1200);
                ClearToasts();
                terminal.Theme.Apply(ThemeService.Dark);
                try
                {
                    await DemoShotAsync("dashboard-dark");
                }
                finally
                {
                    terminal.Theme.Apply(ThemeService.Light);
                }

                return null;
            });

            await terminal.Get<IAppNavigator>().LogoutAsync();
        }
        finally
        {
            await DemoCleanupAsync(cleanup);
        }
    }

    // Leaves the dev database as found: the live demo orders are cancelled and the occupied demo table is released.
    private async Task DemoCleanupAsync(IReadOnlyList<int> orderIds)
    {
        try
        {
            await using var manager = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin, headless: true);
            await manager.SignInAsync("manager1", "Pass@123");
            foreach (var id in orderIds)
            {
                var cancelled = await manager.Get<IOrdersApi>().CancelAsync(id, new CancelOrderRequest { Reason = "Demo session clean-up" });
                Note(cancelled.Success ? $"Order {id} cancelled." : $"Order {id} cancel failed: {cancelled.Message}");
            }

            var floor = manager.Get<IFloorApi>();
            var map = await floor.GetMapAsync();
            foreach (var table in map.Data?.Sections.SelectMany(s => s.Tables).Where(t => t.Code is DemoTable1 or DemoTable2 && t.Status == TableStatus.Occupied) ?? Enumerable.Empty<TableDto>())
            {
                var released = await floor.ReleaseAsync(table.Id);
                Note(released.Success ? $"Table {table.Code} released." : $"Table {table.Code} release failed: {released.Message}");
            }
        }
        catch (Exception ex)
        {
            Note("Demo clean-up failed: " + ex.Message);
        }
    }
}
