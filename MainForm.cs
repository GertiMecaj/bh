using System.Runtime.InteropServices;

namespace BHComboTrigger;

internal sealed class MainForm : Form
{
    const int WM_HOTKEY = 0x0312, HK_TRIGGER = 1001, HK_STOP = 1002;
    const uint MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly AppSettings settings = AppSettings.Load();
    readonly ComboBox weapon = Box(), combo = Box(), trigger = Box(), facing = Box();
    readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 500, Value = 35, Width = 90 };
    readonly CheckBox enabled = new() { Text = "Enable global trigger", AutoSize = true }, topmost = new() { Text = "Always on top", AutoSize = true };
    readonly Label notes = new() { AutoSize = true, MaximumSize = new Size(720, 0) }, status = new() { AutoSize = true };
    readonly Button run = new() { Text = "RUN SELECTED COMBO ONCE", AutoSize = true }, stop = new() { Text = "STOP (F12)", AutoSize = true };
    readonly Dictionary<string, ComboBox> binds = new();
    bool loading = true, executing;
    CancellationTokenSource? cts;

    public MainForm()
    {
        Text = "BH Combo Trigger"; Size = new Size(860, 720); MinimumSize = new Size(760, 620); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(22, 26, 36); ForeColor = Color.WhiteSmoke; Font = new Font("Segoe UI", 10F); AutoScaleMode = AutoScaleMode.Dpi;
        Build(); Populate(); Apply(); loading = false; ShowCombo();
        Shown += (_, _) => RegisterKeys();
        FormClosing += (_, _) => { StopMacro(); Save(); if (IsHandleCreated) { UnregisterHotKey(Handle, HK_TRIGGER); UnregisterHotKey(Handle, HK_STOP); } };
    }

    static ComboBox Box() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390, BackColor = Color.FromArgb(45, 52, 70), ForeColor = Color.WhiteSmoke };
    static Label L(string s) => new() { Text = s, AutoSize = true, ForeColor = Color.FromArgb(180, 193, 210), Padding = new Padding(0, 6, 0, 0) };

    void Build()
    {
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "BH COMBO TRIGGER", AutoSize = true, Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold), ForeColor = Color.FromArgb(105, 205, 255) });
        root.Controls.Add(new Label { Text = "Pick one combo. Press the trigger once; the combo runs once and stops.", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(0, 0, 0, 12) });

        combo.DisplayMember = nameof(ComboDefinition.Name);
        var cg = Group("Combo", 800); var cp = Rows(); cg.Controls.Add(cp); cp.Controls.Add(L("Weapon")); cp.Controls.Add(weapon); cp.Controls.Add(L("True combo")); cp.Controls.Add(combo); cp.Controls.Add(L("Conditions")); cp.Controls.Add(notes); root.Controls.Add(cg);

        trigger.Items.AddRange(KeyChoices(true).Cast<object>().ToArray()); facing.Items.AddRange(new object[] { "Right", "Left" });
        var tg = Group("Trigger & timing", 800); var tp = Rows(); tg.Controls.Add(tp); tp.Controls.Add(L("Trigger key")); tp.Controls.Add(trigger); tp.Controls.Add(L("Facing")); tp.Controls.Add(facing); tp.Controls.Add(L("Input interval (ms)")); tp.Controls.Add(interval); tp.Controls.Add(enabled); tp.Controls.Add(topmost); root.Controls.Add(tg);

        var bg = Group("Brawlhalla key bindings", 800); var bp = Rows(); bg.Controls.Add(bp);
        AddBind(bp, "Move Left", "Left"); AddBind(bp, "Move Right", "Right"); AddBind(bp, "Aim Up", "AimUp"); AddBind(bp, "Jump", "Jump"); AddBind(bp, "Drop / Down", "Down"); AddBind(bp, "Quick Attack", "Light"); AddBind(bp, "Heavy Attack", "Heavy"); AddBind(bp, "Throw Item", "Throw"); AddBind(bp, "Dodge / Dash", "Dodge");
        bp.Controls.Add(new Label { Text = "Your screenshot showed Drop and Dodge/Dash unbound. Combos using DLight/DAir/GP or GC/CD/Dash require those actions. Bind them in Brawlhalla to the same keys shown here (defaults S and Left Shift), or change these mappings.", AutoSize = true, MaximumSize = new Size(720, 0), ForeColor = Color.FromArgb(255, 190, 95) });
        root.Controls.Add(bg);

        var actions = new FlowLayoutPanel { AutoSize = true, Width = 800 }; run.Padding = new Padding(12, 8, 12, 8); stop.Padding = new Padding(12, 8, 12, 8); run.BackColor = Color.FromArgb(32, 137, 197); run.ForeColor = Color.White; stop.BackColor = Color.FromArgb(125, 58, 68); stop.ForeColor = Color.White; actions.Controls.Add(run); actions.Controls.Add(stop); root.Controls.Add(actions);
        status.ForeColor = Color.FromArgb(115, 220, 165); status.Padding = new Padding(0, 8, 0, 8); root.Controls.Add(status);
        root.Controls.Add(new Label { Text = "F12 is the emergency stop. Timing, damage, Dexterity, spacing and patches can affect whether a route remains true. Release movement keys before triggering for consistent neutral inputs.", AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.Gray });

        weapon.SelectedIndexChanged += (_, _) => { if (loading) return; LoadCombos(); ShowCombo(); Save(); };
        combo.SelectedIndexChanged += (_, _) => { if (!loading) { ShowCombo(); Save(); } };
        trigger.SelectedIndexChanged += (_, _) => { if (!loading) { RegisterKeys(); Save(); } };
        facing.SelectedIndexChanged += (_, _) => { if (!loading) Save(); };
        interval.ValueChanged += (_, _) => { if (!loading) Save(); };
        enabled.CheckedChanged += (_, _) => { if (!loading) { RegisterKeys(); Save(); } };
        topmost.CheckedChanged += (_, _) => { TopMost = topmost.Checked; if (!loading) Save(); };
        run.Click += async (_, _) => await RunOnce(); stop.Click += (_, _) => StopMacro();
    }

    static GroupBox Group(string text, int width) => new() { Text = text, Width = width, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), ForeColor = Color.FromArgb(115, 205, 245), BackColor = Color.FromArgb(29, 34, 47), Margin = new Padding(0, 0, 0, 12) };
    static FlowLayoutPanel Rows() => new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = 760, Padding = new Padding(4) };

    void AddBind(Control parent, string label, string id)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Width = 730 }; var b = Box(); b.Width = 180; b.Items.AddRange(KeyChoices(false).Cast<object>().ToArray()); b.SelectedIndexChanged += (_, _) => { if (!loading) Save(); }; binds[id] = b; row.Controls.Add(new Label { Text = label, Width = 170, Padding = new Padding(0, 6, 0, 0), ForeColor = Color.LightGray }); row.Controls.Add(b); parent.Controls.Add(row);
    }

    static List<KeyChoice> KeyChoices(bool functions)
    {
        var r = new List<KeyChoice>(); for (char c = 'A'; c <= 'Z'; c++) r.Add(new(c.ToString(), (Keys)Enum.Parse(typeof(Keys), c.ToString())));
        r.AddRange(new[] { new KeyChoice("Space", Keys.Space), new("Left Shift", Keys.LShiftKey), new("Right Shift", Keys.RShiftKey), new("Left Ctrl", Keys.LControlKey), new("Right Ctrl", Keys.RControlKey), new("Up", Keys.Up), new("Down", Keys.Down), new("Left Arrow", Keys.Left), new("Right Arrow", Keys.Right) });
        if (functions) for (int i = 1; i <= 11; i++) r.Add(new($"F{i}", (Keys)((int)Keys.F1 + i - 1))); return r;
    }

    void Populate()
    {
        weapon.Items.AddRange(ComboLibrary.Weapons.Cast<object>().ToArray()); weapon.SelectedItem = ComboLibrary.Weapons.Contains(settings.Weapon) ? settings.Weapon : ComboLibrary.Weapons[0]; LoadCombos(settings.Combo);
    }
    void LoadCombos(string? wanted = null)
    {
        var list = ComboLibrary.ForWeapon(weapon.SelectedItem?.ToString() ?? ComboLibrary.Weapons[0]); combo.Items.Clear(); foreach (var x in list) combo.Items.Add(x); combo.SelectedItem = list.FirstOrDefault(x => x.Name == wanted) ?? list[0];
    }
    void Apply()
    {
        Pick(trigger, (Keys)settings.TriggerKey); interval.Value = Math.Clamp(settings.IntervalMs, 1, 500); facing.SelectedItem = settings.Facing == "Left" ? "Left" : "Right"; enabled.Checked = settings.TriggerEnabled; topmost.Checked = settings.TopMost; TopMost = settings.TopMost;
        Pick(binds["Left"], (Keys)settings.LeftKey); Pick(binds["Right"], (Keys)settings.RightKey); Pick(binds["AimUp"], (Keys)settings.AimUpKey); Pick(binds["Jump"], (Keys)settings.JumpKey); Pick(binds["Down"], (Keys)settings.DownKey); Pick(binds["Light"], (Keys)settings.LightKey); Pick(binds["Heavy"], (Keys)settings.HeavyKey); Pick(binds["Throw"], (Keys)settings.ThrowKey); Pick(binds["Dodge"], (Keys)settings.DodgeKey);
    }
    static void Pick(ComboBox box, Keys key) { foreach (var x in box.Items) if (x is KeyChoice k && k.Key == key) { box.SelectedItem = x; return; } if (box.Items.Count > 0) box.SelectedIndex = 0; }
    static Keys Key(ComboBox b) => b.SelectedItem is KeyChoice k ? k.Key : Keys.None;
    KeyMap Map() => new() { Left = Key(binds["Left"]), Right = Key(binds["Right"]), AimUp = Key(binds["AimUp"]), Jump = Key(binds["Jump"]), Down = Key(binds["Down"]), Light = Key(binds["Light"]), Heavy = Key(binds["Heavy"]), Throw = Key(binds["Throw"]), Dodge = Key(binds["Dodge"]) };
    void ShowCombo() { if (combo.SelectedItem is ComboDefinition c) notes.Text = c.Notes; }

    void RegisterKeys()
    {
        if (!IsHandleCreated) return; UnregisterHotKey(Handle, HK_TRIGGER); UnregisterHotKey(Handle, HK_STOP); RegisterHotKey(Handle, HK_STOP, MOD_NOREPEAT, (uint)Keys.F12);
        if (!enabled.Checked) { SetStatus("Trigger disabled.", Color.Silver); return; }
        var k = Key(trigger); if (k == Keys.None || k == Keys.F12) { SetStatus("Choose a trigger other than F12.", Color.Orange); return; }
        bool ok = RegisterHotKey(Handle, HK_TRIGGER, MOD_NOREPEAT, (uint)k);
        SetStatus(ok ? $"Ready — press {Friendly(k)}. F12 stops." : $"Could not register {Friendly(k)} globally.", ok ? Color.LightGreen : Color.IndianRed);
    }
    protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY) { int id = m.WParam.ToInt32(); if (id == HK_TRIGGER) { _ = RunOnce(); return; } if (id == HK_STOP) { StopMacro(); return; } } base.WndProc(ref m); }

    async Task RunOnce()
    {
        if (executing || combo.SelectedItem is not ComboDefinition c) return; executing = true; run.Enabled = false; cts?.Dispose(); cts = new(); var map = Map();
        try { SetStatus($"Running: {c.Name}", Color.SkyBlue); await InputEngine.ExecuteAsync(c, (facing.SelectedItem?.ToString() == "Left") ? Facing.Left : Facing.Right, (int)interval.Value, map, cts.Token, s => SafeStatus($"Running: {c.Name} | {s}", Color.SkyBlue)); SetStatus("Combo finished. Ready.", Color.LightGreen); }
        catch (OperationCanceledException) { SetStatus("Stopped. Ready.", Color.Orange); }
        catch (Exception ex) { SetStatus($"Input error: {ex.Message}", Color.IndianRed); }
        finally { InputEngine.ReleaseAll(map); executing = false; run.Enabled = true; }
    }
    void StopMacro() { if (cts is { IsCancellationRequested: false }) cts.Cancel(); try { InputEngine.ReleaseAll(Map()); } catch { } if (!executing) SetStatus("Ready.", Color.LightGreen); }
    void SafeStatus(string t, Color c) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke((Action)(() => SetStatus(t, c))); else SetStatus(t, c); }
    void SetStatus(string t, Color c) { status.Text = t; status.ForeColor = c; }
    static string Friendly(Keys k) => k switch { Keys.LShiftKey => "Left Shift", Keys.RShiftKey => "Right Shift", Keys.LControlKey => "Left Ctrl", Keys.RControlKey => "Right Ctrl", _ => k.ToString() };

    void Save()
    {
        if (loading) return; settings.TriggerKey = (int)Key(trigger); settings.IntervalMs = (int)interval.Value; settings.Facing = facing.SelectedItem?.ToString() ?? "Right"; settings.Weapon = weapon.SelectedItem?.ToString() ?? "Axe"; settings.Combo = (combo.SelectedItem as ComboDefinition)?.Name ?? ""; settings.TriggerEnabled = enabled.Checked; settings.TopMost = topmost.Checked;
        settings.LeftKey = (int)Key(binds["Left"]); settings.RightKey = (int)Key(binds["Right"]); settings.AimUpKey = (int)Key(binds["AimUp"]); settings.JumpKey = (int)Key(binds["Jump"]); settings.DownKey = (int)Key(binds["Down"]); settings.LightKey = (int)Key(binds["Light"]); settings.HeavyKey = (int)Key(binds["Heavy"]); settings.ThrowKey = (int)Key(binds["Throw"]); settings.DodgeKey = (int)Key(binds["Dodge"]); settings.Save();
    }
}
