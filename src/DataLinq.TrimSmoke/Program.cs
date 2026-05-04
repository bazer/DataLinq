using DataLinq.PlatformCompatibility.Smoke;

var result = PlatformSmokeRunner.Run();
Console.WriteLine(result.ToDisplayString());

return result.Passed ? 0 : 1;
