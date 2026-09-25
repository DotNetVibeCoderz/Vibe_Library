// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Samples;

// dotnet run --project samples/APL.Net.Samples -c Release              -> both
// dotnet run --project samples/APL.Net.Samples -c Release -- quickstart
// dotnet run --project samples/APL.Net.Samples -c Release -- image

Console.WriteLine("APL.Net samples - Gravicode Studios, led by Kang Fadhil");
Console.WriteLine();

string which = args.Length > 0 ? args[0] : "all";
if (which is "all" or "quickstart")
    QuickStart.Run();
if (which is "all" or "image")
    ImageProcessingDemo.Run();
