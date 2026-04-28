namespace TestingPatterns;

using System;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

public class TraceFixture : IDisposable
{
    private bool disposedValue;
    internal static readonly AssemblyName AssemblyName = typeof(TraceFixture).Assembly.GetName();
    internal static readonly string ActivitySourceName = AssemblyName.Name!;
    internal static readonly Version Version = AssemblyName.Version!;
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version.ToString());

    public TracerProvider Provider { get; }

    public TraceFixture()
    {
        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("FunctionDddExample"))
            .AddResultsInstrumentation()
            .AddSource(ActivitySourceName)
            .AddOtlpExporter();

        Provider = builder.Build();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Provider.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}