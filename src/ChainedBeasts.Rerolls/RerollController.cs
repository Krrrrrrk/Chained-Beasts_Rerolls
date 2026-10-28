using ChainedBeasts.Rerolls.Core;
using Il2CppInterop.Runtime.Attributes;
using Unity.Netcode;
using UnityEngine;

namespace ChainedBeasts.Rerolls;

public sealed class RerollController : MonoBehaviour
{
    private readonly RerollView _view = new();
    private double _next;
    public RerollController(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        if (Plugin.Current == null) { UnityEngine.Object.Destroy(this); return; }
        var now = Time.realtimeSinceStartupAsDouble;
        if (now < _next) return; _next = now + .1;
        Plugin.Current.Tick();
    }
    public void OnGUI()
    {
        var service = Plugin.Current;
        if (service?.Visible == null || NetworkManager.Singleton == null || Screen.width < 1 || Screen.height < 1) return;
        try { _view.Draw(service); }
        catch (Exception e) { Plugin.Logger.LogWarning("Reroll controls could not draw: " + e.GetType().Name); }
    }
    public void OnDestroy() { _view.Dispose(); }
}
