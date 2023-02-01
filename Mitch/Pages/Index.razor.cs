using System.Reactive.Linq;
using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using VePack.Navigation;

namespace Mitch.Pages;

partial class Index
{

    // ------ fields ------ //

    private static IDisposable? _stateHandler;
    private static bool _uiDisabled;
    private static bool _menuOpened;
    private static int _openedMenuNumber;

    private static string _entrancePathInput = "";
    private static string _exitPathInput = "";
    private static string _workingStartPointInput = "";
    private static string _workingEndPointInput = "";

    private static bool _directionRight = true;
    private static int _workingPathCount = 1;
    private static double _workingWidth = 2.64;
    private static double _pointsInterval = 0.5;
    private static string _loadingMapFileName = "";

    private static Editor _editor;
    private static WgsPathData? _entrancePath;
    private static WgsPathData? _exitPath;
    private static WgsPointData? _workingStartPoint;
    private static WgsPointData? _workingEndPoint;

    private static string _mapSaveFileName = "tmp";
    private static bool _exportJmap;

    private static string _menuTabButtonFace => _menuOpened ? "<" : ">";


    // ------ constructors ------ //

    public Index()
    {
        _stateHandler?.Dispose();
        _stateHandler = Util.ChangeNotifier.Subscribe(_ =>
        {
            try
            {
                InvokeAsync(() =>
                {
                    try { StateHasChanged(); }
                    catch (ObjectDisposedException) { }

                });
            }
            catch (InvalidOperationException) { }
        });
    }


    // ------ methods ------ //

    public void MenuTabClick(int number)
    {
        if (_openedMenuNumber == number)
            _openedMenuNumber = 0;
        else
            _openedMenuNumber = number;
    }


    // ----------- import ----------- //

    public async Task InputEntrancePathAsync(InputFileChangeEventArgs e)
    {
        var txt = await e.File.OpenReadStream(e.File.Size).ToStringAsync();
        var path = Util.LoadGnssLog(txt.Split('\n'), p => p.Id is "2" or "4" or "5");
        if (path is not null)
        {
            _entrancePath = new(path.Points, "8705");
            _entrancePathInput = e.File.Name;
        }
        else
        {
            _entrancePathInput = "input source is invalid";
        }
    }

    public async Task InputExitPathAsync(InputFileChangeEventArgs e)
    {
        var txt = await e.File.OpenReadStream(e.File.Size).ToStringAsync();
        var path = Util.LoadGnssLog(txt.Split('\n'), p => p.Id is "2" or "4" or "5");
        if (path is not null)
        {
            _exitPath = new(path.Points, "9215");
            _exitPathInput = e.File.Name;
        }
        else
        {
            _exitPathInput = "input source is invalid";
        }
    }

    public async Task InputStartPointAsync(InputFileChangeEventArgs e)
    {
        var txt = await e.File.OpenReadStream(e.File.Size).ToStringAsync();
        var path = Util.LoadGnssLog(txt.Split('\n'), p => p.Id is "4");
        if (path is not null)
        {
            _workingStartPoint = path.Points.First();
            _workingStartPointInput = e.File.Name;
        }
        else
        {
            _workingStartPointInput = "input source is invalid";
        }
    }

    public async Task InputEndPointAsync(InputFileChangeEventArgs e)
    {
        var txt = await e.File.OpenReadStream(e.File.Size).ToStringAsync();
        var path = Util.LoadGnssLog(txt.Split('\n'), p => p.Id is "4");
        if (path is not null)
        {
            _workingEndPoint = path.Points.First();
            _workingEndPointInput = e.File.Name;
        }
        else
        {
            _workingEndPointInput = "input source is invalid";
        }
    }

    public bool CanConfigure()
    {
        // まず数値入力かどうかの確認
        var start = _workingStartPointInput.Split(",");
        var end = _workingEndPointInput.Split(",");
        if (start.Length is 2 && end.Length is 2 &&
            double.TryParse(start[0], out var sLat) &&
            double.TryParse(start[1], out var sLon) &&
            double.TryParse(end[0], out var eLat) &&
            double.TryParse(end[1], out var eLon)
        )
        {
            _workingStartPoint = new(sLat, sLon);
            _workingEndPoint = new(eLat, eLon);
        }

        var entExist = _entrancePath is not null && _entrancePath.Points.Length > 1;
        var extExist = _exitPath is not null && _exitPath.Points.Length > 1;
        var wrkExist = _workingStartPoint is not null && _workingEndPoint is not null;
        return entExist || extExist || wrkExist;
    }

    public async Task ConfigureAsync()
    {
        _uiDisabled = true;
        _editor = await Editor.CreateAsync(
            false, 1, 2.64, 0.5,
            _entrancePath!, _exitPath!, _workingStartPoint!, _workingEndPoint!
        );
        _uiDisabled = false;
        _entrancePathInput = "";
        _entrancePath = null;
        _exitPathInput = "";
        _exitPath = null;
        _workingStartPointInput = "";
        _workingStartPoint = null;
        _workingEndPointInput = "";
        _workingEndPoint = null;
        _openedMenuNumber = 3;
        await ArcGisRuntime.SetCenter(_editor.EditingPath.Points[0].Longitude, _editor.EditingPath.Points[0].Latitude);
        Util.ChangeNotifier.OnNext(null);
    }


    // ---------- load map file --------- //

    public async Task LoadMapFileAsync(InputFileChangeEventArgs e)
    {
        _uiDisabled = true;
        _loadingMapFileName = e.File.Name;
        var txt = await e.File.OpenReadStream().ToStringAsync();
        if (Path.GetExtension(_loadingMapFileName) is ".jmap")
            _editor = await Editor.CreateAsync(Util.LoadJmap(txt)!, 0.5);
        else
            _editor = await Editor.CreateAsync(Util.LoadMap(txt)!, 0.5);
        _directionRight = _editor.PathDirection is Direction.Right;
        _workingPathCount = _editor.WorkingPaths.Count;
        _workingWidth = _editor.WorkingWidth;
        _pointsInterval = _editor.PointsInterval;
        _uiDisabled = false;
        _openedMenuNumber = 3;
        await ArcGisRuntime.SetCenter(_editor.EditingPath.Points[0].Longitude, _editor.EditingPath.Points[0].Latitude);
        Util.ChangeNotifier.OnNext(null);
    }


    // ---------- edit detail and export ---------- //


    public async Task SelectPathAsync(WgsPathData path)
    {
        _uiDisabled = true;
        if (path.Id != _editor?.EditingPath.Id)
            await _editor.SelectPathAsync(path);
        _uiDisabled = false;
        Util.ChangeNotifier.OnNext(null);
    }

    public async Task DeletePathAsync(WgsPathData path)
    {
        _uiDisabled = true;
        if (path.Id != _editor?.EditingPath.Id)
            await _editor.DeletePathAsync(path);
        _workingPathCount = _editor.WorkingPaths.Count;
        _uiDisabled = false;
        Util.ChangeNotifier.OnNext(null);
    }

    public bool CheckIfSelected(string pathId)
    {
        return _editor?.EditingPath.Id == pathId;
    }

    public async Task PreviewAsync()
    {
        _uiDisabled = true;
        await _editor.PreviewAsync(
            _directionRight, 
            _workingPathCount, 
            _workingWidth, 
            _pointsInterval
        );
        _uiDisabled = false;
        Util.ChangeNotifier.OnNext(null);
    }

    public async Task GenerateAsync()
    {
        _uiDisabled = true;
        await _editor.PreviewAsync(
            _directionRight,
            _workingPathCount,
            _workingWidth,
            _pointsInterval
        );
        var paths = new List<WgsPathData>();
        if (_editor.EntrancePath is not null && _editor.EntrancePath.Points.Length > 0)
            paths.Add(_editor.EntrancePath);
        if (_editor.WorkingPaths.Any())
            paths.AddRange(_editor!.WorkingPaths);
        if (_editor.ExitPath is not null && _editor.ExitPath.Points.Length > 0)
            paths.Add(_editor.ExitPath);
        var direction = _editor.WorkingPaths.Count > 0 ? _editor.PathDirection : Direction.Left;
        var map = new WgsMapData(paths, _mapSaveFileName);
        MemoryStream ms;
        DotNetStreamReference streamRef;
        if (_exportJmap)
        {
            ms = new(Encoding.UTF8.GetBytes(Util.ToJmap(map)));
            streamRef = new(ms);
            await Js.InvokeVoidAsync("downloadFileFromStream", $"{map.Name}.jmap", streamRef);
        }
        else
        {
            ms = new(Encoding.UTF8.GetBytes(Util.ToMap(map)));
            streamRef = new(ms);
            await Js.InvokeVoidAsync("downloadFileFromStream", $"{map.Name}.map", streamRef);
            ms = new(Encoding.UTF8.GetBytes(Util.ToPln(map, _workingWidth, direction)));
            streamRef = new(ms);
            await Js.InvokeVoidAsync("downloadFileFromStream", $"{map.Name}.pln", streamRef);
        }
        ms.Dispose();
        streamRef.Dispose();
        _uiDisabled = false;
        Util.ChangeNotifier.OnNext(null);
    }

}
