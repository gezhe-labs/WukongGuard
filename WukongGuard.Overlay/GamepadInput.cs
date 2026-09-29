using System.Runtime.InteropServices;

namespace WukongGuard.Overlay;

internal static class GamepadInput
{
    internal const ushort Menu = 0x0010;
    internal const ushort A = 0x1000;
    internal const ushort B = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        internal ushort Buttons;
        internal byte LeftTrigger;
        internal byte RightTrigger;
        internal short LeftX;
        internal short LeftY;
        internal short RightX;
        internal short RightY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        internal uint PacketNumber;
        internal Gamepad Pad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out State state);

    internal static bool TryGetButtons(uint userIndex, out ushort buttons)
    {
        buttons = 0;
        try
        {
            if (XInputGetState(userIndex, out var state) != 0) return false;
            buttons = state.Pad.Buttons;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
