using Microsoft.JSInterop;
using VePack.Guidance;

namespace Mitch;

public class ArcGisRuntime
{

    private static IJSRuntime _js;

    public ArcGisRuntime(IJSRuntime js)
    {
        _js = js;
        _js.InvokeVoidAsync("initialize");
    }

    public async static Task SetPath(WgsPathData path, List<int> color)
    {
        await _js.InvokeVoidAsync("setPath", path.Points, path.Id, color);
    }

    public async static Task RemovePath(WgsPathData path)
    {
        await _js.InvokeVoidAsync("removePath", path.Id);
    }

    public async static Task ClearMap()
    {
        await _js.InvokeVoidAsync("clearMap");
    }

    public static async Task SetCenter(double lon, double lat)
    {
        await _js.InvokeVoidAsync("setCenter", lon, lat);
    }

    [JSInvokable]
    public static void Notify()
    {
        Util.ChangeNotifier.OnNext(null);
    }

}
