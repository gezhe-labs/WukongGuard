param([int]$ProcessId)

$native = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ConsoleCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Coord { public short X, Y; public Coord(short x, short y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)]
    public struct SmallRect { public short Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BufferInfo
    {
        public Coord Size;
        public Coord CursorPosition;
        public ushort Attributes;
        public SmallRect Window;
        public Coord MaximumWindowSize;
    }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetConsoleScreenBufferInfo(IntPtr handle, out BufferInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ReadConsoleOutputCharacter(IntPtr handle, StringBuilder chars, uint length, Coord position, out uint read);
}
'@
Add-Type -TypeDefinition $native
[ConsoleCapture]::FreeConsole() | Out-Null
if (-not [ConsoleCapture]::AttachConsole([uint32]$ProcessId)) {
    throw "AttachConsole failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
}
try {
    $handle = [ConsoleCapture]::CreateFile('CONOUT$', [Convert]::ToUInt32('80000000', 16), [uint32]3, [IntPtr]::Zero, [uint32]3, [uint32]0, [IntPtr]::Zero)
    if ($handle -eq [IntPtr](-1)) { throw "CreateFile(CONOUT$) failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    $info = [ConsoleCapture+BufferInfo]::new()
    if (-not [ConsoleCapture]::GetConsoleScreenBufferInfo($handle, [ref]$info)) {
        throw "GetConsoleScreenBufferInfo failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
    $start = [Math]::Max(0, [int]$info.CursorPosition.Y - 80)
    for ($row = $start; $row -le [int]$info.CursorPosition.Y; $row++) {
        $buffer = [Text.StringBuilder]::new([int]$info.Size.X)
        $count = [uint32]0
        $position = [ConsoleCapture+Coord]::new([int16]0, [int16]$row)
        if ([ConsoleCapture]::ReadConsoleOutputCharacter($handle, $buffer, [uint32]$info.Size.X, $position, [ref]$count)) {
            $line = $buffer.ToString().TrimEnd()
            if ($line.Length -gt 0) { Write-Output $line }
        }
    }
}
finally {
    if ($handle -and $handle -ne [IntPtr](-1)) { [ConsoleCapture]::CloseHandle($handle) | Out-Null }
    [ConsoleCapture]::FreeConsole() | Out-Null
}
