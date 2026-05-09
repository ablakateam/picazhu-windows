using Picazhu.Cache;
using Picazhu.Core;
using Picazhu.Media;

if (args.Length == 0)
{
    Console.WriteLine("Usage: HeicProbe <path-to-heic>");
    return;
}

var path = args[0];
var appPaths = new AppPaths();
var service = new HeicDecoderService(appPaths);

await service.InitializeAsync([path]);
var diagnostics = service.GetDiagnostics();
Console.WriteLine($"Diagnostics: active={diagnostics.ActivePath}; nativeDetected={diagnostics.NativeWicDetected}; nativeHealthy={diagnostics.NativeWicHealthy}; libheifRegistered={diagnostics.LibheifRegistered}");
Console.WriteLine($"Summary: {diagnostics.Summary}");
Console.WriteLine($"LastError: {diagnostics.LastError}");

var probe = await service.ProbeAsync(path);
Console.WriteLine(probe is null
    ? "Probe: null"
    : $"Probe: {probe.Value.Width}x{probe.Value.Height} orientation={probe.Value.Orientation}");

var tempOutput = Path.Combine(Environment.CurrentDirectory, "heic-probe.jpg");
var ok = await service.TryCreateThumbnailAsync(path, tempOutput, 640, 480);
Console.WriteLine($"ThumbnailOk: {ok}");
Console.WriteLine($"ThumbnailPath: {tempOutput}");
Console.WriteLine($"ThumbnailExists: {File.Exists(tempOutput)}");
Console.WriteLine($"PostLastError: {service.GetDiagnostics().LastError}");
