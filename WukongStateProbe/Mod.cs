using System;
using CSharpModBase;

namespace WukongStateProbe
{
    // B1CSharpLoader creates this class and calls Init / DeInit.
    public sealed class Mod : ICSharpMod
    {
        public string Name { get { return "WukongStateProbe"; } }
        public string Version { get { return "0.1.0"; } }

        public void Init()
        {
            TraceLog.Write("[WukongGuard] loaded");
#if RUNTIME_PROBE
            RuntimeProbe.Register();
#endif
        }

        public void DeInit()
        {
#if RUNTIME_PROBE
            RuntimeProbe.Unregister();
#endif
            TraceLog.Write("[WukongGuard] unloaded");
        }
    }
}
