#if RUNTIME_PROBE
using System;
using System.Threading;
using System.Reflection;
using b1;
using CSharpModBase;
using UnrealEngine.Engine;
using UnrealEngine.Runtime;

namespace WukongStateProbe
{
    // Samples only on the game thread. Logs on changes, with one queued callback at most.
    internal static class RuntimeMonitor
    {
        private static Timer timer;
        private static volatile bool active;
        private static int queued;
        private static string lastState;
        private static bool errorLogged;
        private static bool dispatchLogged;
        private static bool callbackLogged;

        internal static void Start()
        {
            active = true;
            timer = new Timer(QueueSample, null, 1000, 1000);
            TraceLog.Write("[WukongGuard] change monitor started (1 s)");
        }

        internal static void Stop()
        {
            active = false;
            timer?.Dispose();
            timer = null;
            lastState = null;
            Interlocked.Exchange(ref queued, 0);
        }

        private static void QueueSample(object _)
        {
            if (!active || Interlocked.Exchange(ref queued, 1) != 0) return;
            try
            {
                if (!dispatchLogged)
                {
                    dispatchLogged = true;
                    TraceLog.Write("[WukongGuard] monitor dispatching first sample");
                }
                Utils.TryRunOnGameThread(() =>
                {
                    try
                    {
                        if (!callbackLogged)
                        {
                            callbackLogged = true;
                            TraceLog.Write("[WukongGuard] monitor first game-thread callback");
                        }
                        if (active) Sample();
                    }
                    finally { Interlocked.Exchange(ref queued, 0); }
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref queued, 0);
                if (!errorLogged)
                {
                    errorLogged = true;
                    TraceLog.Write("[WukongGuard] change monitor scheduling error: " + ex);
                }
            }
        }

        private static void Sample()
        {
            try
            {
                UObjectRef worldRef = GCHelper.FindRef(FGlobals.GWorld);
                UWorld world = worldRef?.Managed as UWorld;
                var controller = world == null ? null : UGSE_EngineFuncLib.GetFirstLocalPlayerController(world);
                var roleData = controller == null ? null : controller.GetType()
                    .GetMethod("GetReadOnlyDataTodoRemove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(controller, null) as IBPC_PlayerRoleData;
                if (roleData == null) return;
                var chapter = roleData.RoleData?.RoleCs?.Chapter?.CurChapter;
                string state = "chapter=" + (chapter?.ToString() ?? "null")
                    + " mapId=" + roleData.MapId
                    + " areaId=" + roleData.MapAreaId;
                if (state == lastState) return;
                lastState = state;
                TraceLog.Write("[WukongGuard] state change " + state);
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    TraceLog.Write("[WukongGuard] change monitor error: " + ex);
                }
            }
        }
    }
}
#endif
