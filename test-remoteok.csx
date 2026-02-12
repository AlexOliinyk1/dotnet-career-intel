#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Extensions.Logging, 10.0.3"
#r "nuget: Microsoft.Extensions.Logging.Console, 10.0.3"

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

// Simple test to verify RemoteOK API works
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

Console.WriteLine("Testing RemoteOK API...");
Console.WriteLine("======================");
Console.WriteLine("");

try
{
    var json = await httpClient.GetStringAsync("https://remoteok.com/api");

    if (!string.IsNullOrEmpty(json))
    {
        // Count jobs in response
        var jobCount = json.Split("\"id\":").Length - 1;

        Console.WriteLine($"✓ API Response received!");
        Console.WriteLine($"✓ Found ~{jobCount} jobs in response");
        Console.WriteLine("");
        Console.WriteLine("Sample data:");
        Console.WriteLine(json.Substring(0, Math.Min(500, json.Length)) + "...");
    }
    else
    {
        Console.WriteLine("✗ Empty response from API");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
}
