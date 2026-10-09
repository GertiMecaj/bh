using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BHComboTrigger;

internal static class InputEngine
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static async Task ExecuteAsync(
        ComboDefinition combo,
        Facing facing,
        int intervalMs,
        KeyMap map,
        CancellationToken cancellationToken,
        Action<string>? status = null)
    {
        intervalMs = Math.Clamp(intervalMs, 1, 500);
        var tokens = combo.Script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool airborne = false;
        bool gravityCancelPrimed = false;

        try
        {
            ReleaseMovement(map);
            await Delay(Math.Min(intervalMs, 20), cancellationToken);

            foreach (var raw in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = raw.ToUpperInvariant();
                status?.Invoke(TokenLabel(token));

                switch (token)
                {
                    case "JUMP":
                        await TapAsync(map.Jump, intervalMs, cancellationToken);
                        airborne = true;
                        continue;

                    case "GROUND":
                        ReleaseMovement(map);
                        await Delay(Math.Max(intervalMs, 18), cancellationToken);
                        airborne = false;
                        gravityCancelPrimed = false;
                        continue;

                    case "GC":
                        if (!airborne)
                        {
                            await TapAsync(map.Jump, intervalMs, cancellationToken);
                            airborne = true;
                        }
                        ReleaseMovement(map);
                        await TapAsync(map.Dodge, intervalMs, cancellationToken);
                        gravityCancelPrimed = true;
                        continue;

                    case "CD_FWD":
                    case "DASH_FWD":
                        await ChordAsync(new[] { Forward(map, facing) }, map.Dodge, intervalMs, cancellationToken);
                        continue;

                    case "CD_BACK":
                        await ChordAsync(new[] { Back(map, facing) }, map.Dodge, intervalMs, cancellationToken);
                        continue;

                    case "DASH_JUMP":
                        await ChordAsync(new[] { Forward(map, facing) }, map.Dodge, Math.Max(1, intervalMs / 2), cancellationToken);
                        await TapAsync(map.Jump, intervalMs, cancellationToken);
                        airborne = true;
                        continue;

                    case "BACK_NAIR":
                        if (!airborne)
                        {
                            await TapAsync(map.Jump, intervalMs, cancellationToken);
                            airborne = true;
                        }
                        await ChordAsync(new[] { Back(map, facing), map.AimUp }, map.Light, intervalMs, cancellationToken);
                        continue;

                    case "AI_DLIGHT_FWD":
                        await ChordAsync(new[] { map.Down, Forward(map, facing) }, map.Light, intervalMs, cancellationToken);
                        airborne = false;
                        gravityCancelPrimed = false;
                        continue;
                }

                bool aerialMove = IsAerialMove(token);
                bool groundMove = IsGroundMove(token);

                if (aerialMove && !airborne)
                {
                    await TapAsync(map.Jump, intervalMs, cancellationToken);
                    airborne = true;
                }

                await ExecuteMoveAsync(token, facing, intervalMs, map, cancellationToken);

                if (groundMove)
                {
                    if (gravityCancelPrimed)
                    {
                        airborne = true;
                        gravityCancelPrimed = false;
                    }
                    else
                    {
                        airborne = false;
                    }
                }
                else if (aerialMove)
                {
                    airborne = true;
                }
            }
        }
        finally
        {
            ReleaseAll(map);
        }
    }

    private static async Task ExecuteMoveAsync(
        string token,
        Facing facing,
        int intervalMs,
        KeyMap map,
        CancellationToken cancellationToken)
    {
        switch (token)
        {
            case "NLIGHT":
            case "NAIR":
                await ChordAsync(new[] { map.AimUp }, map.Light, intervalMs, cancellationToken);
                break;

            case "SLIGHT":
            case "SAIR":
                await ChordAsync(new[] { Forward(map, facing) }, map.Light, intervalMs, cancellationToken);
                break;

            case "DLIGHT":
            case "DAIR":
                await ChordAsync(new[] { map.Down }, map.Light, intervalMs, cancellationToken);
                break;

            case "NSIG":
            case "REC":
                await ChordAsync(new[] { map.AimUp }, map.Heavy, intervalMs, cancellationToken);
                break;

            case "SSIG":
                await ChordAsync(new[] { Forward(map, facing) }, map.Heavy, intervalMs, cancellationToken);
                break;

            case "DSIG":
            case "GP":
                await ChordAsync(new[] { map.Down }, map.Heavy, intervalMs, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unsupported combo token: {token}");
        }
    }

    private static bool IsAerialMove(string token) => token is "NAIR" or "SAIR" or "DAIR" or "REC" or "GP";
    private static bool IsGroundMove(string token) => token is "NLIGHT" or "SLIGHT" or "DLIGHT" or "NSIG" or "SSIG" or "DSIG";

    private static Keys Forward(KeyMap map, Facing facing) => facing == Facing.Right ? map.Right : map.Left;
    private static Keys Back(KeyMap map, Facing facing) => facing == Facing.Right ? map.Left : map.Right;

    private static async Task TapAsync(Keys key, int intervalMs, CancellationToken cancellationToken)
    {
        int holdMs = HoldDuration(intervalMs);
        KeyDown(key);
        await Delay(holdMs, cancellationToken);
        KeyUp(key);
        await Delay(intervalMs, cancellationToken);
    }

    private static async Task ChordAsync(
        IEnumerable<Keys> modifiers,
        Keys action,
        int intervalMs,
        CancellationToken cancellationToken)
    {
        var keys = modifiers.Where(k => k != Keys.None).Distinct().ToArray();
        int settleMs = Math.Clamp(intervalMs / 4, 2, 10);
        int holdMs = HoldDuration(intervalMs);

        foreach (var key in keys) KeyDown(key);
        await Delay(settleMs, cancellationToken);

        KeyDown(action);
        await Delay(holdMs, cancellationToken);
        KeyUp(action);

        for (int i = keys.Length - 1; i >= 0; i--) KeyUp(keys[i]);
        await Delay(intervalMs, cancellationToken);
    }

    private static int HoldDuration(int intervalMs) => Math.Clamp(intervalMs / 3, 5, 16);

    private static Task Delay(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(Math.Max(1, milliseconds), cancellationToken);

    private static void KeyDown(Keys key) => SendKey(key, false);
    private static void KeyUp(Keys key) => SendKey(key, true);

    private static void SendKey(Keys key, bool keyUp)
    {
        if (key == Keys.None) return;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput failed for {key}.");
        }
    }

    private static void ReleaseMovement(KeyMap map)
    {
        foreach (var key in new[] { map.Left, map.Right, map.AimUp, map.Down })
            KeyUp(key);
    }

    public static void ReleaseAll(KeyMap map)
    {
        foreach (var key in new[]
        {
            map.Left, map.Right, map.AimUp, map.Jump, map.Down,
            map.Light, map.Heavy, map.Throw, map.Dodge
        }.Distinct())
        {
            try { KeyUp(key); } catch { }
        }
    }

    private static string TokenLabel(string token) => token switch
    {
        "NLIGHT" => "NLight",
        "SLIGHT" => "SLight",
        "DLIGHT" => "DLight",
        "NAIR" => "NAir",
        "SAIR" => "SAir",
        "DAIR" => "DAir",
        "REC" => "Recovery",
        "GP" => "Ground Pound",
        "NSIG" => "NSig",
        "SSIG" => "SSig",
        "DSIG" => "DSig",
        "GC" => "Gravity Cancel",
        "CD_FWD" => "Chase Dodge",
        "CD_BACK" => "Reverse Chase Dodge",
        "DASH_FWD" => "Dash",
        "DASH_JUMP" => "Dash Jump",
        "BACK_NAIR" => "Back-hit NAir",
        "AI_DLIGHT_FWD" => "Active-input DLight",
        "GROUND" => "Landing window",
        "JUMP" => "Jump",
        _ => token
    };
}
