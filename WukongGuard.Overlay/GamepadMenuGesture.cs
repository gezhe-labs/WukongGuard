namespace WukongGuard.Overlay;

// A hold must start after the toast appears and after a release has been observed.
internal sealed class GamepadMenuGesture
{
    private readonly bool[] ready = new bool[4];
    private readonly bool[] holding = new bool[4];
    private readonly bool[] consumed = new bool[4];
    private readonly long[] pressedAt = new long[4];

    internal void Reset()
    {
        Array.Clear(ready);
        Array.Clear(holding);
        Array.Clear(consumed);
        Array.Clear(pressedAt);
    }

    internal bool Update(int index, bool connected, bool held, long nowMilliseconds)
    {
        if (!connected)
        {
            ready[index] = holding[index] = consumed[index] = false;
            return false;
        }
        if (!held)
        {
            ready[index] = true;
            holding[index] = consumed[index] = false;
            return false;
        }
        if (!ready[index] || consumed[index]) return false;
        if (!holding[index])
        {
            holding[index] = true;
            pressedAt[index] = nowMilliseconds;
        }
        if (nowMilliseconds - pressedAt[index] < 1000) return false;
        consumed[index] = true;
        return true;
    }
}
