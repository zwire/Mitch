using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using Husty.Geometry;
using VePack.Guidance;

namespace Mitch;

public static class Util
{

    public static Subject<object?> ChangeNotifier { get; } = new();

    public static async Task<string> ToStringAsync(this Stream stream)
    {
        var bytes = new byte[stream.Length];
        await stream.ReadAsync(bytes, 0, (int)stream.Length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static WgsPathData? ExtractPathFromNmeaSentence(
        IEnumerable<string> lines,
        Func<WgsPointData, bool>? filter = default
    )
    {
        filter ??= p => true;
        if (lines is null) return null;
        var points = new List<WgsPointData>();
        foreach (var line in lines)
        {
            if (line is null) continue;
            var words = line.Split(',', '\t');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Contains("GGA"))
                {
                    words = words.Skip(i).ToArray();
                    break;
                }
            }
            if (words.Length < 7) continue;
            if (double.TryParse(words[2], out var latdmm) &&
                double.TryParse(words[4], out var londmm) &&
                int.TryParse(words[6], out var status)
            )
            {
                var sgnNS = words[3] is "S" ? -1.0 : 1.0;
                var lat = sgnNS * ((latdmm - (int)(latdmm / 100) * 100) / 60 + (int)(latdmm / 100));
                var sgnEW = words[5] is "W" ? -1.0 : 1.0;
                var lon = sgnEW * ((londmm - (int)(londmm / 100) * 100) / 60 + (int)(londmm / 100));
                var p = new WgsPointData(lat, lon, status.ToString());
                if (filter(p)) points.Add(p);
            }
        }
        if (points.Any())
            return new(points.ToArray());
        return null;
    }

    public static WgsPathData? ExtractPathFromCsvWithHeader(
        string[] lines,
        Func<WgsPointData, bool>? filter = default
    )
    {
        filter ??= p => true;
        var points = new List<WgsPointData>();
        var header = lines[0].Split(",");
        var statusPos = -1;
        var latPos = -1;
        var lonPos = -1;
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i].ToLower().Contains("status")) statusPos = i;
            else if (header[i].ToLower().Contains("lat")) latPos = i;
            else if (header[i].ToLower().Contains("lon")) lonPos = i;
        }
        if (statusPos is not -1 && latPos is not -1 && lonPos is not -1)
        {
            foreach (var line in lines)
            {
                var strs = line.Split(",");
                if (statusPos < strs.Length && latPos < strs.Length && lonPos < strs.Length)
                {
                    if (int.TryParse(strs[statusPos], out var status) &&
                        double.TryParse(strs[latPos], out var lat) && 
                        double.TryParse(strs[lonPos], out var lon)
                    )
                    {
                        var p = new WgsPointData(lat, lon, status.ToString());
                        if (filter(p))
                            points.Add(p);
                    }
                }
            }
        }
        if (points.Any())
            return new(points.ToArray());
        return null;
    }

    public static WgsPathData? LoadGnssLog(
        string[] lines,
        Func<WgsPointData, bool>? filter = default
    )
    {
        if (ExtractPathFromNmeaSentence(lines, filter) is WgsPathData p0) return p0;
        if (ExtractPathFromCsvWithHeader(lines, filter) is WgsPathData p1) return p1;
        return null;
    }

    public static string ToMap(this WgsMapData map)
    {
        var output = "";
        foreach (var path in map.Paths)
            foreach (var p in path.Points)
                output += $"{p.Latitude:f8}\t{p.Longitude:f8}\t{path.Id}\n";
        return output;
    }

    public static string ToPln(
        WgsMapData map,
        double width, 
        Direction direction
    )
    {
        var entrancePathExists = map.Paths.FirstOrDefault()?.Id is "8705";
        var exitPathExists = map.Paths.LastOrDefault()?.Id is "9215";
        var workingPathCount = map.Paths.Count;
        if (entrancePathExists)
            workingPathCount--;
        if (exitPathExists)
            workingPathCount--;
        if (direction is Direction.Right)
            width *= -1.0;
        var output = "";
        output += "Working plan for cultivating\n";
        output += $"{map.Name}.map\t;navigation map name\n";
        output += $"{width:f2}\t;Work width [m]\n";
        output += $"{workingPathCount}\t;Total path number\n";
        if (entrancePathExists)
            output += "0\t";
        for (int i = 0; i < workingPathCount; i++)
            output += $"{i + 1}\t";
        if (exitPathExists)
            output += "127\t";
        output += ";Order of travel path\n";
        output += "0\t0\t4\t0\t0\t;Code of approach guidance\n";
        output += "PTO(ON=1), hitch(Down=1), transmission(1-8), engine speed(MAX=1), Working flag(Working=1) :See Coding.h\n";
        output += "0.000000\t;Offset\n";
        return output;
    }

    public static string ToJmap(WgsMapData map)
    {
        var paths = map.Paths;
        using var sw = new StreamWriter(map.Name + ".jmap");
        paths = paths.Select(path => path.Id switch
        {
            "8705" => new WgsPathData(path.Points.Select(p => new WgsPointData(p.Latitude, p.Longitude)).ToArray(), "Transfer"),
            "9215" => new WgsPathData(path.Points.Select(p => new WgsPointData(p.Latitude, p.Longitude)).ToArray(), "Transfer"),
            _ => new WgsPathData(path.Points.Select(p => new WgsPointData(p.Latitude, p.Longitude)).ToArray(), "Work")
        }).ToList();
        var jmap = new JmapData(Enumerable.Range(1, paths.Count).ToArray(), new WgsMapData(paths, map.Name));
        return JsonSerializer.Serialize(jmap, new JsonSerializerOptions() { WriteIndented = true });
    }

    public static WgsMapData? LoadMap(string txt)
    {
        var current = 115459;
        var target = 115461;
        var outwardPoints = new List<WgsPointData>();
        var returnPoints = new List<WgsPointData>();
        var oneWorkingPoints = new List<WgsPointData>();
        var workingPaths = new List<WgsPathData>();
        foreach (var line in txt.Split('\n'))
        {
            var strs = line.Split("\t");
            if (strs.Length > 2
                && double.TryParse(strs[0], out var lat)
                && double.TryParse(strs[1], out var lon)
                && int.TryParse(strs[2], out var code))
            {
                if (code is 8705)
                {
                    outwardPoints.Add(new(lat, lon));
                }
                else if (code is 9215)
                {
                    returnPoints.Add(new(lat, lon));
                }
                else if (code == current)
                {
                    oneWorkingPoints.Add(new(lat, lon));
                }
                else if (code == target)
                {
                    workingPaths.Add(new(oneWorkingPoints.ToArray(), $"{current}"));
                    current = target;
                    target += 2;
                    oneWorkingPoints.Clear();
                    oneWorkingPoints.Add(new(lat, lon));
                }
            }
        }
        if (oneWorkingPoints.Count > 0)
            workingPaths.Add(new(oneWorkingPoints.ToArray(), $"{current}"));

        var paths = new List<WgsPathData>();
        if (outwardPoints.Count > 0)
            paths.Add(new(outwardPoints.ToArray(), "8705"));
        if (workingPaths.Count > 0)
            paths.AddRange(workingPaths);
        if (returnPoints.Count > 0)
            paths.Add(new(returnPoints.ToArray(), "9215"));
        if (paths.Any())
            return new(paths, "");
        return null;
    }

    public static WgsMapData? LoadJmap(string txt)
    {
        var jmap = JsonSerializer.Deserialize<JmapData>(txt);
        var ps = jmap.Map.Paths;
        var outwardPath = ps[0].Id is "Transfer" ? ps[0] : null;
        var returnPath = ps.Count > 1 && ps[^1].Id is "Transfer" ? ps[^1] : null;
        ps = ps.Where(p => p.Id is "Work").ToList();

        var workingPathCode = 115459;
        var oneWorkingPoints = new List<WgsPointData>();
        var workingPaths = new List<WgsPathData>();
        ps = ps.Where(p => p.Id is "Work").ToList();
        foreach (var p in ps)
        {
            workingPaths.Add(new(p.Points, $"{workingPathCode}"));
            workingPathCode += 2;
        }
        var paths = new List<WgsPathData>();
        if (outwardPath is not null)
            paths.Add(new(outwardPath.Points.ToArray(), "8705"));
        if (workingPaths.Count > 0)
            paths.AddRange(workingPaths);
        if (returnPath is not null)
            paths.Add(new(returnPath.Points.ToArray(), "9215"));
        return new WgsMapData(paths, "");
    }

    public static WgsPathData CreateFreeCurvePath(WgsPathData path, double interval)
    {
        if (path is null || path.Points.Length is 0) return null;
        var utms = path.ToUtm();
        var outputWPoints = new List<UtmPointData>() { utms.Points[0] };
        for (int i = 1; i < utms.Points.Length; i++)
        {
            var p = utms.Points[i];
            var dist = p.DistanceTo(outputWPoints[^1]);
            if (dist > interval / 2)
                outputWPoints.Add(p);
        }
        var wgss = outputWPoints.Select(p => p.ToWgs()).ToArray();
        return new(wgss, path.Id);
    }

    public static WgsPointData[] CreateLineSegmentPathBetweenTwoPoints(WgsPointData Start, WgsPointData End, double interval)
    {
        var path = new WgsPathData(new List<WgsPointData>() { Start, End }.ToArray(), "");
        return path.ToUtm().AdjastPathPointsInterval(interval).ToWgs().Points;
    }

    public static WgsPointData[][] DuplicateLineSegmentPaths(this IEnumerable<WgsPointData> path, Direction direction, double width, int count)
    {
        var utms = path.Select(p => p.ToUtm()).ToArray();
        var eigenVec = new Vector2D(utms[0], utms[^1]).UnitVector;
        var normalVec = direction is Direction.Left ? eigenVec.Rotate(Angle.FromDegree(90)) : eigenVec.Rotate(Angle.FromDegree(-90));
        var ps = new WgsPointData[count][];
        ps[0] = path.ToArray();
        for (int i = 1; i < count; i++)
        {
            var wgs = new WgsPointData[utms.Length];
            for (int j = 0; j < utms.Length; j++)
            {
                var x = utms[j].X + normalVec.X * width * i;
                var y = utms[j].Y + normalVec.Y * width * i;
                wgs[j] = new UtmPointData(x, y).ToWgs();
            }
            ps[i] = wgs;
        }
        return ps;
    }

    public static WgsPathData GetEntrancePath(this WgsMapData map)
        => map.Paths
            .Where(x => x?.Id is "8705")
            .Where(x => x is not null)
            .Select(x => x.Clone())
            .FirstOrDefault();

    public static WgsPathData GetExitPath(this WgsMapData map)
        => map.Paths
            .Where(x => x?.Id is "9215")
            .Where(x => x is not null)
            .Select(x => x.Clone())
            .LastOrDefault();

    public static WgsPathData[] GetWorkingPaths(this WgsMapData map)
       => map.Paths
            .Where(x => x?.Id is not "8705" && x?.Id is not "9215")
            .Where(x => x is not null)
            .Select(x => x.Clone())
            .ToArray();

}

public struct Colors
{

    public static List<int> EntrancePathColor { set; get; } = new() { 60, 180, 60 };

    public static List<int> ExitPathColor { set; get; } = new() { 20, 190, 255 };

    public static List<int> WorkingPathColor { set; get; } = new() { 255, 50, 50 };

    public static List<int> ActivePathColor { set; get; } = new() { 255, 255, 0 };

}