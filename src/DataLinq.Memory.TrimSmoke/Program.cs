using DataLinq.Memory.PlatformCompatibility.Smoke;

var result = MemoryPlatformSmokeRunner.Run();
Console.WriteLine(result.ToDisplayString());

return result.Passed ? 0 : 1;
