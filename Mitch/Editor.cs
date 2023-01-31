using Husty.Geometry;
using VePack.Navigation;
using Gis = Mitch.ArcGisRuntime;

namespace Mitch;

public enum Direction { Left, Right }

public record class Editor(
    WgsMapData ConfiguredMap,
    Direction PathDirection,
    Dictionary<string, int> StartAttributes,
    Dictionary<string, int> EndAttributes,
    Dictionary<string, bool> ReverseAttributes,
    double WorkingWidth,
    double PointsInterval
)
{
    public WgsPathData EditingPath { set; get; }

    public WgsPathData EntrancePath { set; get; }

    public WgsPathData ExitPath { set; get; }

    public List<WgsPathData> WorkingPaths { set; get; }

    public List<string> HiddenKeys { set; get; } = new();

    public static async Task<Editor> CreateAsync(
        bool directionRight,
        int workingPathCount,
        double workingWidth,
        double pointsInterval,
        WgsPathData entrancePath,
        WgsPathData exitPath,
        WgsPointData workingStartPoint,
        WgsPointData workingEndPoint
    )
    {

        var direction = directionRight ? Direction.Right : Direction.Left;
        var workingPaths = new List<WgsPathData>();
        if (workingStartPoint is not null && workingEndPoint is not null)
        {
            var segment = Util.CreateLineSegmentPathBetweenTwoPoints(workingStartPoint, workingEndPoint, pointsInterval);
            var segmentList = Util.DuplicateLineSegmentPaths(segment, direction, workingWidth, workingPathCount);
            var num = 115459;
            foreach (var s in segmentList)
            {
                workingPaths.Add(new(s.ToArray(), $"{num}"));
                num += 2;
            }
        }

        return await CreateAsync(
            direction, workingWidth, pointsInterval,
            entrancePath, exitPath, new(workingPaths)
        );
    }

    public static async Task<Editor> CreateAsync(WgsMapData map, double pointsInterval)
    {

        var entrancePath = Util.CreateFreeCurvePath(map.GetEntrancePath(), pointsInterval) ?? null;
        var exitPath = Util.CreateFreeCurvePath(map.GetExitPath(), pointsInterval) ?? null;
        var workingPaths = new List<WgsPathData>();
        foreach (var w in map.GetWorkingPaths())
            workingPaths.Add(Util.CreateFreeCurvePath(w, pointsInterval));

        var workingWidth = 0.0;
        var direction = Direction.Left;

        if (workingPaths.Count > 1 && workingPaths[0].Points.Length > 0 && workingPaths[1].Points.Length > 0)
        {
            var gPoints0 = new List<WgsPointData>();
            gPoints0.AddRange(workingPaths[0].Points);
            var gPoints1 = new List<WgsPointData>();
            gPoints1.AddRange(workingPaths[1].Points);
            var wPoints0 = gPoints0.Select(p => p.ToUtm()).ToList();
            var wPoints1 = gPoints1.Select(p => p.ToUtm()).ToList();
            var eye = new Vector2D(wPoints0[0], wPoints0[1]).UnitVector;
            var minDist = int.MaxValue;
            var deg = 0.0;
            for (int i = 0; i < workingPaths[1].Points.Length; i++)
            {
                for (int j = 0; j < workingPaths[0].Points.Length; j++)
                {
                    var dist = (int)(wPoints0[j].DistanceTo(wPoints1[i]) * 100);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        var normal = new Vector2D(wPoints0[j], wPoints1[i]).UnitVector;
                        deg = eye.GetClockwiseAngleFrom(normal).Degree;
                    }
                }
            }
            workingWidth = minDist / 100.0;
            if (deg < 0)
                direction = Direction.Right;
        }

        if (workingPaths.Count < 2)
            workingWidth = 2.64;

        return await CreateAsync(
            direction, workingWidth, pointsInterval,
            entrancePath, exitPath, workingPaths
        );
    }


    private static async Task<Editor> CreateAsync(
        Direction direction,
        double workingWidth,
        double pointsInterval,
        WgsPathData entrancePath,
        WgsPathData returnPath,
        List<WgsPathData> workingPaths
    )
    {

        var startAttributes = new Dictionary<string, int>();
        var endAttributes = new Dictionary<string, int>();
        var reverseAttributes = new Dictionary<string, bool>();
        var pathCodes = new List<string>();

        await Gis.ClearMap();
        foreach (var path in workingPaths)
        {
            startAttributes.Add(path.Id, 0);
            endAttributes.Add(path.Id, 0);
            reverseAttributes.Add(path.Id, false);
            pathCodes.Add(path.Id);
            await Gis.SetPath(path, Colors.WorkingPathColor);
        }
        if (entrancePath is not null && entrancePath.Points.Length > 0)
        {
            startAttributes.Add(entrancePath.Id, 0);
            endAttributes.Add(entrancePath.Id, 0);
            reverseAttributes.Add(entrancePath.Id, false);
            pathCodes.Add(entrancePath.Id);
            entrancePath = Util.CreateFreeCurvePath(entrancePath, pointsInterval);
            await Gis.SetPath(entrancePath, Colors.EntrancePathColor);
        }
        if (returnPath is not null && returnPath.Points.Length > 0)
        {
            startAttributes.Add(returnPath.Id, 0);
            endAttributes.Add(returnPath.Id, 0);
            reverseAttributes.Add(returnPath.Id, false);
            pathCodes.Add(returnPath.Id);
            returnPath = Util.CreateFreeCurvePath(returnPath, pointsInterval);
            await Gis.SetPath(returnPath, Colors.ExitPathColor);
        }

        WgsPathData editingPath;
        if (workingPaths.Count > 0)
            editingPath = workingPaths[0].Clone();
        else if (entrancePath is not null && entrancePath.Points.Length > 0)
            editingPath = entrancePath;
        else
            editingPath = returnPath;
        await Gis.RemovePath(editingPath);
        await Gis.SetPath(editingPath, Colors.ActivePathColor);

        var paths = new List<WgsPathData>();
        paths.Add(entrancePath);
        paths.AddRange(new List<WgsPathData>(workingPaths));
        paths.Add(returnPath);
        var configuredMap = new WgsMapData("", paths);

        return new(
            configuredMap, direction, startAttributes, endAttributes, reverseAttributes,
            workingWidth, pointsInterval
        )
        {
            EditingPath = editingPath,
            EntrancePath = entrancePath,
            ExitPath = returnPath,
            WorkingPaths = workingPaths
        };
    }

    public async Task SelectPathAsync(WgsPathData path)
    {
        await Gis.RemovePath(path);
        await Gis.RemovePath(EditingPath);
        await Gis.SetPath(path, Colors.ActivePathColor);
        if (EditingPath.Id is "8705")
            await Gis.SetPath(EditingPath, Colors.EntrancePathColor);
        else if (EditingPath.Id is "9215")
            await Gis.SetPath(EditingPath, Colors.ExitPathColor);
        else
            await Gis.SetPath(EditingPath, Colors.WorkingPathColor);
        EditingPath = path.Clone();
    }

    public async Task DeletePathAsync(WgsPathData path)
    {
        await Gis.RemovePath(path);
        HiddenKeys.Add(path.Id);
        WorkingPaths = WorkingPaths.Where(p => !HiddenKeys.Contains(p.Id)).ToList();
    }

    public async Task PreviewAsync(
        bool directionRight,
        int workingPathCount,
        double workingWidth,
        double pointsInterval
    )
    {
        var direction = directionRight ? Direction.Right : Direction.Left;
        var rootPaths = ConfiguredMap.GetWorkingPaths();
        var workingPaths = rootPaths.Where(p => !HiddenKeys.Contains(p.Id)).ToList();

        foreach (var wp in WorkingPaths)
        {
            if (workingPaths.All(p => p.Id != wp.Id))
            {
                var index = (int.Parse(wp.Id) - int.Parse(rootPaths[0].Id)) * 0.5;
                var path = new WgsPathData(rootPaths[0].Points.DuplicateLineSegmentPaths(PathDirection, WorkingWidth * index, 2)[1], wp.Id);
                workingPaths.Add(path);
            }
        }

        var num = int.Parse(workingPaths[^1].Id);
        var additionalCount = workingPathCount - workingPaths.Count;
        for (int i = 0; i < additionalCount; i++)
        {
            do { num += 2; }
            while (HiddenKeys.Contains($"{num}"));
            var index = (num - int.Parse(rootPaths[0].Id)) * 0.5;
            var p = rootPaths[0].Points.DuplicateLineSegmentPaths(PathDirection, WorkingWidth * index, 2)[1];
            workingPaths.Add(new(p, $"{num}"));
        }

        if (direction != PathDirection || workingWidth != WorkingWidth || pointsInterval != PointsInterval)
        {
            for (int i = 0; i < workingPaths.Count; i++)
            {
                if (HiddenKeys.Contains(workingPaths[i].Id)) continue;
                var index = (int.Parse(workingPaths[i].Id) - int.Parse(rootPaths[0].Id)) * 0.5;
                var w = direction == PathDirection
                    ? index * (workingWidth - WorkingWidth)
                    : index * (workingWidth + WorkingWidth);
                workingPaths[i] = new(
                    workingPaths[i].Points.DuplicateLineSegmentPaths(direction, w, 2)[1].ToArray(),
                    workingPaths[i].Id
                );
                if (pointsInterval != PointsInterval)
                    workingPaths[i] = new(Util.CreateLineSegmentPathBetweenTwoPoints(
                        workingPaths[i].Points[0], workingPaths[i].Points[^1], pointsInterval),
                        workingPaths[i].Id
                    );
            }
        }

        var pathCodes = new List<string>();
        foreach (var path in workingPaths)
        {
            pathCodes.Add(path.Id);
            if (!StartAttributes.ContainsKey(path.Id))
            {
                StartAttributes.Add(path.Id, 0);
                EndAttributes.Add(path.Id, 0);
                ReverseAttributes.Add(path.Id, false);
            }
        }

        var keys = StartAttributes.Keys;
        foreach (var key in keys)
        {
            if (!pathCodes.Contains(key) && HiddenKeys.Contains(key))
            {
                WorkingPaths.Remove(WorkingPaths.Where(x => x.Id == key).FirstOrDefault());
                StartAttributes.Remove(key);
                EndAttributes.Remove(key);
                ReverseAttributes.Remove(key);
            }
        }

        var configuredMap = ConfiguredMap;
        var entrancePath = HiddenKeys.Contains("8705") ? null : configuredMap.GetEntrancePath();
        var exitPath = HiddenKeys.Contains("9215") ? null : configuredMap.GetExitPath();

        if (entrancePath is not null)
        {
            pathCodes.Add(entrancePath.Id);
            entrancePath = Util.CreateFreeCurvePath(entrancePath, pointsInterval);
        }
        if (exitPath is not null)
        {
            pathCodes.Add(exitPath.Id);
            exitPath = Util.CreateFreeCurvePath(exitPath, pointsInterval);
        }

        foreach (var p in ReverseAttributes)
        {
            if (p.Value)
            {
                if (p.Key is "8705")
                {
                    entrancePath = entrancePath?.GetReverse();
                }
                else if (p.Key is "9215")
                {
                    exitPath = exitPath?.GetReverse();
                }
                else
                {
                    for (int i = 0; i < workingPaths.Count; i++)
                    {
                        if (workingPaths[i].Id == p.Key)
                        {
                            workingPaths[i] = workingPaths[i].GetReverse();
                        }
                    }
                }
            }
        }
        foreach (var p in StartAttributes)
        {
            if (p.Key is "8705")
            {
                if (p.Value < 0)
                    for (int i = 0; i < -p.Value; i++)
                        entrancePath?.ShortenFirst(pointsInterval);
                else if (p.Value > 0)
                    for (int i = 0; i < p.Value; i++)
                        entrancePath?.ExtendFirst(pointsInterval);
            }
            else if (p.Key is "9215")
            {
                if (p.Value < 0)
                    for (int i = 0; i < -p.Value; i++)
                        exitPath?.ShortenFirst(pointsInterval);
                else if (p.Value > 0)
                    for (int i = 0; i < p.Value; i++)
                        exitPath?.ExtendFirst(pointsInterval);
            }
            else
            {
                for (int i = 0; i < workingPaths.Count; i++)
                {
                    if (workingPaths[i].Id == p.Key)
                    {
                        if (p.Value < 0)
                            for (int j = 0; j < -p.Value; j++)
                                workingPaths[i].ShortenFirst(pointsInterval);
                        else if (p.Value > 0)
                            for (int j = 0; j < p.Value; j++)
                                workingPaths[i].ExtendFirst(pointsInterval);
                    }
                }
            }

        }
        foreach (var p in EndAttributes)
        {
            if (p.Key is "8705")
            {
                if (p.Value < 0)
                    for (int i = 0; i < -p.Value; i++)
                        entrancePath?.ShortenLast(pointsInterval);
                else if (p.Value > 0)
                    for (int i = 0; i < p.Value; i++)
                        entrancePath?.ExtendLast(pointsInterval);
            }
            else if (p.Key is "9215")
            {
                if (p.Value < 0)
                    for (int i = 0; i < -p.Value; i++)
                        exitPath?.ShortenLast(pointsInterval);
                else if (p.Value > 0)
                    for (int i = 0; i < p.Value; i++)
                        exitPath?.ExtendLast(pointsInterval);
            }
            else
            {
                for (int i = 0; i < workingPaths.Count; i++)
                {
                    if (workingPaths[i].Id == p.Key)
                    {
                        if (p.Value < 0)
                            for (int j = 0; j < -p.Value; j++)
                                workingPaths[i].ShortenLast(pointsInterval);
                        else if (p.Value > 0)
                            for (int j = 0; j < p.Value; j++)
                                workingPaths[i].ExtendLast(pointsInterval);
                    }
                }
            }
        }

        await Gis.ClearMap();
        foreach (var code in pathCodes)
        {
            if (code is "8705")
            {
                if (code == EditingPath.Id)
                    EditingPath = entrancePath.Clone();
                else
                    await Gis.SetPath(entrancePath, Colors.EntrancePathColor);
            }
            else if (code is "9215")
            {
                if (code == EditingPath.Id)
                    EditingPath = exitPath.Clone();
                else
                    await Gis.SetPath(exitPath, Colors.ExitPathColor);
            }
            else
            {
                for (int i = 0; i < workingPaths.Count; i++)
                {
                    if (code == workingPaths[i].Id)
                    {
                        if (code == EditingPath.Id)
                            EditingPath = workingPaths[i].Clone();
                        else
                            await Gis.SetPath(workingPaths[i], Colors.WorkingPathColor);
                    }

                }
            }
        }
        EntrancePath = entrancePath;
        ExitPath = exitPath;
        WorkingPaths = workingPaths;
        await Gis.SetPath(EditingPath, Colors.ActivePathColor);
    }

}
